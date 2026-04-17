using Vda5050FleetController.Application;
using Vda5050FleetController.Domain.Models;
using Vda5050FleetController.Infrastructure.Persistence;

namespace FleetManager.Tests.Fakes;

/// <summary>
/// In-memory test double for IFleetPersistenceService.
/// Records all persistence calls so tests can assert on saved state.
/// </summary>
public sealed class FakeFleetPersistenceService : IFleetPersistenceService
{
    public List<Vehicle>            SavedVehicles    { get; } = [];
    public List<TransportOrder>     SavedOrders      { get; } = [];
    public List<TransportOrder>     CompletedOrders  { get; } = [];

    public Task SaveVehicleAsync(Vehicle vehicle, CancellationToken ct = default)
    {
        SavedVehicles.Add(vehicle);
        return Task.CompletedTask;
    }

    public Task SaveOrderAsync(TransportOrder order, CancellationToken ct = default)
    {
        SavedOrders.Add(order);
        return Task.CompletedTask;
    }

    public Task CompleteOrderAsync(TransportOrder completedOrder, CancellationToken ct = default)
    {
        CompletedOrders.Add(completedOrder);
        return Task.CompletedTask;
    }

    public Task<List<OrderHistoryRecord>> GetOrderHistoryAsync(int limit = 100, CancellationToken ct = default)
        => Task.FromResult(new List<OrderHistoryRecord>());

    public Task<List<OrderRecord>> GetActiveOrdersAsync(CancellationToken ct = default)
        => Task.FromResult(new List<OrderRecord>());
}
