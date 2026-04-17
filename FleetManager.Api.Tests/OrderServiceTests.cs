using FleetManager.Api.Models;
using FleetManager.Api.Services;

namespace FleetManager.Api.Tests;

public class OrderServiceTests
{
    [Fact]
    public async Task CreateOrderAsync_Accepts_Order_When_Route_Available()
    {
        var graph = new RouteGraphService();
        var service = new OrderService(graph, new NoOpDashboardNotifier());

        var outcome = await service.CreateOrderAsync(new("HU-001", "INBOUND", "OUTBOUND"));

        Assert.Equal(TransportOrderStatus.Accepted, outcome.Status);
        Assert.NotNull(outcome.Order);
        Assert.Equal("HU-001", outcome.Order!.Hu);
        Assert.Equal(new[] { "INBOUND", "BUFFER-1", "BUFFER-2", "OUTBOUND" }, outcome.Order.PlannedRoute);
    }

    [Fact]
    public async Task CreateOrderAsync_Rejects_Order_When_Zone_Is_Blocked()
    {
        var graph = new RouteGraphService();
        graph.SetZoneBlocked("ZONE-OUT", blocked: true);
        var service = new OrderService(graph, new NoOpDashboardNotifier());

        var outcome = await service.CreateOrderAsync(new("HU-002", "INBOUND", "OUTBOUND"));

        Assert.Equal(TransportOrderStatus.RejectedBlockedZone, outcome.Status);
        Assert.Null(outcome.Order);
    }

    private sealed class NoOpDashboardNotifier : IDashboardNotifier
    {
        public Task OrderUpdatedAsync(TransportOrderOutcome orderOutcome) => Task.CompletedTask;

        public Task VehicleUpdatedAsync(VehicleState vehicleState) => Task.CompletedTask;

        public Task ZoneBlockChangedAsync(string zoneId, bool blocked) => Task.CompletedTask;
    }
}
