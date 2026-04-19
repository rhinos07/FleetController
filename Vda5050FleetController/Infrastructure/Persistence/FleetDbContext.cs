using LinqToDB;
using LinqToDB.Data;

namespace Vda5050FleetController.Infrastructure.Persistence;

// ── linq2db DataConnection ────────────────────────────────────────────────────

public class FleetDbContext : DataConnection
{
    public FleetDbContext(DataOptions<FleetDbContext> options) : base(options.Options) { }

    public ITable<VehicleRecord>      Vehicles      => this.GetTable<VehicleRecord>();
    public ITable<OrderRecord>        Orders        => this.GetTable<OrderRecord>();
    public ITable<OrderHistoryRecord> OrderHistory  => this.GetTable<OrderHistoryRecord>();
    public ITable<NodeRecord>         TopologyNodes => this.GetTable<NodeRecord>();
    public ITable<EdgeRecord>         TopologyEdges => this.GetTable<EdgeRecord>();
}
