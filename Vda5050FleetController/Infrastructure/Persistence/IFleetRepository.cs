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

    // Topology nodes
    Task UpsertNodeAsync(NodeRecord node, CancellationToken ct = default);
    Task DeleteNodeAsync(string nodeId, CancellationToken ct = default);
    Task<List<NodeRecord>> GetAllNodesAsync(CancellationToken ct = default);

    // Topology edges
    Task UpsertEdgeAsync(EdgeRecord edge, CancellationToken ct = default);
    Task DeleteEdgeAsync(string edgeId, CancellationToken ct = default);
    Task<List<EdgeRecord>> GetAllEdgesAsync(CancellationToken ct = default);
}
