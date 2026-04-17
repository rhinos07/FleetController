using FleetManager.Api.Models;

namespace FleetManager.Api.Services;

public sealed class VehicleStateStore
{
    private readonly Dictionary<string, VehicleState> _states = new(StringComparer.OrdinalIgnoreCase);

    public VehicleState Upsert(string vehicleId, string currentNode, string state, DateTimeOffset lastMessageAtUtc)
    {
        var snapshot = new VehicleState(vehicleId, currentNode, state, lastMessageAtUtc);
        _states[vehicleId] = snapshot;
        return snapshot;
    }

    public IReadOnlyCollection<VehicleState> GetAll() => _states.Values;
}
