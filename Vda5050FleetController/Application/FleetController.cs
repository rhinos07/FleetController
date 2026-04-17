using Microsoft.Extensions.Logging;
using Vda5050FleetController.Domain.Models;
using Vda5050FleetController.Infrastructure.Mqtt;

namespace Vda5050FleetController.Application;

// ── Vehicle Registry ──────────────────────────────────────────────────────────

public class VehicleRegistry
{
    private readonly Dictionary<string, Vehicle> _vehicles = [];
    private readonly ILogger<VehicleRegistry>    _log;

    public VehicleRegistry(ILogger<VehicleRegistry> log) => _log = log;

    public Vehicle GetOrCreate(string manufacturer, string serial)
    {
        var id = $"{manufacturer}/{serial}";
        if (!_vehicles.TryGetValue(id, out var vehicle))
        {
            vehicle       = new Vehicle(manufacturer, serial);
            _vehicles[id] = vehicle;
            _log.LogInformation("Registered new vehicle {VehicleId}", id);
        }
        return vehicle;
    }

    public Vehicle? Find(string vehicleId)
        => _vehicles.GetValueOrDefault(vehicleId);

    public IEnumerable<Vehicle> All()
        => _vehicles.Values;

    public Vehicle? FindAvailable()
        => _vehicles.Values.FirstOrDefault(v => v.IsAvailable);
}

// ── Order Queue ───────────────────────────────────────────────────────────────

public class TransportOrderQueue
{
    private readonly Queue<TransportOrder>            _pending   = [];
    private readonly Dictionary<string, TransportOrder> _active  = [];
    private readonly ILogger<TransportOrderQueue>     _log;

    public TransportOrderQueue(ILogger<TransportOrderQueue> log) => _log = log;

    public void Enqueue(TransportOrder order)
    {
        _pending.Enqueue(order);
        _log.LogInformation("Queued TransportOrder {OrderId}: {Src} → {Dst}",
            order.OrderId, order.SourceId, order.DestId);
    }

    public TransportOrder? DequeuePending()
        => _pending.TryDequeue(out var order) ? order : null;

    public void MarkActive(TransportOrder order)
        => _active[order.OrderId] = order;

    public TransportOrder? FindActive(string orderId)
        => _active.GetValueOrDefault(orderId);

    public void Complete(string orderId)
    {
        if (_active.Remove(orderId, out var order))
        {
            order.Complete();
            _log.LogInformation("TransportOrder {OrderId} completed", orderId);
        }
    }

    public int PendingCount => _pending.Count;
    public int ActiveCount  => _active.Count;
}

// ── Fleet Controller ──────────────────────────────────────────────────────────

public class FleetController
{
    private readonly VehicleRegistry      _registry;
    private readonly TransportOrderQueue  _queue;
    private readonly TopologyMap          _topology;
    private readonly IVda5050MqttService  _mqtt;
    private readonly IFleetStatusPublisher _statusPublisher;
    private readonly ILogger<FleetController> _log;

    private static readonly string DefaultMapId = "WAREHOUSE-FLOOR-1";

    public FleetController(
        VehicleRegistry      registry,
        TransportOrderQueue  queue,
        TopologyMap          topology,
        IVda5050MqttService  mqtt,
        IFleetStatusPublisher? statusPublisher,
        ILogger<FleetController> log)
    {
        _registry = registry;
        _queue    = queue;
        _topology = topology;
        _mqtt     = mqtt;
        _statusPublisher = statusPublisher ?? NoOpFleetStatusPublisher.Instance;
        _log      = log;

        // Wire up MQTT events
        _mqtt.OnStateReceived      += HandleVehicleStateAsync;
        _mqtt.OnConnectionReceived += HandleConnectionAsync;
    }

    // ── Inbound: WMS/MFR requests a transport ────────────────────────────────

    public Task RequestTransportAsync(string sourceStationId, string destStationId,
        string? loadId = null, CancellationToken ct = default)
    {
        var order = new TransportOrder(
            orderId:  $"TO-{Guid.NewGuid():N}"[..16],
            sourceId: sourceStationId,
            destId:   destStationId,
            loadId:   loadId
        );

        _queue.Enqueue(order);
        return UpdateStatusAfterDispatchAsync(ct);
    }

    private async Task UpdateStatusAfterDispatchAsync(CancellationToken ct = default)
    {
        await TryDispatchAsync(ct);
        await PublishStatusAsync(ct);
    }

    // ── Dispatch: find idle vehicle and send VDA5050 order ───────────────────

    private async Task TryDispatchAsync(CancellationToken ct = default)
    {
        while (true)
        {
            var pendingOrder = _queue.DequeuePending();
            if (pendingOrder is null) break;

            var vehicle = _registry.FindAvailable();
            if (vehicle is null)
            {
                // No vehicle available — re-queue and wait for next state update
                _log.LogWarning("No available vehicle for order {OrderId}, re-queuing",
                    pendingOrder.OrderId);
                _queue.Enqueue(pendingOrder);
                break;
            }

            await DispatchToVehicleAsync(pendingOrder, vehicle, ct);
        }
    }

