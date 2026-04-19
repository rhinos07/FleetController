using Vda5050FleetController.Domain.Models;
using Vda5050FleetController.Infrastructure.Persistence;

namespace Vda5050FleetController.Application;

// ── Persistence service interface ─────────────────────────────────────────────

public interface IFleetPersistenceService
{
    Task SaveVehicleAsync(Vehicle vehicle, CancellationToken ct = default);
    Task SaveOrderAsync(TransportOrder order, CancellationToken ct = default);
    Task CompleteOrderAsync(TransportOrder completedOrder, CancellationToken ct = default);
    Task<List<OrderHistoryRecord>> GetOrderHistoryAsync(int limit = 100, CancellationToken ct = default);
    Task<List<OrderRecord>> GetActiveOrdersAsync(CancellationToken ct = default);

    // Topology
    Task SaveNodeAsync(TopologyNode node, CancellationToken ct = default);
    Task DeleteNodeAsync(string nodeId, CancellationToken ct = default);
    Task<List<NodeRecord>> GetAllNodesAsync(CancellationToken ct = default);
    Task SaveEdgeAsync(TopologyEdge edge, CancellationToken ct = default);
    Task DeleteEdgeAsync(string edgeId, CancellationToken ct = default);
    Task<List<EdgeRecord>> GetAllEdgesAsync(CancellationToken ct = default);
}

// ── No-op implementation (used in tests / when DB is not configured) ──────────

public sealed class NoOpFleetPersistenceService : IFleetPersistenceService
{
    public static NoOpFleetPersistenceService Instance { get; } = new();

    private NoOpFleetPersistenceService() { }

    public Task SaveVehicleAsync(Vehicle vehicle, CancellationToken ct = default)            => Task.CompletedTask;
    public Task SaveOrderAsync(TransportOrder order, CancellationToken ct = default)          => Task.CompletedTask;
    public Task CompleteOrderAsync(TransportOrder completedOrder, CancellationToken ct = default) => Task.CompletedTask;

    public Task<List<OrderHistoryRecord>> GetOrderHistoryAsync(int limit = 100, CancellationToken ct = default)
        => Task.FromResult(new List<OrderHistoryRecord>());

    public Task<List<OrderRecord>> GetActiveOrdersAsync(CancellationToken ct = default)
        => Task.FromResult(new List<OrderRecord>());

    public Task SaveNodeAsync(TopologyNode node, CancellationToken ct = default)    => Task.CompletedTask;
    public Task DeleteNodeAsync(string nodeId, CancellationToken ct = default)       => Task.CompletedTask;
    public Task<List<NodeRecord>> GetAllNodesAsync(CancellationToken ct = default)
        => Task.FromResult(new List<NodeRecord>());

    public Task SaveEdgeAsync(TopologyEdge edge, CancellationToken ct = default)    => Task.CompletedTask;
    public Task DeleteEdgeAsync(string edgeId, CancellationToken ct = default)       => Task.CompletedTask;
    public Task<List<EdgeRecord>> GetAllEdgesAsync(CancellationToken ct = default)
        => Task.FromResult(new List<EdgeRecord>());
}
