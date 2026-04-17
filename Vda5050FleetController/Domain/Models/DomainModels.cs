namespace Vda5050FleetController.Domain.Models;

// ── Vehicle Aggregate ─────────────────────────────────────────────────────────

public class Vehicle
{
    public string VehicleId    { get; private set; }
    public string Manufacturer { get; private set; }
    public string SerialNumber { get; private set; }

    public VehicleStatus    Status      { get; private set; } = VehicleStatus.Unknown;
    public AgvPosition?     Position    { get; private set; }
    public BatteryState?    Battery     { get; private set; }
    public string?          CurrentOrderId { get; private set; }
    public DateTime         LastSeen    { get; private set; }
    public List<VdaError>   ActiveErrors { get; private set; } = [];

    private int _headerIdCounter = 0;

    public Vehicle(string manufacturer, string serialNumber)
    {
        Manufacturer = manufacturer;
        SerialNumber = serialNumber;
        VehicleId    = $"{manufacturer}/{serialNumber}";
    }

    public void ApplyState(VehicleState state)
    {
        Position       = state.AgvPosition;
        Battery        = state.BatteryState;
        CurrentOrderId = string.IsNullOrEmpty(state.OrderId) ? null : state.OrderId;
        ActiveErrors   = state.Errors;
        LastSeen       = DateTime.UtcNow;

        Status = state.Errors.Any(e => e.ErrorLevel == "FATAL") ? VehicleStatus.Error
               : state.Driving                                   ? VehicleStatus.Driving
               : !string.IsNullOrEmpty(state.OrderId)           ? VehicleStatus.Busy
                                                                 : VehicleStatus.Idle;
    }

    public void ApplyConnection(ConnectionMessage msg)
    {
        Status   = msg.ConnectionState == "ONLINE" ? VehicleStatus.Idle : VehicleStatus.Offline;
        LastSeen = DateTime.UtcNow;
    }

    public bool IsAvailable =>
        Status is VehicleStatus.Idle &&
        Battery?.BatteryCharge > 20.0 &&
        !ActiveErrors.Any(e => e.ErrorLevel == "FATAL");

    public int NextHeaderId() => Interlocked.Increment(ref _headerIdCounter);
}

public enum VehicleStatus
{
    Unknown,
    Idle,
    Driving,
    Busy,
    Charging,
    Error,
    Offline
}

// ── Transport Order ───────────────────────────────────────────────────────────

public class TransportOrder
{
    public string          OrderId     { get; }
    public string          SourceId    { get; }
    public string          DestId      { get; }
    public string?         LoadId      { get; }
    public TransportStatus Status      { get; private set; } = TransportStatus.Pending;
    public string?         AssignedVehicleId { get; private set; }
    public DateTime        CreatedAt   { get; } = DateTime.UtcNow;

    public TransportOrder(string orderId, string sourceId, string destId, string? loadId = null)
    {
        OrderId  = orderId;
        SourceId = sourceId;
        DestId   = destId;
        LoadId   = loadId;
    }

    public void Assign(string vehicleId)
    {
        AssignedVehicleId = vehicleId;
        Status            = TransportStatus.Assigned;
    }

    public void Start()    => Status = TransportStatus.InProgress;
    public void Complete() => Status = TransportStatus.Completed;
    public void Fail()     => Status = TransportStatus.Failed;
}

public enum TransportStatus
{
    Pending,
    Assigned,
    InProgress,
    Completed,
    Failed
}

// ── Topology (Graph) ──────────────────────────────────────────────────────────

public class TopologyMap
{
    private readonly Dictionary<string, NodePosition> _nodes = [];
    private readonly Dictionary<string, (string From, string To)> _edges = [];

    public void AddNode(string nodeId, double x, double y, double theta, string mapId)
        => _nodes[nodeId] = new NodePosition { X = x, Y = y, Theta = theta, MapId = mapId };

    public void AddEdge(string edgeId, string fromNodeId, string toNodeId)
        => _edges[edgeId] = (fromNodeId, toNodeId);

    public NodePosition? GetNode(string nodeId)
        => _nodes.GetValueOrDefault(nodeId);

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

    // Simplified: returns direct edge between source and dest station nodes.
    // In production: A* or Dijkstra over the graph.
    public (List<Node> Nodes, List<Edge> Edges) BuildPath(string sourceNodeId, string destNodeId,
        List<VdaAction> pickActions, List<VdaAction> dropActions)
    {
        var sourcePos = GetNode(sourceNodeId) ?? throw new InvalidOperationException($"Node {sourceNodeId} not found");
        var destPos   = GetNode(destNodeId)   ?? throw new InvalidOperationException($"Node {destNodeId} not found");

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
                Released     = true,          // release immediately — extend for zone blocking
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
                MaxSpeed    = 1.5
            }
        };

        return (nodes, edges);
    }
}
