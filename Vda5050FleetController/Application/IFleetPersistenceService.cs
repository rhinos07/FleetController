using Vda5050FleetController.Application.Contracts;
using Vda5050FleetController.Domain.Models;
using Vda5050FleetController.Infrastructure.Persistence;

namespace Vda5050FleetController.Application;

// ── Persistence service interface ─────────────────────────────────────────────

/// <summary>
/// Interface for persisting fleet data to a durable store.
/// </summary>
public interface IFleetPersistenceService
{
    /// <summary>
    /// Saves or updates a vehicle record.
    /// </summary>
    Task SaveVehicleAsync(Vehicle vehicle, CancellationToken ct = default);

    /// <summary>
    /// Saves or updates a transport order.
    /// </summary>
    Task SaveOrderAsync(TransportOrder order, CancellationToken ct = default);

    /// <summary>
    /// Moves a completed order to the history table.
    /// </summary>
    Task CompleteOrderAsync(TransportOrder completedOrder, CancellationToken ct = default);

    /// <summary>
    /// Retrieves completed/failed order history.
    /// </summary>
    Task<List<OrderHistoryRecord>> GetOrderHistoryAsync(int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all active (non-completed) orders.
    /// </summary>
    Task<List<OrderRecord>> GetActiveOrdersAsync(CancellationToken ct = default);

    // Topology

    /// <summary>
    /// Saves or updates a topology node.
    /// </summary>
    Task SaveNodeAsync(TopologyNodeDto node, CancellationToken ct = default);

    /// <summary>
    /// Deletes a topology node by ID.
    /// </summary>
    Task DeleteNodeAsync(string nodeId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all topology nodes.
    /// </summary>
    Task<List<NodeRecord>> GetAllNodesAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves or updates a topology edge.
    /// </summary>
    Task SaveEdgeAsync(TopologyEdgeDto edge, CancellationToken ct = default);

    /// <summary>
    /// Deletes a topology edge by ID.
    /// </summary>
    Task DeleteEdgeAsync(string edgeId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all topology edges.
    /// </summary>
    Task<List<EdgeRecord>> GetAllEdgesAsync(CancellationToken ct = default);
}

// ── No-op implementation (used in tests / when DB is not configured) ──────────

/// <summary>
/// No-operation implementation of IFleetPersistenceService.
/// Used when no database is configured or during testing.
/// </summary>
public sealed class NoOpFleetPersistenceService : IFleetPersistenceService
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static NoOpFleetPersistenceService Instance { get; } = new();

    private NoOpFleetPersistenceService() { }

    public Task SaveVehicleAsync(Vehicle vehicle, CancellationToken ct = default)            => Task.CompletedTask;
    public Task SaveOrderAsync(TransportOrder order, CancellationToken ct = default)          => Task.CompletedTask;
    public Task CompleteOrderAsync(TransportOrder completedOrder, CancellationToken ct = default) => Task.CompletedTask;

    public Task<List<OrderHistoryRecord>> GetOrderHistoryAsync(int limit = 100, CancellationToken ct = default)
        => Task.FromResult(new List<OrderHistoryRecord>());

    public Task<List<OrderRecord>> GetActiveOrdersAsync(CancellationToken ct = default)
        => Task.FromResult(new List<OrderRecord>());

    public Task SaveNodeAsync(TopologyNodeDto node, CancellationToken ct = default)    => Task.CompletedTask;
    public Task DeleteNodeAsync(string nodeId, CancellationToken ct = default)       => Task.CompletedTask;
    public Task<List<NodeRecord>> GetAllNodesAsync(CancellationToken ct = default)
        => Task.FromResult(new List<NodeRecord>());

    public Task SaveEdgeAsync(TopologyEdgeDto edge, CancellationToken ct = default)    => Task.CompletedTask;
    public Task DeleteEdgeAsync(string edgeId, CancellationToken ct = default)       => Task.CompletedTask;
    public Task<List<EdgeRecord>> GetAllEdgesAsync(CancellationToken ct = default)
        => Task.FromResult(new List<EdgeRecord>());
}
