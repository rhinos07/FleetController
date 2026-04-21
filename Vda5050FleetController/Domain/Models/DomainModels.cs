namespace Vda5050FleetController.Domain.Models;

// ── Vehicle Aggregate ─────────────────────────────────────────────────────────

/// <summary>
/// Represents a VDA5050-compliant Automated Guided Vehicle (AGV).
/// This is the main aggregate root for vehicle management in the fleet controller.
/// </summary>
public class Vehicle
{
    /// <summary>
    /// Unique identifier combining manufacturer and serial number (e.g., "Acme/SN-001").
    /// </summary>
    public string VehicleId    { get; }

    /// <summary>
    /// Vehicle manufacturer name as per VDA5050 specification.
    /// </summary>
    public string Manufacturer { get; }

    /// <summary>
    /// Unique serial number assigned by the manufacturer.
    /// </summary>
    public string SerialNumber { get; }

    /// <summary>
    /// Current operational status of the vehicle.
    /// </summary>
    public VehicleStatus    Status      { get; private set; } = VehicleStatus.Unknown;

    /// <summary>
    /// Current position reported by the AGV, or null if not yet localized.
    /// </summary>
    public AgvPosition?     Position    { get; private set; }

    /// <summary>
    /// Current battery state including charge level and charging status.
    /// </summary>
    public BatteryState?    Battery     { get; private set; }

    /// <summary>
    /// ID of the currently assigned transport order, or null if idle.
    /// </summary>
    public string?          CurrentOrderId { get; private set; }

    /// <summary>
    /// The node ID the vehicle last reported as its current position, or null if not yet known.
    /// Updated from the <c>lastNodeId</c> field of each incoming VDA5050 state message.
    /// </summary>
    public string?          LastNodeId     { get; private set; }

    /// <summary>
    /// Timestamp of the last received state or connection message.
    /// </summary>
    public DateTime         LastSeen    { get; private set; }

    /// <summary>
    /// List of active errors reported by the vehicle.
    /// </summary>
    public IReadOnlyList<VdaError> ActiveErrors => _activeErrors;
    private List<VdaError> _activeErrors = [];

    private int _headerIdCounter = 0;

    /// <summary>
    /// Minimum battery level required for a vehicle to be considered available for dispatch.
    /// </summary>
    public const double MinimumBatteryForDispatch = 20.0;

    /// <summary>
    /// Battery level at or above which a vehicle at a charging station is considered
    /// fully charged and eligible for eviction to free up the spot.
    /// </summary>
    public const double FullBatteryThreshold = 80.0;

    /// <summary>
    /// The remaining node IDs from the last state message where the vehicle was stopped mid-path
    /// (driving=false with at least one pending NodeState).  Null when the vehicle is driving
    /// normally or has no active order.  Used by the fleet controller to proactively re-send
    /// dodge orders when a blocker at one of these nodes later becomes idle.
    /// </summary>
    public IReadOnlyList<string>? RemainingNodeIds { get; private set; }

    /// <summary>
    /// Creates a new vehicle instance.
    /// </summary>
    /// <param name="manufacturer">Manufacturer name (required, non-empty).</param>
    /// <param name="serialNumber">Serial number (required, non-empty).</param>
    /// <exception cref="ArgumentException">Thrown when manufacturer or serialNumber is null or empty.</exception>
    public Vehicle(string manufacturer, string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(manufacturer))
            throw new ArgumentException("Manufacturer cannot be null or empty", nameof(manufacturer));
        if (string.IsNullOrWhiteSpace(serialNumber))
            throw new ArgumentException("SerialNumber cannot be null or empty", nameof(serialNumber));

