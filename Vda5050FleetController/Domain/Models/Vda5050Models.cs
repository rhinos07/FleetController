using System.Text.Json.Serialization;

namespace Vda5050FleetController.Domain.Models;

// ── Shared Header ────────────────────────────────────────────────────────────

public record Vda5050Header
{
    [JsonPropertyName("headerId")]     public int    HeaderId     { get; init; }
    [JsonPropertyName("timestamp")]    public string Timestamp    { get; init; } = DateTime.UtcNow.ToString("o");
    [JsonPropertyName("version")]      public string Version      { get; init; } = "2.0.0";
    [JsonPropertyName("manufacturer")] public string Manufacturer { get; init; } = string.Empty;
    [JsonPropertyName("serialNumber")] public string SerialNumber { get; init; } = string.Empty;
}

// ── Order (Master → Vehicle) ─────────────────────────────────────────────────

public record Order : Vda5050Header
{
    [JsonPropertyName("orderId")]       public string       OrderId       { get; init; } = string.Empty;
    [JsonPropertyName("orderUpdateId")] public int          OrderUpdateId { get; init; }
    [JsonPropertyName("nodes")]         public List<Node>   Nodes         { get; init; } = [];
    [JsonPropertyName("edges")]         public List<Edge>   Edges         { get; init; } = [];
}

public record Node
{
    [JsonPropertyName("nodeId")]       public string        NodeId       { get; init; } = string.Empty;
    [JsonPropertyName("sequenceId")]   public int           SequenceId   { get; init; }
    [JsonPropertyName("released")]     public bool          Released     { get; init; }
    [JsonPropertyName("nodePosition")] public NodePosition? NodePosition { get; init; }
    [JsonPropertyName("actions")]      public List<VdaAction> Actions    { get; init; } = [];
}

public record NodePosition
{
    [JsonPropertyName("x")]     public double X     { get; init; }
    [JsonPropertyName("y")]     public double Y     { get; init; }
    [JsonPropertyName("theta")] public double Theta { get; init; }
    [JsonPropertyName("mapId")] public string MapId { get; init; } = string.Empty;
}

public record Edge
{
    [JsonPropertyName("edgeId")]      public string        EdgeId      { get; init; } = string.Empty;
    [JsonPropertyName("sequenceId")]  public int           SequenceId  { get; init; }
    [JsonPropertyName("released")]    public bool          Released    { get; init; }
    [JsonPropertyName("startNodeId")] public string        StartNodeId { get; init; } = string.Empty;
    [JsonPropertyName("endNodeId")]   public string        EndNodeId   { get; init; } = string.Empty;
    [JsonPropertyName("maxSpeed")]    public double?       MaxSpeed    { get; init; }
    [JsonPropertyName("actions")]     public List<VdaAction> Actions   { get; init; } = [];
}

public record VdaAction
{
    [JsonPropertyName("actionId")]         public string               ActionId         { get; init; } = string.Empty;
    [JsonPropertyName("actionType")]       public string               ActionType       { get; init; } = string.Empty;
    [JsonPropertyName("blockingType")]     public string               BlockingType     { get; init; } = "HARD";
    [JsonPropertyName("actionParameters")] public List<ActionParameter> ActionParameters { get; init; } = [];
}

public record ActionParameter
{
    [JsonPropertyName("key")]   public string Key   { get; init; } = string.Empty;
    [JsonPropertyName("value")] public string Value { get; init; } = string.Empty;
}

// ── InstantActions (Master → Vehicle) ────────────────────────────────────────

public record InstantActions : Vda5050Header
{
    [JsonPropertyName("instantActions")] public List<VdaAction> Actions { get; init; } = [];
}

// ── State (Vehicle → Master) ──────────────────────────────────────────────────

public record VehicleState : Vda5050Header
{
    [JsonPropertyName("orderId")]            public string            OrderId            { get; init; } = string.Empty;
    [JsonPropertyName("orderUpdateId")]      public int               OrderUpdateId      { get; init; }
    [JsonPropertyName("lastNodeId")]         public string            LastNodeId         { get; init; } = string.Empty;
    [JsonPropertyName("lastNodeSequenceId")] public int               LastNodeSequenceId { get; init; }
    [JsonPropertyName("driving")]            public bool              Driving            { get; init; }
    [JsonPropertyName("operatingMode")]      public string            OperatingMode      { get; init; } = "AUTOMATIC";
    [JsonPropertyName("agvPosition")]        public AgvPosition?      AgvPosition        { get; init; }
    [JsonPropertyName("velocity")]           public Velocity?         Velocity           { get; init; }
    [JsonPropertyName("batteryState")]       public BatteryState?     BatteryState       { get; init; }
    [JsonPropertyName("actionStates")]       public List<ActionState> ActionStates       { get; init; } = [];
    [JsonPropertyName("errors")]             public List<VdaError>    Errors             { get; init; } = [];
    [JsonPropertyName("nodeStates")]         public List<NodeState>   NodeStates         { get; init; } = [];
    [JsonPropertyName("edgeStates")]         public List<EdgeState>   EdgeStates         { get; init; } = [];
}

public record AgvPosition
{
    [JsonPropertyName("x")]                   public double X                   { get; init; }
    [JsonPropertyName("y")]                   public double Y                   { get; init; }
    [JsonPropertyName("theta")]               public double Theta               { get; init; }
    [JsonPropertyName("mapId")]               public string MapId               { get; init; } = string.Empty;
    [JsonPropertyName("positionInitialized")] public bool   PositionInitialized { get; init; }
    [JsonPropertyName("localizationScore")]   public double LocalizationScore   { get; init; }
}

public record Velocity
{
    [JsonPropertyName("vx")]    public double Vx    { get; init; }
    [JsonPropertyName("vy")]    public double Vy    { get; init; }
    [JsonPropertyName("omega")] public double Omega { get; init; }
}

public record BatteryState
{
    [JsonPropertyName("batteryCharge")] public double BatteryCharge { get; init; }
    [JsonPropertyName("charging")]      public bool   Charging      { get; init; }
}

public record ActionState
{
    [JsonPropertyName("actionId")]            public string ActionId            { get; init; } = string.Empty;
    [JsonPropertyName("actionStatus")]        public string ActionStatus        { get; init; } = string.Empty;
    [JsonPropertyName("resultDescription")]   public string ResultDescription   { get; init; } = string.Empty;
}

public record VdaError
{
    [JsonPropertyName("errorType")]        public string ErrorType        { get; init; } = string.Empty;
    [JsonPropertyName("errorLevel")]       public string ErrorLevel       { get; init; } = string.Empty;
    [JsonPropertyName("errorDescription")] public string ErrorDescription { get; init; } = string.Empty;
}

public record NodeState
{
    [JsonPropertyName("nodeId")]     public string NodeId     { get; init; } = string.Empty;
    [JsonPropertyName("sequenceId")] public int    SequenceId { get; init; }
    [JsonPropertyName("released")]   public bool   Released   { get; init; }
}

public record EdgeState
{
    [JsonPropertyName("edgeId")]     public string EdgeId     { get; init; } = string.Empty;
    [JsonPropertyName("sequenceId")] public int    SequenceId { get; init; }
    [JsonPropertyName("released")]   public bool   Released   { get; init; }
}

// ── Connection (Vehicle → Master) ────────────────────────────────────────────

public record ConnectionMessage : Vda5050Header
{
    [JsonPropertyName("connectionState")] public string ConnectionState { get; init; } = "ONLINE";
}
