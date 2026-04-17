using LinqToDB.Mapping;

namespace Vda5050FleetController.Infrastructure.Persistence;

// ── Vehicle snapshot ──────────────────────────────────────────────────────────

[Table("vehicles")]
public class VehicleRecord
{
    [Column("vehicle_id"),   PrimaryKey, NotNull] public string  VehicleId       { get; set; } = string.Empty;
    [Column("manufacturer"),             NotNull] public string  Manufacturer    { get; set; } = string.Empty;
    [Column("serial_number"),            NotNull] public string  SerialNumber    { get; set; } = string.Empty;
    [Column("status"),                   NotNull] public string  Status          { get; set; } = string.Empty;
    [Column("battery_charge"),  Nullable]         public double? BatteryCharge   { get; set; }
    [Column("position_x"),      Nullable]         public double? PositionX       { get; set; }
    [Column("position_y"),      Nullable]         public double? PositionY       { get; set; }
    [Column("position_map_id"), Nullable]         public string? PositionMapId   { get; set; }
    [Column("current_order_id"),Nullable]         public string? CurrentOrderId  { get; set; }
    [Column("last_seen"),                NotNull] public DateTime LastSeen        { get; set; }
    [Column("updated_at"),               NotNull] public DateTime UpdatedAt       { get; set; }
}

// ── Active / pending order ────────────────────────────────────────────────────

[Table("orders")]
public class OrderRecord
{
    [Column("order_id"),             PrimaryKey, NotNull] public string  OrderId           { get; set; } = string.Empty;
    [Column("source_id"),                        NotNull] public string  SourceId          { get; set; } = string.Empty;
    [Column("dest_id"),                          NotNull] public string  DestId            { get; set; } = string.Empty;
    [Column("load_id"),              Nullable]            public string? LoadId            { get; set; }
    [Column("status"),                           NotNull] public string  Status            { get; set; } = string.Empty;
    [Column("assigned_vehicle_id"),  Nullable]            public string? AssignedVehicleId { get; set; }
    [Column("created_at"),                       NotNull] public DateTime CreatedAt         { get; set; }
    [Column("updated_at"),                       NotNull] public DateTime UpdatedAt         { get; set; }
}

// ── Completed / failed order history ─────────────────────────────────────────

[Table("order_history")]
public class OrderHistoryRecord
{
    [Column("id"),                   PrimaryKey, Identity] public long    Id                { get; set; }
    [Column("order_id"),                         NotNull]  public string  OrderId           { get; set; } = string.Empty;
    [Column("source_id"),                        NotNull]  public string  SourceId          { get; set; } = string.Empty;
    [Column("dest_id"),                          NotNull]  public string  DestId            { get; set; } = string.Empty;
    [Column("load_id"),              Nullable]             public string? LoadId            { get; set; }
    [Column("final_status"),                     NotNull]  public string  FinalStatus       { get; set; } = string.Empty;
    [Column("assigned_vehicle_id"),  Nullable]             public string? AssignedVehicleId { get; set; }
    [Column("created_at"),                       NotNull]  public DateTime CreatedAt         { get; set; }
    [Column("completed_at"),                     NotNull]  public DateTime CompletedAt       { get; set; }
}
