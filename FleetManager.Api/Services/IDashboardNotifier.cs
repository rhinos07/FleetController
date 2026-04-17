using FleetManager.Api.Models;

namespace FleetManager.Api.Services;

public interface IDashboardNotifier
{
    Task OrderUpdatedAsync(TransportOrderOutcome orderOutcome);
    Task VehicleUpdatedAsync(VehicleState vehicleState);
    Task ZoneBlockChangedAsync(string zoneId, bool blocked);
}
