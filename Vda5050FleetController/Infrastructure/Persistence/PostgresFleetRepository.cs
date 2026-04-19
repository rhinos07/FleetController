using LinqToDB;
using LinqToDB.Data;
using Vda5050FleetController.Domain.Models;

namespace Vda5050FleetController.Infrastructure.Persistence;

public class PostgresFleetRepository : IFleetRepository
{
    private readonly FleetDbContext _db;

    public PostgresFleetRepository(FleetDbContext db) => _db = db;

    // ── Vehicles ──────────────────────────────────────────────────────────────

    public Task UpsertVehicleAsync(Vehicle vehicle, CancellationToken ct = default)
    {
        var record = new VehicleRecord
        {
            VehicleId      = vehicle.VehicleId,
            Manufacturer   = vehicle.Manufacturer,
            SerialNumber   = vehicle.SerialNumber,
            Status         = vehicle.Status.ToString(),
            BatteryCharge  = vehicle.Battery?.BatteryCharge,
            PositionX      = vehicle.Position?.X,
            PositionY      = vehicle.Position?.Y,
            PositionMapId  = vehicle.Position?.MapId,
            CurrentOrderId = vehicle.CurrentOrderId,
            LastSeen       = vehicle.LastSeen,
            UpdatedAt      = DateTime.UtcNow
        };

        return _db.InsertOrReplaceAsync(record, token: ct);
    }

    // ── Orders ────────────────────────────────────────────────────────────────

    public Task UpsertOrderAsync(TransportOrder order, CancellationToken ct = default)
    {
        var record = new OrderRecord
        {
            OrderId           = order.OrderId,
            SourceId          = order.SourceId,
            DestId            = order.DestId,
            LoadId            = order.LoadId,
            Status            = order.Status.ToString(),
            AssignedVehicleId = order.AssignedVehicleId,
            CreatedAt         = order.CreatedAt,
            UpdatedAt         = DateTime.UtcNow
        };

        return _db.InsertOrReplaceAsync(record, token: ct);
    }

    public Task<List<OrderRecord>> GetActiveOrdersAsync(CancellationToken ct = default)
        => _db.Orders
              .Where(o => o.Status != nameof(TransportStatus.Completed)
                       && o.Status != nameof(TransportStatus.Failed))
              .OrderBy(o => o.CreatedAt)
              .ToListAsync(token: ct);

    // ── Order history ─────────────────────────────────────────────────────────

    public Task AddOrderHistoryAsync(TransportOrder completedOrder, CancellationToken ct = default)
    {
        var record = new OrderHistoryRecord
        {
            OrderId           = completedOrder.OrderId,
            SourceId          = completedOrder.SourceId,
            DestId            = completedOrder.DestId,
            LoadId            = completedOrder.LoadId,
            FinalStatus       = completedOrder.Status.ToString(),
            AssignedVehicleId = completedOrder.AssignedVehicleId,
            CreatedAt         = completedOrder.CreatedAt,
            CompletedAt       = DateTime.UtcNow
        };

        return _db.InsertAsync(record, token: ct);
    }

    public Task<List<OrderHistoryRecord>> GetOrderHistoryAsync(int limit = 100, CancellationToken ct = default)
        => _db.OrderHistory
              .OrderByDescending(h => h.CompletedAt)
              .Take(limit)
              .ToListAsync(token: ct);

    // ── Topology nodes ────────────────────────────────────────────────────────

    public Task UpsertNodeAsync(NodeRecord node, CancellationToken ct = default)
        => _db.InsertOrReplaceAsync(node, token: ct);

    public async Task DeleteNodeAsync(string nodeId, CancellationToken ct = default)
        => await _db.TopologyNodes
                    .Where(n => n.NodeId == nodeId)
                    .DeleteAsync(token: ct);

    public Task<List<NodeRecord>> GetAllNodesAsync(CancellationToken ct = default)
        => _db.TopologyNodes.ToListAsync(token: ct);

    // ── Topology edges ────────────────────────────────────────────────────────

    public Task UpsertEdgeAsync(EdgeRecord edge, CancellationToken ct = default)
        => _db.InsertOrReplaceAsync(edge, token: ct);

    public async Task DeleteEdgeAsync(string edgeId, CancellationToken ct = default)
        => await _db.TopologyEdges
                    .Where(e => e.EdgeId == edgeId)
                    .DeleteAsync(token: ct);

    public Task<List<EdgeRecord>> GetAllEdgesAsync(CancellationToken ct = default)
        => _db.TopologyEdges.ToListAsync(token: ct);
}
