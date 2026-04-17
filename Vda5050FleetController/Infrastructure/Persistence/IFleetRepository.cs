using Vda5050FleetController.Domain.Models;

namespace Vda5050FleetController.Infrastructure.Persistence;

public interface IFleetRepository
{
    // Vehicles
    Task UpsertVehicleAsync(Vehicle vehicle, CancellationToken ct = default);

    // Orders (active / pending)
    Task UpsertOrderAsync(TransportOrder order, CancellationToken ct = default);
    Task<List<OrderRecord>> GetActiveOrdersAsync(CancellationToken ct = default);

    // Order history
    Task AddOrderHistoryAsync(TransportOrder completedOrder, CancellationToken ct = default);
    Task<List<OrderHistoryRecord>> GetOrderHistoryAsync(int limit = 100, CancellationToken ct = default);
}
