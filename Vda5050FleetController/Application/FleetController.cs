using Microsoft.Extensions.Logging;
using Vda5050FleetController.Application.Contracts;
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

    /// <summary>
    /// Gets all orders (both pending and active) for status reporting and visualization.
    /// </summary>
    public IEnumerable<TransportOrder> GetAllOrders()
        => _pending.Concat(_active.Values);

    public int PendingCount => _pending.Count;
    public int ActiveCount  => _active.Count;
}

// ── Fleet Controller ──────────────────────────────────────────────────────────

/// <summary>
/// Core orchestrator for the VDA5050 fleet management system.
/// Handles transport order requests, vehicle dispatch, state updates, and instant actions.
/// </summary>
public class FleetController
{
    private readonly VehicleRegistry          _registry;
    private readonly TransportOrderQueue      _queue;
    private readonly TopologyMap              _topology;
    private readonly IVda5050MqttService      _mqtt;
    private readonly IFleetStatusPublisher    _statusPublisher;
    private readonly IFleetPersistenceService _persistence;
    private readonly ILogger<FleetController> _log;

    /// <summary>
    /// Creates a new FleetController instance.
    /// </summary>
    /// <param name="registry">Vehicle registry for managing vehicle instances.</param>
    /// <param name="queue">Queue for managing transport orders.</param>
    /// <param name="topology">Topology map for path building.</param>
    /// <param name="mqtt">MQTT service for VDA5050 communication.</param>
    /// <param name="statusPublisher">Optional publisher for fleet status updates (defaults to no-op).</param>
    /// <param name="persistence">Optional persistence service for durability (defaults to no-op).</param>
    /// <param name="log">Logger instance.</param>
    public FleetController(
        VehicleRegistry          registry,
        TransportOrderQueue      queue,
        TopologyMap              topology,
        IVda5050MqttService      mqtt,
        IFleetStatusPublisher?   statusPublisher,
        IFleetPersistenceService? persistence,
        ILogger<FleetController> log)
    {
        _registry        = registry;
        _queue           = queue;
        _topology        = topology;
        _mqtt            = mqtt;
        _statusPublisher = statusPublisher ?? NoOpFleetStatusPublisher.Instance;
        _persistence     = persistence    ?? NoOpFleetPersistenceService.Instance;
        _log             = log;

        // Wire up MQTT events
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
        await DispatchAndPublishStatusAsync(ct);
    }

    private async Task DispatchAndPublishStatusAsync(CancellationToken ct = default)
    {
        try
        {
            await TryDispatchAsync(ct);
        }
        finally
        {
            await PublishStatusAsync(ct);
        }
    }

    // ── Dispatch: find idle vehicle and send VDA5050 order ───────────────────

