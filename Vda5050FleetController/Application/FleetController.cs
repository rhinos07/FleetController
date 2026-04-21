using Microsoft.Extensions.Logging;
using Vda5050FleetController.Application.Contracts;
using Vda5050FleetController.Domain.Models;
using Vda5050FleetController.Infrastructure.Mqtt;

namespace Vda5050FleetController.Application;

public class FleetController
{
    private readonly VehicleRegistry          _registry;
    private readonly TransportOrderQueue      _queue;
    private readonly TopologyMap              _topology;
    private readonly IVda5050MqttService      _mqtt;
    private readonly IFleetStatusPublisher    _statusPublisher;
    private readonly IFleetPersistenceService _persistence;
    private readonly VehicleDispatcher        _dispatcher;
    private readonly ILogger<FleetController> _log;

    public FleetController(
        VehicleRegistry           registry,
        TransportOrderQueue       queue,
        TopologyMap               topology,
        IVda5050MqttService       mqtt,
        IFleetStatusPublisher?    statusPublisher,
        IFleetPersistenceService? persistence,
        VehicleDispatcher         dispatcher,
        ILogger<FleetController>  log)
    {
        _registry        = registry;
        _queue           = queue;
        _topology        = topology;
        _mqtt            = mqtt;
        _statusPublisher = statusPublisher ?? NoOpFleetStatusPublisher.Instance;
        _persistence     = persistence    ?? NoOpFleetPersistenceService.Instance;
        _dispatcher      = dispatcher;
        _log             = log;

        _mqtt.OnStateReceived      += HandleVehicleStateAsync;
        _mqtt.OnConnectionReceived += HandleConnectionAsync;
    }

    // ── Inbound: WMS/MFR requests a transport ────────────────────────────────

    public async Task RequestTransportAsync(string sourceStationId, string destStationId,
        string? loadId = null, CancellationToken ct = default)
    {
        var order = new TransportOrder(
            orderId:  $"TO-{Guid.NewGuid():N}"[..16],
            sourceId: sourceStationId,
            destId:   destStationId,
            loadId:   loadId
        );

        _queue.Enqueue(order);
        await _persistence.SaveOrderAsync(order, ct);
        try
        {
            await _dispatcher.TryDispatchAsync(ct);
        }
        finally
        {
            await PublishStatusAsync(ct);
        }
    }

    public async Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        if (!_queue.RemovePending(orderId))
            return false;