        Manufacturer = manufacturer;
        SerialNumber = serialNumber;
        VehicleId    = $"{manufacturer}/{serialNumber}";
    }

    /// <summary>
    /// Applies a VDA5050 state message to update the vehicle's internal state.
    /// </summary>
    /// <param name="state">The state message received from the AGV.</param>
    public void ApplyState(VehicleState state)
    {
        Position       = state.AgvPosition;
        Battery        = state.BatteryState;
        CurrentOrderId = string.IsNullOrEmpty(state.OrderId) ? null : state.OrderId;
        _activeErrors  = [.. state.Errors];
        LastSeen       = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(state.LastNodeId))
            LastNodeId = state.LastNodeId;

        // Track remaining nodes when the vehicle has stopped mid-path (potential blockage).
        // Clear when driving again or order is finished.
        if (!state.Driving && state.NodeStates.Count > 0 && !string.IsNullOrEmpty(state.OrderId))
            RemainingNodeIds = state.NodeStates.Select(ns => ns.NodeId).ToList();
        else
            RemainingNodeIds = null;

        Status = DetermineStatus(state);
    }

    /// <summary>
    /// Applies a VDA5050 connection message to update the vehicle's connection status.
    /// </summary>
    /// <param name="msg">The connection message received from the AGV.</param>
    public void ApplyConnection(ConnectionMessage msg)
    {
        Status   = msg.ConnectionState == "ONLINE" ? VehicleStatus.Idle : VehicleStatus.Offline;
        LastSeen = DateTime.UtcNow;
    }

    /// <summary>
    /// Indicates whether the vehicle is available for order dispatch.
    /// A vehicle is available when it is idle, has sufficient battery, and has no fatal errors.
    /// </summary>
    public bool IsAvailable =>
        Status is VehicleStatus.Idle &&
        Battery?.BatteryCharge > MinimumBatteryForDispatch &&
        !HasFatalError;

    /// <summary>
    /// Indicates whether the vehicle has a fatal error that prevents operation.
    /// </summary>
    public bool HasFatalError =>
        _activeErrors.Exists(e => e.ErrorLevel == "FATAL");

    /// <summary>
    /// Generates the next unique header ID for VDA5050 messages.
    /// This method is thread-safe.
    /// </summary>
    /// <returns>A monotonically increasing header ID.</returns>
    public int NextHeaderId() => Interlocked.Increment(ref _headerIdCounter);

    private static VehicleStatus DetermineStatus(VehicleState state)
    {
        if (state.Errors.Any(e => e.ErrorLevel == "FATAL"))
            return VehicleStatus.Error;
        if (state.Driving)
            return VehicleStatus.Driving;
        if (state.BatteryState?.Charging == true)
            return VehicleStatus.Charging;
        if (!string.IsNullOrEmpty(state.OrderId))
            return VehicleStatus.Busy;
        return VehicleStatus.Idle;
    }
}

/// <summary>
/// Represents the operational status of a vehicle in the fleet.
/// </summary>
public enum VehicleStatus
{
    /// <summary>Vehicle status has not been determined yet.</summary>
    Unknown,
    /// <summary>Vehicle is online and available for orders.</summary>
    Idle,
    /// <summary>Vehicle is currently moving to a destination.</summary>
    Driving,
    /// <summary>Vehicle is executing an order but not currently driving.</summary>
    Busy,
    /// <summary>Vehicle is charging its battery.</summary>
    Charging,
    /// <summary>Vehicle has a fatal error and cannot operate.</summary>
    Error,
    /// <summary>Vehicle has disconnected from the network.</summary>
    Offline
}

// ── Transport Order ───────────────────────────────────────────────────────────

/// <summary>
/// Represents a transport order in the fleet management system.
/// A transport order describes a request to move materials from one station to another.
/// </summary>
public class TransportOrder
{
    /// <summary>
    /// Unique identifier for the transport order.
    /// </summary>
    public string OrderId { get; }

    /// <summary>
    /// ID of the source station where materials are picked up.
    /// </summary>
    public string SourceId { get; }

    /// <summary>
    /// ID of the destination station where materials are delivered.
    /// </summary>
    public string DestId { get; }

    /// <summary>
    /// Optional identifier for the load being transported.
    /// </summary>
    public string? LoadId { get; }

    /// <summary>
    /// Current status of the transport order.
    /// </summary>
    public TransportStatus Status { get; private set; } = TransportStatus.Pending;

    /// <summary>
    /// ID of the vehicle assigned to execute this order, or null if not yet assigned.
    /// </summary>
    public string? AssignedVehicleId { get; private set; }

