using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vda5050FleetController.Domain.Models;
using Vda5050FleetController.Infrastructure.Persistence;

namespace Vda5050FleetController.Application;

/// <summary>
/// Postgres-backed implementation of IFleetPersistenceService.
/// Uses IServiceScopeFactory so it can be registered as a singleton alongside
/// FleetController, while each DB operation gets a short-lived scoped
/// FleetDbContext (one DataConnection per call). Exceptions are swallowed so
/// persistence failures never disrupt real-time fleet control.
/// </summary>
public class PostgresFleetPersistenceService : IFleetPersistenceService
{
    private readonly IServiceScopeFactory                     _scopeFactory;
    private readonly ILogger<PostgresFleetPersistenceService> _log;

    public PostgresFleetPersistenceService(IServiceScopeFactory scopeFactory,
        ILogger<PostgresFleetPersistenceService> log)
    {
        _scopeFactory = scopeFactory;
        _log          = log;
    }

    // ── Vehicles ──────────────────────────────────────────────────────────────

    public async Task SaveVehicleAsync(Vehicle vehicle, CancellationToken ct = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IFleetRepository>();
            await repo.UpsertVehicleAsync(vehicle, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to persist vehicle {VehicleId}", vehicle.VehicleId);
        }
    }

    // ── Orders ────────────────────────────────────────────────────────────────

    public async Task SaveOrderAsync(TransportOrder order, CancellationToken ct = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IFleetRepository>();
            await repo.UpsertOrderAsync(order, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to persist order {OrderId}", order.OrderId);
        }
    }

    public async Task CompleteOrderAsync(TransportOrder completedOrder, CancellationToken ct = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IFleetRepository>();
            await repo.UpsertOrderAsync(completedOrder, ct);
            await repo.AddOrderHistoryAsync(completedOrder, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to persist order history for {OrderId}", completedOrder.OrderId);
        }
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<List<OrderHistoryRecord>> GetOrderHistoryAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IFleetRepository>();
        return await repo.GetOrderHistoryAsync(limit, ct);
    }

    public async Task<List<OrderRecord>> GetActiveOrdersAsync(CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IFleetRepository>();
        return await repo.GetActiveOrdersAsync(ct);
    }

    // ── Topology ──────────────────────────────────────────────────────────────

    public async Task SaveNodeAsync(TopologyNode node, CancellationToken ct = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IFleetRepository>();
            await repo.UpsertNodeAsync(new NodeRecord
            {
                NodeId = node.NodeId,
                X      = node.X,
                Y      = node.Y,
                Theta  = node.Theta,
                MapId  = node.MapId
            }, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to persist node {NodeId}", Sanitize(node.NodeId));
        }
    }

    public async Task DeleteNodeAsync(string nodeId, CancellationToken ct = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IFleetRepository>();
            await repo.DeleteNodeAsync(nodeId, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to delete node {NodeId}", Sanitize(nodeId));
        }
    }

    public async Task<List<NodeRecord>> GetAllNodesAsync(CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IFleetRepository>();
        return await repo.GetAllNodesAsync(ct);
    }

    public async Task SaveEdgeAsync(TopologyEdge edge, CancellationToken ct = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IFleetRepository>();
            await repo.UpsertEdgeAsync(new EdgeRecord
            {
                EdgeId     = edge.EdgeId,
                FromNodeId = edge.From,
                ToNodeId   = edge.To
            }, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to persist edge {EdgeId}", Sanitize(edge.EdgeId));
        }
    }

    public async Task DeleteEdgeAsync(string edgeId, CancellationToken ct = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IFleetRepository>();
            await repo.DeleteEdgeAsync(edgeId, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to delete edge {EdgeId}", Sanitize(edgeId));
        }
    }

    public async Task<List<EdgeRecord>> GetAllEdgesAsync(CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IFleetRepository>();
        return await repo.GetAllEdgesAsync(ct);
    }

    // Removes newline characters from log arguments to prevent log-forging attacks.
    private static string Sanitize(string value)
        => value.Replace("\r", string.Empty).Replace("\n", string.Empty);
}