        await PublishStatusAsync(ct);
        return true;
    }

    public async Task<bool> UpdateOrderAsync(string orderId, string sourceStationId,
        string destStationId, string? loadId, CancellationToken ct = default)
    {
        var updated = new TransportOrder(orderId, sourceStationId, destStationId, loadId);
        if (!_queue.ReplacePending(orderId, updated))
            return false;

        await _persistence.SaveOrderAsync(updated, ct);
        await PublishStatusAsync(ct);
        return true;
    }

    // ── Inbound: Vehicle state update ────────────────────────────────────────

    private async Task HandleVehicleStateAsync(VehicleState state)
    {
        var vehicle = _registry.GetOrCreate(state.Manufacturer, state.SerialNumber);
        var wasIdle = vehicle.IsAvailable;

        // AGVs keep reporting their last orderId even after a completed order.
        // Strip it when it no longer refers to a known active order so the vehicle transitions to Idle.
        var stateForVehicle = !string.IsNullOrEmpty(state.OrderId) && _queue.FindActive(state.OrderId) is null
            ? state with { OrderId = string.Empty }
            : state;
        vehicle.ApplyState(stateForVehicle);

        foreach (var err in state.Errors)
            _log.LogWarning("Vehicle {Id} error [{Level}]: {Type} - {Desc}",
                vehicle.VehicleId, err.ErrorLevel, err.ErrorType, err.ErrorDescription);

        if (!string.IsNullOrEmpty(state.OrderId))
        {
            var activeOrder = _queue.FindActive(state.OrderId);
            if (activeOrder is not null
                && !state.NodeStates.Any()
                && !state.EdgeStates.Any()
                && !state.Driving)
            {
                _queue.Complete(state.OrderId);
                await _persistence.CompleteOrderAsync(activeOrder);
            }
        }

        await _persistence.SaveVehicleAsync(vehicle);

        // Vehicle stopped mid-path with remaining nodes → likely blocked by an idle AGV
        if (!state.Driving
            && !string.IsNullOrEmpty(state.OrderId)
            && state.NodeStates.Count > 0)
        {
            _log.LogInformation(
                "Vehicle {VehicleId} stopped mid-path with active order {OrderId}: {RemainingNodeCount} nodes remaining. Checking for blockers.",
                vehicle.VehicleId, state.OrderId, state.NodeStates.Count);
            var remainingNodeIds = state.NodeStates.Select(ns => ns.NodeId).ToList();
            await _dispatcher.TryResolveBlockersAsync(remainingNodeIds, vehicle, ct: default);
        }

        if (!wasIdle && vehicle.IsAvailable)
        {
            await _dispatcher.TryDispatchAsync();
            // Only trigger dodge if no transport order was just dispatched to this vehicle.
            // Sending a dodge order to a vehicle that already received a transport order would
            // overwrite it on the AGV (higher HeaderId wins).
            var wasDispatched = _queue.GetAllOrders()
                .Any(o => o.AssignedVehicleId == vehicle.VehicleId);
            if (!wasDispatched)
                await _dispatcher.TryUnblockVehiclesBlockedByAsync(vehicle, ct: default);
        }

        await PublishStatusAsync();
    }

    // ── Inbound: Vehicle connection event ────────────────────────────────────

    private async Task HandleConnectionAsync(ConnectionMessage msg)
    {
        var vehicle = _registry.GetOrCreate(msg.Manufacturer, msg.SerialNumber);
        vehicle.ApplyConnection(msg);

        _log.LogInformation("Vehicle {Id} connection: {State}",
            vehicle.VehicleId, msg.ConnectionState);

        await _persistence.SaveVehicleAsync(vehicle);
        await PublishStatusAsync();
    }

    // ── Control: Instant actions ─────────────────────────────────────────────

    public Task PauseVehicleAsync(string vehicleId, CancellationToken ct = default)
        => SendInstantActionAsync(vehicleId, "stopPause", ct);

    public Task ResumeVehicleAsync(string vehicleId, CancellationToken ct = default)
        => SendInstantActionAsync(vehicleId, "startPause", ct);

    public Task StartChargingAsync(string vehicleId, CancellationToken ct = default)
        => SendInstantActionAsync(vehicleId, "startCharging", ct);

    public async Task MoveVehicleAsync(string vehicleId, string destNodeId, CancellationToken ct = default)
    {
        var vehicle = _registry.Find(vehicleId)
            ?? throw new InvalidOperationException($"Vehicle {vehicleId} not found");
        if (!vehicle.IsAvailable)
            throw new InvalidOperationException($"Vehicle {vehicleId} is not available for repositioning");
        var fromNodeId = vehicle.LastNodeId
            ?? throw new InvalidOperationException($"Vehicle {vehicleId} has no known position");
        if (fromNodeId == destNodeId)
            return;

        await _dispatcher.MoveToNodeAsync(vehicle, fromNodeId, destNodeId, ct);
        _log.LogInformation("Manual reposition: vehicle {VehicleId} → {DestNodeId}", vehicleId, destNodeId);
    }

    private async Task SendInstantActionAsync(string vehicleId, string actionType, CancellationToken ct)
    {
        var vehicle = _registry.Find(vehicleId)
            ?? throw new InvalidOperationException($"Vehicle {vehicleId} not found");

        var ia = new InstantActions
        {
            HeaderId     = vehicle.NextHeaderId(),
            Manufacturer = vehicle.Manufacturer,
            SerialNumber = vehicle.SerialNumber,
            Actions =
            [
                new VdaAction
                {
                    ActionId     = $"IA-{Guid.NewGuid():N}"[..8],
                    ActionType   = actionType,
                    BlockingType = "HARD"
                }
            ]
        };

        await _mqtt.PublishInstantActionAsync(ia, ct);
    }

    // ── Status ────────────────────────────────────────────────────────────────

    public FleetStatus GetStatus() => new()
    {
        Vehicles = _registry.All().Select(v => new VehicleSummary
        {
            VehicleId = v.VehicleId,
            Status    = v.Status.ToString(),
            Position  = v.Position,
            Battery   = v.Battery?.BatteryCharge,
            OrderId   = v.CurrentOrderId,
            LastSeen  = v.LastSeen
        }).ToList(),
        PendingOrders = _queue.PendingCount,
        ActiveOrders  = _queue.ActiveCount,
        Nodes = _topology.GetAllNodes().Select(n => new TopologyNodeDto
        {
            NodeId = n.NodeId,
            X      = n.Position.X,
            Y      = n.Position.Y,
            Theta  = n.Position.Theta,
            MapId  = n.Position.MapId
        }).ToList(),
        Edges = _topology.GetAllEdges().Select(e => new TopologyEdgeDto
        {
            EdgeId = e.EdgeId,
            From   = e.From,
            To     = e.To
        }).ToList(),
        Orders = _queue.GetAllOrders().Select(o => new OrderSummary
        {
            OrderId   = o.OrderId,
            SourceId  = o.SourceId,
            DestId    = o.DestId,
            LoadId    = o.LoadId,
            Status    = o.Status.ToString(),
            VehicleId = o.AssignedVehicleId
        }).ToList()
    };

    private Task PublishStatusAsync(CancellationToken ct = default)
        => _statusPublisher.PublishAsync(GetStatus(), ct);

    public Task PublishStatusUpdateAsync(CancellationToken ct = default)
        => PublishStatusAsync(ct);
}