    /// <summary>
    /// Timestamp when the order was created.
    /// </summary>
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the order was assigned to a vehicle, or null if not yet assigned.
    /// </summary>
    public DateTime? AssignedAt { get; private set; }

    /// <summary>
    /// Timestamp when the order execution started, or null if not yet started.
    /// </summary>
    public DateTime? StartedAt { get; private set; }

    /// <summary>
    /// Timestamp when the order was completed or failed, or null if still in progress.
    /// </summary>
    public DateTime? CompletedAt { get; private set; }

    /// <summary>
    /// Creates a new transport order.
    /// </summary>
    /// <param name="orderId">Unique order identifier (required, non-empty).</param>
    /// <param name="sourceId">Source station ID (required, non-empty).</param>
    /// <param name="destId">Destination station ID (required, non-empty).</param>
    /// <param name="loadId">Optional load identifier.</param>
    /// <exception cref="ArgumentException">Thrown when required parameters are null or empty.</exception>
    public TransportOrder(string orderId, string sourceId, string destId, string? loadId = null)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            throw new ArgumentException("OrderId cannot be null or empty", nameof(orderId));
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("SourceId cannot be null or empty", nameof(sourceId));
        if (string.IsNullOrWhiteSpace(destId))
            throw new ArgumentException("DestId cannot be null or empty", nameof(destId));

        OrderId  = orderId;
        SourceId = sourceId;
        DestId   = destId;
        LoadId   = loadId;
    }

    /// <summary>
    /// Assigns the order to a vehicle for execution.
    /// </summary>
    /// <param name="vehicleId">The ID of the vehicle to assign.</param>
    /// <exception cref="ArgumentException">Thrown when vehicleId is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the order is not in Pending status.</exception>
    public void Assign(string vehicleId)
    {
        if (string.IsNullOrWhiteSpace(vehicleId))
            throw new ArgumentException("VehicleId cannot be null or empty", nameof(vehicleId));
        if (Status != TransportStatus.Pending)
            throw new InvalidOperationException(
                $"Cannot assign order in {Status} status. Order must be Pending.");

        AssignedVehicleId = vehicleId;
        AssignedAt        = DateTime.UtcNow;
        Status            = TransportStatus.Assigned;
    }

    /// <summary>
    /// Marks the order as in progress (execution has started).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the order is not in Assigned status.</exception>
    public void Start()
    {
        if (Status != TransportStatus.Assigned)
            throw new InvalidOperationException(
                $"Cannot start order in {Status} status. Order must be Assigned.");

        StartedAt = DateTime.UtcNow;
        Status    = TransportStatus.InProgress;
    }

    /// <summary>
    /// Marks the order as successfully completed.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the order is not in InProgress status.</exception>
    public void Complete()
    {
        if (Status != TransportStatus.InProgress)
            throw new InvalidOperationException(
                $"Cannot complete order in {Status} status. Order must be InProgress.");

        CompletedAt = DateTime.UtcNow;
        Status      = TransportStatus.Completed;
    }

    /// <summary>
    /// Marks the order as failed.
    /// Can be called from any active status (Pending, Assigned, or InProgress).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the order is already completed or failed.</exception>
    public void Fail()
    {
        if (Status is TransportStatus.Completed or TransportStatus.Failed)
            throw new InvalidOperationException(
                $"Cannot fail order in {Status} status. Order is already finalized.");

        CompletedAt = DateTime.UtcNow;
        Status      = TransportStatus.Failed;
    }

    /// <summary>
    /// Indicates whether the order is in an active (non-terminal) state.
    /// </summary>
    public bool IsActive =>
        Status is TransportStatus.Pending or TransportStatus.Assigned or TransportStatus.InProgress;

    /// <summary>
    /// Indicates whether the order has reached a terminal state (Completed or Failed).
    /// </summary>
    public bool IsFinalized =>
        Status is TransportStatus.Completed or TransportStatus.Failed;
}

