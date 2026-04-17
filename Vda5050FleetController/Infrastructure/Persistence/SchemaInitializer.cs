using LinqToDB.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vda5050FleetController.Infrastructure.Persistence;

/// <summary>
/// Runs once at startup and creates the fleet tables if they do not exist.
/// Uses raw DDL so the service is idempotent (safe to run on every start).
/// </summary>
public class SchemaInitializer : IHostedService
{
    private readonly IServiceScopeFactory             _scopeFactory;
    private readonly ILogger<SchemaInitializer>       _log;

    public SchemaInitializer(IServiceScopeFactory scopeFactory,
                             ILogger<SchemaInitializer> log)
    {
        _scopeFactory = scopeFactory;
        _log          = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _log.LogInformation("Initializing fleet database schema...");
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        await db.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS vehicles (
                vehicle_id       TEXT        NOT NULL PRIMARY KEY,
                manufacturer     TEXT        NOT NULL,
                serial_number    TEXT        NOT NULL,
                status           TEXT        NOT NULL,
                battery_charge   DOUBLE PRECISION,
                position_x       DOUBLE PRECISION,
                position_y       DOUBLE PRECISION,
                position_map_id  TEXT,
                current_order_id TEXT,
                last_seen        TIMESTAMPTZ NOT NULL,
                updated_at       TIMESTAMPTZ NOT NULL
            );", ct);

        await db.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS orders (
                order_id             TEXT        NOT NULL PRIMARY KEY,
                source_id            TEXT        NOT NULL,
                dest_id              TEXT        NOT NULL,
                load_id              TEXT,
                status               TEXT        NOT NULL,
                assigned_vehicle_id  TEXT,
                created_at           TIMESTAMPTZ NOT NULL,
                updated_at           TIMESTAMPTZ NOT NULL
            );", ct);

        await db.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS order_history (
                id                   BIGSERIAL   NOT NULL PRIMARY KEY,
                order_id             TEXT        NOT NULL,
                source_id            TEXT        NOT NULL,
                dest_id              TEXT        NOT NULL,
                load_id              TEXT,
                final_status         TEXT        NOT NULL,
                assigned_vehicle_id  TEXT,
                created_at           TIMESTAMPTZ NOT NULL,
                completed_at         TIMESTAMPTZ NOT NULL
            );", ct);

        _log.LogInformation("Fleet database schema ready.");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