    private async Task TryDispatchAsync(CancellationToken ct = default)
    {
        while (true)
        {
            var pendingOrder = _queue.DequeuePending();
            if (pendingOrder is null) break;

            var vehicle = FindBestVehicleForOrder(pendingOrder);
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

    private Vehicle? FindBestVehicleForOrder(TransportOrder order)
    {
        var availableVehicles = _registry.All().Where(v => v.IsAvailable).ToList();
        if (availableVehicles.Count == 0)
            return null;

        var sourceNode = _topology.GetNode(order.SourceId);
        if (sourceNode is null)
            return availableVehicles.First();

        var rankedVehicle = availableVehicles
            .Select(v => new
            {
                Vehicle = v,
                Distance = DistanceToSource(v, sourceNode)
            })
            .MinBy(v => v.Distance) ?? new
            {
                Vehicle = availableVehicles.First(),
                Distance = double.PositiveInfinity
            };

        return double.IsInfinity(rankedVehicle.Distance)
            ? availableVehicles.First()
            : rankedVehicle.Vehicle;
    }

    private static double DistanceToSource(Vehicle vehicle, NodePosition sourceNode)
    {
        if (vehicle.Position is null)
            return double.PositiveInfinity;

        if (!string.Equals(vehicle.Position.MapId, sourceNode.MapId, StringComparison.OrdinalIgnoreCase))
            return double.PositiveInfinity;

        var dx = vehicle.Position.X - sourceNode.X;
        var dy = vehicle.Position.Y - sourceNode.Y;
        return (dx * dx) + (dy * dy);
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

        // Move any idle vehicle that is parked on a node the assigned vehicle needs to traverse
        await TryResolveBlockersAsync(nodes.Select(n => n.NodeId).ToList(), vehicle, ct);

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
        await _persistence.SaveOrderAsync(transportOrder, ct);
    }

    // ── Blocker resolution ───────────────────────────────────────────────────

    /// <summary>
    /// For each node in <paramref name="pathNodeIds"/>, checks whether an idle vehicle (other than
    /// <paramref name="assignedVehicle"/>) is parked there.  If so, sends that vehicle a short
    /// "dodge" order to the nearest free adjacent node so it clears the way.
    /// Called both at dispatch time and dynamically when a driving vehicle stops mid-path.
    /// </summary>
    private async Task TryResolveBlockersAsync(IReadOnlyList<string> pathNodeIds,
        Vehicle assignedVehicle, CancellationToken ct)
    {
        // Build a lookup: nodeId → list of stationary vehicles parked there that can be moved.
        // Includes vehicles in Busy status whose last order has already completed (orderId no longer
        // in the active queue) — they are physically present but their status hasn't reset to Idle yet.
        var idleAtNode = _registry.All()
            .Where(v => v.VehicleId != assignedVehicle.VehicleId
                        && v.LastNodeId is not null
                        && v.Status is not VehicleStatus.Driving
                                    and not VehicleStatus.Offline
                                    and not VehicleStatus.Error
                        && (v.CurrentOrderId is null || _queue.FindActive(v.CurrentOrderId) is null))
            .GroupBy(v => v.LastNodeId!)
            .ToDictionary(g => g.Key, g => g.ToList());

        if (idleAtNode.Count == 0)
            return;

        // Nodes that must not be used as dodge targets:
        // - nodes where idle vehicles already stand (they'll become the dodge origin)
        // - nodes on the assigned vehicle's own path (source is held during pick; destination is the goal)
        // - the assigned vehicle's current position (it physically occupies that node)
        var pendingOccupied = new HashSet<string>(idleAtNode.Keys);
        foreach (var n in pathNodeIds)
            pendingOccupied.Add(n);
        if (assignedVehicle.LastNodeId is not null)
            pendingOccupied.Add(assignedVehicle.LastNodeId);

        foreach (var nodeId in pathNodeIds)
        {
            if (!idleAtNode.TryGetValue(nodeId, out var blockers))
                continue;

            foreach (var blocker in blockers)
            {
                var dodgeTarget = _topology.GetNeighborNodeIds(nodeId)
                    .FirstOrDefault(n => !pendingOccupied.Contains(n));

                if (dodgeTarget is null)
                {
                    _log.LogWarning(
                        "No free neighbour for blocking vehicle {VehicleId} at node {NodeId}; skipping dodge",
                        blocker.VehicleId, nodeId);
                    break;
                }

                _log.LogInformation(
                    "Vehicle {VehicleId} is blocking node {NodeId} — sending dodge order to {DodgeTarget}",
                    blocker.VehicleId, nodeId, dodgeTarget);

                await SendDodgeOrderAsync(blocker, nodeId, dodgeTarget, ct);
                pendingOccupied.Add(dodgeTarget);
            }

            pendingOccupied.Remove(nodeId);
        }
    }

    /// <summary>
    /// When <paramref name="idleVehicle"/> just became idle at its last known node, checks whether
    /// any other vehicle was previously stopped mid-path waiting for that node to free up.
    /// For each such blocked vehicle, re-runs blocker resolution so a dodge order is sent to
    /// <paramref name="idleVehicle"/> and the blocked vehicle can proceed.
    /// </summary>
    private async Task TryUnblockVehiclesBlockedByAsync(Vehicle idleVehicle, CancellationToken ct)
    {
        if (idleVehicle.LastNodeId is null)
            return;

        var blockedVehicles = _registry.All()
            .Where(v => v.VehicleId != idleVehicle.VehicleId
                        && v.RemainingNodeIds is not null
                        && v.RemainingNodeIds.Contains(idleVehicle.LastNodeId))
            .ToList();

        foreach (var blockedVehicle in blockedVehicles)
        {
            _log.LogInformation(
                "Vehicle {BlockedId} is stopped mid-path waiting for {NodeId} (now held by idle {IdleId}); retriggering blocker resolution",
                blockedVehicle.VehicleId, idleVehicle.LastNodeId, idleVehicle.VehicleId);

            await TryResolveBlockersAsync(blockedVehicle.RemainingNodeIds!, blockedVehicle, ct);
        }
    }

    /// <summary>
    /// Sends a minimal VDA5050 order that moves <paramref name="vehicle"/> from
    /// <paramref name="fromNodeId"/> to <paramref name="toNodeId"/> without any pick/drop actions.
    /// The order ID starts with <c>DODGE-</c> so it is easy to recognise in logs.
    /// </summary>
    private async Task SendDodgeOrderAsync(Vehicle vehicle, string fromNodeId,
        string toNodeId, CancellationToken ct)
    {
        var (nodes, edges) = _topology.BuildPath(fromNodeId, toNodeId, [], []);
        var order = new Order
        {
            HeaderId      = vehicle.NextHeaderId(),
            Manufacturer  = vehicle.Manufacturer,
            SerialNumber  = vehicle.SerialNumber,
            OrderId       = $"DODGE-{Guid.NewGuid():N}"[..24],
            OrderUpdateId = 0,
            Nodes         = nodes,
            Edges         = edges
        };
        await _mqtt.PublishOrderAsync(order, ct);
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
                await _persistence.CompleteOrderAsync(activeOrder);
            }
        }

        await _persistence.SaveVehicleAsync(vehicle);

        // Vehicle stopped mid-path with an active order → check if it is blocked by an idle AGV
        // (driving=false with remaining nodeStates means the vehicle paused, likely waiting for a node to clear)
        if (!state.Driving
            && !string.IsNullOrEmpty(state.OrderId)
            && state.NodeStates.Count > 0)
        {
            var remainingNodeIds = state.NodeStates.Select(ns => ns.NodeId).ToList();
            await TryResolveBlockersAsync(remainingNodeIds, vehicle, ct: default);
        }

        // Vehicle just became idle → try to assign next pending order, and unblock any
        // active vehicle that was previously stopped because this node was occupied.
        if (!wasIdle && vehicle.IsAvailable)
        {
            await TryDispatchAsync();
            // Only trigger dodge if no transport order was just dispatched to this vehicle.
            // Sending a dodge order to a vehicle that already received a transport order would
            // overwrite it on the AGV (higher HeaderId wins).
            var wasDispatched = _queue.GetAllOrders()
                .Any(o => o.AssignedVehicleId == vehicle.VehicleId);
            if (!wasDispatched)
                await TryUnblockVehiclesBlockedByAsync(vehicle, ct: default);
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

    // ── Control: Send instant action (e.g. emergency stop) ───────────────────

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

        await SendDodgeOrderAsync(vehicle, fromNodeId, destNodeId, ct);
        _log.LogInformation("Manual reposition: vehicle {VehicleId} → {DestNodeId}", vehicleId, destNodeId);
    }

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
