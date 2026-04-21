using Microsoft.Extensions.Logging;
using Vda5050FleetController.Domain.Models;

namespace Vda5050FleetController.Application;

public class VehicleRegistry
{
    private readonly Dictionary<string, Vehicle> _vehicles = [];
    private readonly ILogger<VehicleRegistry>    _log;

    public VehicleRegistry(ILogger<VehicleRegistry> log) => _log = log;

    public Vehicle GetOrCreate(string manufacturer, string serial)
    {
        var id = $"{manufacturer}/{serial}";
        if (!_vehicles.TryGetValue(id, out var vehicle))
        {
            vehicle       = new Vehicle(manufacturer, serial);
            _vehicles[id] = vehicle;
            _log.LogInformation("Registered new vehicle {VehicleId}", id);
        }
        return vehicle;
    }

    public Vehicle? Find(string vehicleId)
        => _vehicles.GetValueOrDefault(vehicleId);

    public IEnumerable<Vehicle> All()
        => _vehicles.Values;

    public Vehicle? FindAvailable()
        => _vehicles.Values.FirstOrDefault(v => v.IsAvailable);
}