    private async Task DispatchToVehicleAsync(TransportOrder transportOrder,
        Vehicle vehicle, CancellationToken ct)
    {
        transportOrder.Assign(vehicle.VehicleId);
        _queue.MarkActive(transportOrder);

        // Build pick + drop actions
        var pickActions = new List<VdaAction>
        {
            new()
            {
                ActionId         = $"pick-{transportOrder.OrderId}",
                ActionType       = "pick",
                BlockingType     = "HARD",
                ActionParameters = transportOrder.LoadId is not null
                    ? [new() { Key = "loadId", Value = transportOrder.LoadId }]
                    : []
            }
        };

        var dropActions = new List<VdaAction>
        {
            new()
            {
                ActionId     = $"drop-{transportOrder.OrderId}",
                ActionType   = "drop",
                BlockingType = "HARD"
            }
        };

        // Build path from topology
        var (nodes, edges) = _topology.BuildPath(
            transportOrder.SourceId,
            transportOrder.DestId,
            pickActions,
            dropActions);

        // Build VDA5050 order
        var vdaOrder = new Order
        {
            HeaderId      = vehicle.NextHeaderId(),
            Manufacturer  = vehicle.Manufacturer,
            SerialNumber  = vehicle.SerialNumber,
            OrderId       = transportOrder.OrderId,
            OrderUpdateId = 0,
            Nodes         = nodes,
            Edges         = edges
        };

        _log.LogInformation(
            "Dispatching order {OrderId} to vehicle {VehicleId}: {Src} → {Dst}",
            transportOrder.OrderId, vehicle.VehicleId,
            transportOrder.SourceId, transportOrder.DestId);

        await _mqtt.PublishOrderAsync(vdaOrder, ct);
        transportOrder.Start();
    }

    // ── Inbound: Vehicle state update ────────────────────────────────────────

    private async Task HandleVehicleStateAsync(VehicleState state)
    {
        var vehicle = _registry.GetOrCreate(state.Manufacturer, state.SerialNumber);
        var wasIdle = vehicle.IsAvailable;

        vehicle.ApplyState(state);

        // Log errors
        foreach (var err in state.Errors)
            _log.LogWarning("Vehicle {Id} error [{Level}]: {Type} - {Desc}",
                vehicle.VehicleId, err.ErrorLevel, err.ErrorType, err.ErrorDescription);

        // Check if active order completed (vehicle has no more node/edge states)
        if (!string.IsNullOrEmpty(state.OrderId))
        {
            var activeOrder = _queue.FindActive(state.OrderId);
            if (activeOrder is not null
                && !state.NodeStates.Any()
                && !state.EdgeStates.Any()
                && !state.Driving)
            {
                _queue.Complete(state.OrderId);
            }
        }

        // Vehicle just became idle → try to assign next pending order
        if (!wasIdle && vehicle.IsAvailable)
            await TryDispatchAsync();

        await PublishStatusAsync();
    }

    // ── Inbound: Vehicle connection event ────────────────────────────────────

    private Task HandleConnectionAsync(ConnectionMessage msg)
    {
        var vehicle = _registry.GetOrCreate(msg.Manufacturer, msg.SerialNumber);
        vehicle.ApplyConnection(msg);

        _log.LogInformation("Vehicle {Id} connection: {State}",
            vehicle.VehicleId, msg.ConnectionState);

        return PublishStatusAsync();
    }

    // ── Control: Send instant action (e.g. emergency stop) ───────────────────

    public Task PauseVehicleAsync(string vehicleId, CancellationToken ct = default)
        => SendInstantActionAsync(vehicleId, "stopPause", ct);

    public Task ResumeVehicleAsync(string vehicleId, CancellationToken ct = default)
        => SendInstantActionAsync(vehicleId, "startPause", ct);

    public Task StartChargingAsync(string vehicleId, CancellationToken ct = default)
        => SendInstantActionAsync(vehicleId, "startCharging", ct);

    private async Task SendInstantActionAsync(string vehicleId, string actionType,
        CancellationToken ct)
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
        Vehicles      = _registry.All().Select(v => new VehicleSummary
        {
            VehicleId  = v.VehicleId,
            Status     = v.Status.ToString(),
            Position   = v.Position,
            Battery    = v.Battery?.BatteryCharge,
            OrderId    = v.CurrentOrderId,
            LastSeen   = v.LastSeen
        }).ToList(),
        PendingOrders = _queue.PendingCount,
        ActiveOrders  = _queue.ActiveCount
    };

    private Task PublishStatusAsync(CancellationToken ct = default)
        => _statusPublisher.PublishAsync(GetStatus(), ct);
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record FleetStatus
{
    public List<VehicleSummary> Vehicles      { get; init; } = [];
    public int                  PendingOrders { get; init; }
    public int                  ActiveOrders  { get; init; }
}

public record VehicleSummary
{
    public string       VehicleId { get; init; } = string.Empty;
    public string       Status    { get; init; } = string.Empty;
    public AgvPosition? Position  { get; init; }
    public double?      Battery   { get; init; }
    public string?      OrderId   { get; init; }
    public DateTime     LastSeen  { get; init; }
}