/// <summary>
/// Represents the lifecycle status of a transport order.
/// </summary>
public enum TransportStatus
{
    /// <summary>Order is queued and waiting for vehicle assignment.</summary>
    Pending,
    /// <summary>Order has been assigned to a vehicle but execution has not started.</summary>
    Assigned,
    /// <summary>Order is being executed by the assigned vehicle.</summary>
    InProgress,
    /// <summary>Order has been successfully completed.</summary>
    Completed,
    /// <summary>Order execution failed.</summary>
    Failed
}

// ── Topology (Graph) ──────────────────────────────────────────────────────────

/// <summary>
/// Represents the topology map of a facility where AGVs operate.
/// The topology defines navigable nodes (stations, waypoints) and edges (paths between nodes).
/// </summary>
/// <remarks>
/// The current implementation provides a simplified direct path building.
/// In a production environment, this would be extended with A* or Dijkstra pathfinding.
/// </remarks>
public class TopologyMap
{
    private readonly Dictionary<string, NodePosition> _nodes = [];
    private readonly Dictionary<string, (string From, string To)> _edges = [];

    /// <summary>
    /// Default maximum speed for edges in units per second.
    /// </summary>
    public const double DefaultMaxSpeed = 1.5;

    /// <summary>
    /// Number of nodes currently in the topology.
    /// </summary>
    public int NodeCount => _nodes.Count;

    /// <summary>
    /// Number of edges currently in the topology.
    /// </summary>
    public int EdgeCount => _edges.Count;

    /// <summary>
    /// Adds or updates a node in the topology.
    /// </summary>
    /// <param name="nodeId">Unique identifier for the node.</param>
    /// <param name="x">X coordinate in the map.</param>
    /// <param name="y">Y coordinate in the map.</param>
    /// <param name="theta">Orientation angle in radians.</param>
    /// <param name="mapId">Identifier of the map this node belongs to.</param>
    /// <exception cref="ArgumentException">Thrown when nodeId or mapId is null or empty.</exception>
    public void AddNode(string nodeId, double x, double y, double theta, string mapId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            throw new ArgumentException("NodeId cannot be null or empty", nameof(nodeId));
        if (string.IsNullOrWhiteSpace(mapId))
            throw new ArgumentException("MapId cannot be null or empty", nameof(mapId));

        _nodes[nodeId] = new NodePosition { X = x, Y = y, Theta = theta, MapId = mapId };
    }

    /// <summary>
    /// Removes a node from the topology.
    /// </summary>
    /// <param name="nodeId">The ID of the node to remove.</param>
    /// <returns>True if the node was found and removed; false otherwise.</returns>
    public bool RemoveNode(string nodeId)
        => _nodes.Remove(nodeId);

    /// <summary>
    /// Adds or updates an edge in the topology.
    /// </summary>
    /// <param name="edgeId">Unique identifier for the edge.</param>
    /// <param name="fromNodeId">ID of the starting node.</param>
    /// <param name="toNodeId">ID of the ending node.</param>
    /// <exception cref="ArgumentException">Thrown when any parameter is null or empty.</exception>
    public void AddEdge(string edgeId, string fromNodeId, string toNodeId)
    {
        if (string.IsNullOrWhiteSpace(edgeId))
            throw new ArgumentException("EdgeId cannot be null or empty", nameof(edgeId));
        if (string.IsNullOrWhiteSpace(fromNodeId))
            throw new ArgumentException("FromNodeId cannot be null or empty", nameof(fromNodeId));
        if (string.IsNullOrWhiteSpace(toNodeId))
            throw new ArgumentException("ToNodeId cannot be null or empty", nameof(toNodeId));

        _edges[edgeId] = (fromNodeId, toNodeId);
    }

    /// <summary>
    /// Removes an edge from the topology.
    /// </summary>
    /// <param name="edgeId">The ID of the edge to remove.</param>
    /// <returns>True if the edge was found and removed; false otherwise.</returns>
    public bool RemoveEdge(string edgeId)
        => _edges.Remove(edgeId);

    /// <summary>
    /// Gets the position of a node by its ID.
    /// </summary>
    /// <param name="nodeId">The ID of the node to look up.</param>
    /// <returns>The node position, or null if not found.</returns>
    public NodePosition? GetNode(string nodeId)
        => _nodes.GetValueOrDefault(nodeId);

