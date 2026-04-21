using Microsoft.Extensions.Logging;
using Vda5050FleetController.Domain.Models;
using Vda5050FleetController.Infrastructure.Mqtt;

namespace Vda5050FleetController.Application;

public class VehicleDispatcher
{
    private readonly VehicleRegistry            _registry;
    private readonly TransportOrderQueue        _queue;
    private readonly TopologyMap                _topology;
    private readonly IVda5050MqttService        _mqtt;
    private readonly IFleetPersistenceService   _persistence;
    private readonly ILogger<VehicleDispatcher> _log;

    public VehicleDispatcher(
        VehicleRegistry            registry,
        TransportOrderQueue        queue,
        TopologyMap                topology,
        IVda5050MqttService        mqtt,
        IFleetPersistenceService   persistence,
        ILogger<VehicleDispatcher> log)
    {
        _registry    = registry;
        _queue       = queue;
        _topology    = topology;
        _mqtt        = mqtt;
        _persistence = persistence;
        _log         = log;
    }

    public async Task TryDispatchAsync(CancellationToken ct = default)
    {
        while (true)
        {
            var pendingOrder = _queue.DequeuePending();
            if (pendingOrder is null) break;

            var vehicle = FindBestVehicleForOrder(pendingOrder);
            if (vehicle is null)
            {
                _log.LogWarning("No available vehicle for order {OrderId}, re-queuing",
                    pendingOrder.OrderId);
                _queue.Enqueue(pendingOrder);
                break;
            }

            await DispatchToVehicleAsync(pendingOrder, vehicle, ct);
        }
    }

    public async Task TryResolveBlockersAsync(IReadOnlyList<string> pathNodeIds,
        Vehicle assignedVehicle, CancellationToken ct)
    {
        _log.LogDebug(
            "TryResolveBlockersAsync: Checking {PathNodeCount} path nodes for blocking vehicles (assigned: {AssignedVehicleId})",
            pathNodeIds.Count, assignedVehicle.VehicleId);

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
            _log.LogDebug("TryResolveBlockersAsync: No blocking vehicles found on path nodes");
            return;
        }

        _log.LogInformation(
            "TryResolveBlockersAsync: Found {BlockingNodeCount} nodes with blocking vehicles: {BlockingNodeIds}",
            idleAtNode.Count, string.Join(", ", idleAtNode.Keys));

        // Nodes not available as dodge targets: occupied nodes, the assigned vehicle's path, its current position
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

    public async Task TryUnblockVehiclesBlockedByAsync(Vehicle idleVehicle, CancellationToken ct)
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

    // Used by FleetController.MoveVehicleAsync to reposition a free vehicle
    public Task MoveToNodeAsync(Vehicle vehicle, string fromNodeId, string toNodeId, CancellationToken ct)
        => SendDodgeOrderAsync(vehicle, fromNodeId, toNodeId, ct);

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
                Vehicle  = v,
                Distance = DistanceToSource(v, sourceNode)
            })
            .MinBy(v => v.Distance) ?? new
            {
                Vehicle  = availableVehicles.First(),
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

        var (nodes, edges) = _topology.BuildPath(
            transportOrder.SourceId,
            transportOrder.DestId,
            pickActions,
            dropActions);

        var pathNodeIds = nodes.Select(n => n.NodeId).ToList();
        _log.LogDebug(
            "Dispatch order {OrderId} to vehicle {VehicleId}: checking {PathNodeCount} path nodes for blockers",
            transportOrder.OrderId, vehicle.VehicleId, pathNodeIds.Count);
        await TryResolveBlockersAsync(pathNodeIds, vehicle, ct);

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
}
