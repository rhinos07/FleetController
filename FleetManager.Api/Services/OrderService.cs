using FleetManager.Api.Models;

namespace FleetManager.Api.Services;

public sealed class OrderService(RouteGraphService graph, IDashboardNotifier notifier)
{
    public async Task<TransportOrderOutcome> CreateOrderAsync(TransportOrderRequest request)
    {
        if (!graph.HasNode(request.SourceNodeId) || !graph.HasNode(request.DestinationNodeId))
        {
            return new(
                TransportOrderStatus.RejectedUnknownNode,
                "Source or destination node does not exist in the route graph.");
        }

        if (graph.IsZoneBlockedByNode(request.SourceNodeId) || graph.IsZoneBlockedByNode(request.DestinationNodeId))
        {
            return new(
                TransportOrderStatus.RejectedBlockedZone,
                "Source or destination zone is currently blocked.");
        }

        var route = graph.TryFindRoute(request.SourceNodeId, request.DestinationNodeId);
        if (route is null)
        {
            return new(
                TransportOrderStatus.RejectedNoRoute,
                "No route available between source and destination under current zone-blocking constraints.");
        }

        var order = new TransportOrder(
            Guid.NewGuid().ToString("N"),
            request.Hu,
            request.SourceNodeId,
            request.DestinationNodeId,
            route,
            DateTimeOffset.UtcNow);

        var outcome = new TransportOrderOutcome(
            TransportOrderStatus.Accepted,
            "Order accepted for dispatch to AGV fleet.",
            order);

        await notifier.OrderUpdatedAsync(outcome);
        return outcome;
    }
}
