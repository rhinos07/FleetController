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
    private readonly List<TransportOrder>              _pending = [];
    private readonly Dictionary<string, TransportOrder> _active  = [];
    private readonly ILogger<TransportOrderQueue>      _log;

    public TransportOrderQueue(ILogger<TransportOrderQueue> log) => _log = log;

    public void Enqueue(TransportOrder order)
    {
        _pending.Add(order);
        _log.LogInformation("Queued TransportOrder {OrderId}: {Src} → {Dst}",
            order.OrderId, order.SourceId, order.DestId);
    }

    public TransportOrder? DequeuePending()
    {
        if (_pending.Count == 0) return null;
        var order = _pending[0];
        _pending.RemoveAt(0);
        return order;
    }

    public void MarkActive(TransportOrder order)
        => _active[order.OrderId] = order;

    public TransportOrder? FindActive(string orderId)
        => _active.GetValueOrDefault(orderId);

    /// <summary>
    /// Removes a pending order from the queue by order ID.
    /// Returns true if the order was found and removed, false otherwise.
    /// </summary>
    public bool RemovePending(string orderId)
    {
        var index = _pending.FindIndex(o => o.OrderId == orderId);
        if (index < 0) return false;
        _pending.RemoveAt(index);
        _log.LogInformation("Cancelled pending TransportOrder {OrderId}", orderId);
        return true;
    }

    /// <summary>
    /// Replaces a pending order in-place with a new order object, preserving queue position.
    /// Returns true if the order was found and replaced, false otherwise.
    /// </summary>
    public bool ReplacePending(string orderId, TransportOrder replacement)
    {
        var index = _pending.FindIndex(o => o.OrderId == orderId);
        if (index < 0) return false;
        _pending[index] = replacement;
        _log.LogInformation("Updated pending TransportOrder {OrderId}: {Src} → {Dst}",
            replacement.OrderId, replacement.SourceId, replacement.DestId);
        return true;
    }

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

    /// <summary>
    /// Cancels a pending transport order by ID.
    /// Returns false if the order is not found in the pending queue (already dispatched or unknown).
    /// </summary>
    public async Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        if (!_queue.RemovePending(orderId))
            return false;

        await PublishStatusAsync(ct);
        return true;
    }

    /// <summary>
    /// Updates the source, destination and load of a pending transport order.
    /// Returns false if the order is not found in the pending queue (already dispatched or unknown).
    /// </summary>
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
        var pathNodeIds = nodes.Select(n => n.NodeId).ToList();
        _log.LogDebug(
            "Dispatch order {OrderId} to vehicle {VehicleId}: checking {PathNodeCount} path nodes for blockers",
            transportOrder.OrderId, vehicle.VehicleId, pathNodeIds.Count);
        await TryResolveBlockersAsync(pathNodeIds, vehicle, ct);
        
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
        _log.LogDebug(
            "TryResolveBlockersAsync: Checking {PathNodeCount} path nodes for blocking vehicles (assigned: {AssignedVehicleId})",
            pathNodeIds.Count, assignedVehicle.VehicleId);

        // Build a lookup: nodeId → list of stationary vehicles parked there that can be moved.
        // Includes vehicles in Busy status whose last order has already completed (orderId no longer
        // in the active queue) — they are physically present but their status hasn't reset to Idle yet.
        var allVehicles = _registry.All().ToList();
        _log.LogDebug(
            "TryResolveBlockersAsync: Scanning {TotalVehicles} vehicles for blockers",
            allVehicles.Count);

        var idleAtNode = allVehicles
            .Where(v =>
            {
                var passes = v.VehicleId != assignedVehicle.VehicleId
                             && v.LastNodeId is not null
                             && v.Status is not VehicleStatus.Driving
                                         and not VehicleStatus.Offline
                                         and not VehicleStatus.Error
                             && (v.CurrentOrderId is null || _queue.FindActive(v.CurrentOrderId) is null);
                if (!passes && v.VehicleId != assignedVehicle.VehicleId)
                    _log.LogDebug(
                        "Vehicle {VehicleId} excluded: Status={Status}, LastNode={LastNodeId}, CurrentOrder={OrderId}",
                        v.VehicleId, v.Status, v.LastNodeId, v.CurrentOrderId);
                return passes;
            })
            .GroupBy(v => v.LastNodeId!)
            .ToDictionary(g => g.Key, g => g.ToList());

        if (idleAtNode.Count == 0)
        {
            _log.LogDebug(
                "TryResolveBlockersAsync: No blocking vehicles found on path nodes");
            return;
        }

        _log.LogInformation(
            "TryResolveBlockersAsync: Found {BlockingNodeCount} nodes with blocking vehicles: {BlockingNodeIds}",
            idleAtNode.Count, string.Join(", ", idleAtNode.Keys));

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
            {
                _log.LogDebug("Node {NodeId} on path has no blocking vehicles", nodeId);
                continue;
            }

            _log.LogInformation(
                "Node {NodeId} has {BlockerCount} blocking vehicle(s): {VehicleIds}",
                nodeId, blockers.Count, string.Join(", ", blockers.Select(b => b.VehicleId)));

            foreach (var blocker in blockers)
            {
                var neighbors = _topology.GetNeighborNodeIds(nodeId).ToList();
                _log.LogDebug(
                    "Vehicle {VehicleId} at node {NodeId}: {NeighborCount} neighbors available",
                    blocker.VehicleId, nodeId, neighbors.Count);

                var freeNeighbors = neighbors.Where(n => !pendingOccupied.Contains(n)).ToList();
                _log.LogDebug(
                    "Vehicle {VehicleId}: {FreeNeighborCount} free neighbors (occupied: {OccupiedCount})",
                    blocker.VehicleId, freeNeighbors.Count, neighbors.Count - freeNeighbors.Count);

                var dodgeTarget = freeNeighbors.FirstOrDefault();

                if (dodgeTarget is null)
                {
                    _log.LogWarning(
                        "No free neighbour for blocking vehicle {VehicleId} at node {NodeId}; neighbors: [{AllNeighbors}], occupied: [{OccupiedNeighbors}]",
                        blocker.VehicleId, nodeId,
                        string.Join(", ", neighbors),
                        string.Join(", ", neighbors.Where(n => pendingOccupied.Contains(n))));
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
        _log.LogDebug(
            "SendDodgeOrderAsync: Building path {FromNode} → {ToNode} for vehicle {VehicleId}",
            fromNodeId, toNodeId, vehicle.VehicleId);
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
        _log.LogDebug(
            "SendDodgeOrderAsync: Publishing dodge order {DodgeOrderId} for vehicle {VehicleId}",
            order.OrderId, vehicle.VehicleId);
        await _mqtt.PublishOrderAsync(order, ct);
        _log.LogInformation(
            "Dodge order {DodgeOrderId} published to vehicle {VehicleId}",
            order.OrderId, vehicle.VehicleId);
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
            _log.LogInformation(
                "Vehicle {VehicleId} stopped mid-path with active order {OrderId}: {RemainingNodeCount} nodes remaining. Checking for blockers.",
                vehicle.VehicleId, state.OrderId, state.NodeStates.Count);
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
