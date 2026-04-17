using Microsoft.AspNetCore.SignalR;
using Vda5050FleetController.Application;

namespace Vda5050FleetController.Realtime;

public class SignalRFleetStatusPublisher(
    IHubContext<FleetStatusHub> hubContext,
    ILogger<SignalRFleetStatusPublisher> log) : IFleetStatusPublisher
{
    public async Task PublishAsync(FleetStatus status, CancellationToken ct = default)
    {
        try
        {
            await hubContext.Clients.All.SendAsync("fleetStatusUpdated", status, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to publish live fleet status update");
        }
    }
}
