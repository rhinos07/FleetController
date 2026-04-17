using FleetManager.Api.Hubs;
using FleetManager.Api.Models;
using Microsoft.AspNetCore.SignalR;

namespace FleetManager.Api.Services;

public sealed class SignalRDashboardNotifier(IHubContext<DashboardHub> hubContext) : IDashboardNotifier
{
    public Task OrderUpdatedAsync(TransportOrderOutcome orderOutcome)
        => hubContext.Clients.All.SendAsync("orderUpdated", orderOutcome);

    public Task VehicleUpdatedAsync(VehicleState vehicleState)
        => hubContext.Clients.All.SendAsync("vehicleUpdated", vehicleState);

    public Task ZoneBlockChangedAsync(string zoneId, bool blocked)
        => hubContext.Clients.All.SendAsync("zoneBlockChanged", new { zoneId, blocked });
}
