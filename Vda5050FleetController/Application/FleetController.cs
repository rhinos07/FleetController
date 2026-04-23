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
    private readonly BatteryChargingSettings  _batterySettings;
    private readonly ILogger<FleetController> _log;

    // Tracks vehicles for which a charge-dispatch order has been sent but the vehicle
    // has not yet started driving (avoids duplicate dispatches in rapid state bursts).
    private readonly HashSet<string> _pendingChargeDispatches = [];

    public FleetController(
        VehicleRegistry           registry,
        TransportOrderQueue       queue,
        TopologyMap               topology,
        IVda5050MqttService       mqtt,
        IFleetStatusPublisher?    statusPublisher,
        IFleetPersistenceService? persistence,
        VehicleDispatcher         dispatcher,
        BatteryChargingSettings   batterySettings,
        ILogger<FleetController>  log)
    {
        _registry        = registry;
        _queue           = queue;
        _topology        = topology;
        _mqtt            = mqtt;
        _statusPublisher = statusPublisher ?? NoOpFleetStatusPublisher.Instance;
        _persistence     = persistence    ?? NoOpFleetPersistenceService.Instance;
        _dispatcher      = dispatcher;
        _batterySettings = batterySettings;
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

        // Once the vehicle starts driving or reaches a charging node, clear the pending dispatch flag.
        if (vehicle.Status == VehicleStatus.Driving
            || IsChargingNode(vehicle.LastNodeId))
        {
            _pendingChargeDispatches.Remove(vehicle.VehicleId);
        }

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

        await TryDispatchLowBatteryVehiclesAsync(ct: default);

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
        }).ToList(),
        LowBatteryThreshold = _batterySettings.LowBatteryThreshold
    };

    public void UpdateBatteryThreshold(double threshold)
    {
        if (threshold < 0 || threshold > 100)
            throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be between 0 and 100.");
        _batterySettings.LowBatteryThreshold = threshold;
        _log.LogInformation("Battery low-threshold updated to {Threshold}%", threshold);
    }

    private Task PublishStatusAsync(CancellationToken ct = default)
        => _statusPublisher.PublishAsync(GetStatus(), ct);

    public Task PublishStatusUpdateAsync(CancellationToken ct = default)
        => PublishStatusAsync(ct);

    // ── Battery charging dispatch ─────────────────────────────────────────────

    private static bool IsChargingNode(string? nodeId)
        => nodeId != null && nodeId.StartsWith("CHG-", StringComparison.OrdinalIgnoreCase);

    private async Task TryDispatchLowBatteryVehiclesAsync(CancellationToken ct)
    {
        var threshold = _batterySettings.LowBatteryThreshold;

        var chargingNodeIds = _topology.GetAllNodes()
            .Where(n => IsChargingNode(n.NodeId))
            .Select(n => n.NodeId)
            .ToHashSet();

        if (chargingNodeIds.Count == 0)
            return;

        // Collect vehicles that need to go to a charging station.
        var vehiclesNeedingCharge = _registry.All()
            .Where(v => v.Status == VehicleStatus.Idle
                        && v.Battery?.BatteryCharge < threshold
                        && !IsChargingNode(v.LastNodeId)
                        && !_pendingChargeDispatches.Contains(v.VehicleId))
            .ToList();

        if (vehiclesNeedingCharge.Count == 0)
            return;

        // Nodes currently occupied by any vehicle (for eviction target selection).
        // Use DistinctBy to guard against duplicate LastNodeId values (should not happen
        // in normal operation but prevents a ToDictionary key-collision exception).
        var vehiclesAtChargingNode = _registry.All()
            .Where(v => IsChargingNode(v.LastNodeId))
            .DistinctBy(v => v.LastNodeId)
            .ToDictionary(v => v.LastNodeId!, v => v);

        foreach (var vehicle in vehiclesNeedingCharge)
        {
            var fromNodeId = vehicle.LastNodeId;
            if (fromNodeId is null)
                continue;

            var freeChargingNode = chargingNodeIds.FirstOrDefault(n => !vehiclesAtChargingNode.ContainsKey(n));

            if (freeChargingNode != null)
            {
                _log.LogInformation(
                    "Low-battery vehicle {VehicleId} ({Battery:F1}%) dispatched to free charging station {ChargingNodeId}",
                    vehicle.VehicleId, vehicle.Battery?.BatteryCharge ?? 0, freeChargingNode);
                _pendingChargeDispatches.Add(vehicle.VehicleId);
                await _dispatcher.MoveToNodeAsync(vehicle, fromNodeId, freeChargingNode, ct);

                // Mark this node as occupied so subsequent iterations don't double-book it.
                vehiclesAtChargingNode[freeChargingNode] = vehicle;
            }
            else
            {
                // All charging nodes occupied — try to evict a fully-charged vehicle.
                var evictCandidate = vehiclesAtChargingNode.Values
                    .Where(v => v.Battery?.BatteryCharge >= Vehicle.FullBatteryThreshold
                                && v.Status is VehicleStatus.Idle or VehicleStatus.Charging
                                && !_pendingChargeDispatches.Contains(v.VehicleId))
                    .OrderByDescending(v => v.Battery?.BatteryCharge ?? 0)
                    .FirstOrDefault();

                if (evictCandidate is null)
                {
                    _log.LogWarning(
                        "Low-battery vehicle {VehicleId} ({Battery:F1}%) cannot be sent to charge: all stations occupied by non-evictable vehicles",
                        vehicle.VehicleId, vehicle.Battery?.BatteryCharge ?? 0);
                    continue;
                }

                var chargeNodeId = evictCandidate.LastNodeId!;
                var allOccupied  = new HashSet<string>(_registry.All()
                    .Where(v => v.LastNodeId != null)
                    .Select(v => v.LastNodeId!));

                var evictTarget = _topology.GetNeighborNodeIds(chargeNodeId)
                    .FirstOrDefault(n => !chargingNodeIds.Contains(n) && !allOccupied.Contains(n));

                if (evictTarget is null)
                {
                    _log.LogWarning(
                        "Cannot evict fully-charged vehicle {EvictVehicleId} from {ChargingNodeId}: no free neighbours",
                        evictCandidate.VehicleId, chargeNodeId);
                    continue;
                }

                _log.LogInformation(
                    "Evicting fully-charged vehicle {EvictVehicleId} ({Battery:F1}%) from {ChargingNodeId} → {EvictTarget} to make room for low-battery vehicle {LowBatteryVehicleId} ({LowBattery:F1}%)",
                    evictCandidate.VehicleId, evictCandidate.Battery?.BatteryCharge ?? 0,
                    chargeNodeId, evictTarget,
                    vehicle.VehicleId, vehicle.Battery?.BatteryCharge ?? 0);

                await _dispatcher.MoveToNodeAsync(evictCandidate, chargeNodeId, evictTarget, ct);
                vehiclesAtChargingNode.Remove(chargeNodeId);

                _pendingChargeDispatches.Add(vehicle.VehicleId);
                await _dispatcher.MoveToNodeAsync(vehicle, fromNodeId, chargeNodeId, ct);
                vehiclesAtChargingNode[chargeNodeId] = vehicle;
            }
        }
    }
}