    /// <summary>
    /// Checks if a node exists in the topology.
    /// </summary>
    /// <param name="nodeId">The ID of the node to check.</param>
    /// <returns>True if the node exists; false otherwise.</returns>
    public bool ContainsNode(string nodeId)
        => _nodes.ContainsKey(nodeId);

    /// <summary>
    /// Checks if an edge exists in the topology.
    /// </summary>
    /// <param name="edgeId">The ID of the edge to check.</param>
    /// <returns>True if the edge exists; false otherwise.</returns>
    public bool ContainsEdge(string edgeId)
        => _edges.ContainsKey(edgeId);

    /// <summary>
    /// Gets all nodes in the topology map with their positions.
    /// </summary>
    /// <returns>A collection of tuples containing node IDs and their positions.</returns>
    public IEnumerable<(string NodeId, NodePosition Position)> GetAllNodes()
        => _nodes.Select(kvp => (kvp.Key, kvp.Value));

    /// <summary>
    /// Gets all edges in the topology map.
    /// </summary>
    /// <returns>A collection of tuples containing edge IDs and their start/end node IDs.</returns>
    public IEnumerable<(string EdgeId, string From, string To)> GetAllEdges()
        => _edges.Select(kvp => (kvp.Key, kvp.Value.From, kvp.Value.To));

    /// <summary>
    /// Returns the IDs of all nodes directly connected to <paramref name="nodeId"/> by a topology edge,
    /// in either direction.
    /// </summary>
    /// <param name="nodeId">The node whose neighbours are to be found.</param>
    /// <returns>Distinct node IDs of adjacent nodes; empty when <paramref name="nodeId"/> has no edges.</returns>
    public IEnumerable<string> GetNeighborNodeIds(string nodeId)
        => _edges.Values
            .Where(e => e.From == nodeId || e.To == nodeId)
            .Select(e => e.From == nodeId ? e.To : e.From)
            .Distinct();

    /// <summary>
    /// Builds a VDA5050-compliant path from source to destination.
    /// </summary>
    /// <param name="sourceNodeId">ID of the starting node.</param>
    /// <param name="destNodeId">ID of the destination node.</param>
    /// <param name="pickActions">Actions to perform at the source node (e.g., pick up load).</param>
    /// <param name="dropActions">Actions to perform at the destination node (e.g., drop load).</param>
    /// <returns>A tuple containing the list of nodes and edges forming the path.</returns>
    /// <exception cref="InvalidOperationException">Thrown when source or destination node is not found.</exception>
    /// <remarks>
    /// Current implementation creates a direct path between source and destination.
    /// For production use, extend with proper pathfinding (A*, Dijkstra) over the graph.
    /// </remarks>
    public (List<Node> Nodes, List<Edge> Edges) BuildPath(string sourceNodeId, string destNodeId,
        List<VdaAction> pickActions, List<VdaAction> dropActions)
    {
        var sourcePos = GetNode(sourceNodeId)
            ?? throw new InvalidOperationException($"Source node {sourceNodeId} not found in topology");
        var destPos = GetNode(destNodeId)
            ?? throw new InvalidOperationException($"Destination node {destNodeId} not found in topology");

        var edgeId = $"E-{sourceNodeId}-{destNodeId}";

        var nodes = new List<Node>
        {
            new() {
                NodeId       = sourceNodeId,
                SequenceId   = 0,
                Released     = true,
                NodePosition = sourcePos,
                Actions      = pickActions
            },
            new() {
                NodeId       = destNodeId,
                SequenceId   = 2,
                Released     = true,
                NodePosition = destPos,
                Actions      = dropActions
            }
        };

        var edges = new List<Edge>
        {
            new() {
                EdgeId      = edgeId,
                SequenceId  = 1,
                Released    = true,
                StartNodeId = sourceNodeId,
                EndNodeId   = destNodeId,
                MaxSpeed    = DefaultMaxSpeed
            }
        };

        return (nodes, edges);
    }
}
