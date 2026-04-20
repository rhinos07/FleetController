using Vda5050FleetController.Domain.Models;

namespace Vda5050FleetController.Application.Contracts;

// ── Fleet Status DTOs ─────────────────────────────────────────────────────────

/// <summary>
/// Data transfer object representing the current state of the entire fleet.
/// This is the primary response type for fleet status queries and real-time updates.
/// </summary>
public record FleetStatus
{
    /// <summary>
    /// Summary information for all registered vehicles.
    /// </summary>
    public List<VehicleSummary> Vehicles { get; init; } = [];

    /// <summary>
    /// Number of orders waiting to be assigned to a vehicle.
    /// </summary>
    public int PendingOrders { get; init; }

    /// <summary>
    /// Number of orders currently being executed.
    /// </summary>
    public int ActiveOrders { get; init; }

    /// <summary>
    /// All nodes in the facility topology.
    /// </summary>
    public List<TopologyNodeDto> Nodes { get; init; } = [];

    /// <summary>
    /// All edges connecting nodes in the facility topology.
    /// </summary>
    public List<TopologyEdgeDto> Edges { get; init; } = [];

    /// <summary>
    /// Summary of all active and pending orders.
    /// </summary>
    public List<OrderSummary> Orders { get; init; } = [];
}

/// <summary>
/// Summary information about a single vehicle in the fleet.
/// </summary>
public record VehicleSummary
{
    /// <summary>
    /// Unique vehicle identifier (manufacturer/serialNumber).
    /// </summary>
    public string VehicleId { get; init; } = string.Empty;

    /// <summary>
    /// Current operational status.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Current position of the vehicle, if known.
    /// </summary>
    public AgvPosition? Position { get; init; }

    /// <summary>
    /// Current battery charge percentage (0-100).
    /// </summary>
    public double? Battery { get; init; }

    /// <summary>
    /// ID of the currently assigned order, if any.
    /// </summary>
    public string? OrderId { get; init; }

    /// <summary>
    /// Timestamp of the last received message from this vehicle.
    /// </summary>
    public DateTime LastSeen { get; init; }
}

/// <summary>
/// Data transfer object for topology nodes.
/// </summary>
public record TopologyNodeDto
{
    /// <summary>
    /// Unique identifier for the node.
    /// </summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>
    /// X coordinate in the map coordinate system.
    /// </summary>
    public double X { get; init; }

    /// <summary>
    /// Y coordinate in the map coordinate system.
    /// </summary>
    public double Y { get; init; }

    /// <summary>
    /// Orientation angle in radians.
    /// </summary>
    public double Theta { get; init; }

    /// <summary>
    /// Identifier of the map this node belongs to.
    /// </summary>
    public string MapId { get; init; } = string.Empty;
}

/// <summary>
/// Data transfer object for topology edges.
/// </summary>
public record TopologyEdgeDto
{
    /// <summary>
    /// Unique identifier for the edge.
    /// </summary>
    public string EdgeId { get; init; } = string.Empty;

    /// <summary>
    /// ID of the starting node.
    /// </summary>
    public string From { get; init; } = string.Empty;

    /// <summary>
    /// ID of the ending node.
    /// </summary>
    public string To { get; init; } = string.Empty;
}

/// <summary>
/// Summary information about a transport order.
/// </summary>
public record OrderSummary
{
    /// <summary>
    /// Unique order identifier.
    /// </summary>
    public string OrderId { get; init; } = string.Empty;

    /// <summary>
    /// ID of the source station.
    /// </summary>
    public string SourceId { get; init; } = string.Empty;

    /// <summary>
    /// ID of the destination station.
    /// </summary>
    public string DestId { get; init; } = string.Empty;

    /// <summary>
    /// Optional load identifier.
    /// </summary>
    public string? LoadId { get; init; }

    /// <summary>
    /// Current status of the order.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// ID of the assigned vehicle, if any.
    /// </summary>
    public string? VehicleId { get; init; }
}
