using Microsoft.AspNetCore.SignalR;
using Vda5050FleetController.Application;

namespace Vda5050FleetController.Realtime;

public class FleetStatusHub(FleetController fleetController) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("fleetStatusUpdated", fleetController.GetStatus());
        await base.OnConnectedAsync();
    }
}
