namespace FleetManager.Api.Models;

public sealed record RouteNode(string NodeId, string ZoneId);
public sealed record RouteEdge(string FromNodeId, string ToNodeId);

public sealed record TransportOrderRequest(string Hu, string SourceNodeId, string DestinationNodeId);

public enum TransportOrderStatus
{
    Accepted,
    RejectedBlockedZone,
    RejectedNoRoute,
    RejectedUnknownNode
}

public sealed record TransportOrder(
    string OrderId,
    string Hu,
    string SourceNodeId,
    string DestinationNodeId,
    IReadOnlyCollection<string> PlannedRoute,
    DateTimeOffset CreatedAtUtc);

public sealed record TransportOrderOutcome(
    TransportOrderStatus Status,
    string Message,
    TransportOrder? Order = null);

public sealed record VehicleState(
    string VehicleId,
    string CurrentNode,
    string State,
    DateTimeOffset LastMessageAtUtc);

public sealed record VehicleStateUpdateRequest(
    string VehicleId,
    string CurrentNode,
    string State,
    DateTimeOffset? LastMessageAtUtc);
