using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.PostgreSQL;
using LinqToDB.DataProvider;
using LinqToDB.Mapping;

namespace FleetManager.Api.Data;

public sealed class FleetDb(string connectionString) : DataConnection(
    new DataOptions().UseConnectionString(PostgreSQLTools.GetDataProvider(PostgreSQLVersion.v95), connectionString))
{
    public ITable<VehicleEntity> Vehicles => this.GetTable<VehicleEntity>();
    public ITable<TransportOrderEntity> TransportOrders => this.GetTable<TransportOrderEntity>();
}

[Table(Schema = "public", Name = "vehicles")]
public sealed class VehicleEntity
{
    [PrimaryKey, NotNull]
    public string VehicleId { get; set; } = string.Empty;

    [NotNull]
    public string CurrentNode { get; set; } = string.Empty;

    [NotNull]
    public string State { get; set; } = string.Empty;

    [NotNull]
    public DateTimeOffset LastMessageAtUtc { get; set; }
}

[Table(Schema = "public", Name = "transport_orders")]
public sealed class TransportOrderEntity
{
    [PrimaryKey, NotNull]
    public string OrderId { get; set; } = string.Empty;

    [NotNull]
    public string Hu { get; set; } = string.Empty;

    [NotNull]
    public string SourceNodeId { get; set; } = string.Empty;

    [NotNull]
    public string DestinationNodeId { get; set; } = string.Empty;

    [NotNull]
    public DateTimeOffset CreatedAtUtc { get; set; }
}
