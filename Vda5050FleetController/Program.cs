using LinqToDB;
using LinqToDB.AspNet;
using LinqToDB.AspNet.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Vda5050FleetController.Application;
using Vda5050FleetController.Domain.Models;
using Vda5050FleetController.Infrastructure.Mqtt;
using Vda5050FleetController.Infrastructure.Persistence;
using Vda5050FleetController.Realtime;

var builder = WebApplication.CreateBuilder(args);

// ── Logging (Serilog) ─────────────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, _, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext();

    var seqUrl = ctx.Configuration["SEQ_URL"];
    if (!string.IsNullOrWhiteSpace(seqUrl))
        cfg.WriteTo.Seq(seqUrl);
});

// ── Configuration ─────────────────────────────────────────────────────────────
builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection("Mqtt"));

if (builder.Environment.IsEnvironment("UITests"))
    builder.Host.ConfigureHostOptions(
        o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

// ── Domain / Application ──────────────────────────────────────────────────────
builder.Services.AddSingleton<VehicleRegistry>();
builder.Services.AddSingleton<TransportOrderQueue>();
builder.Services.AddSingleton<FleetController>();
builder.Services.AddSingleton<IFleetStatusPublisher, SignalRFleetStatusPublisher>();
builder.Services.AddSingleton<TopologyMap>();

// ── Infrastructure ────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IVda5050MqttService, Vda5050MqttService>();

// ── Persistence (PostgreSQL + linq2db) ────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("Fleet");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddLinqToDBContext<FleetDbContext>((provider, options) =>
        options
            .UsePostgreSQL(connectionString)
            .UseDefaultLogging(provider));

    builder.Services.AddScoped<IFleetRepository, PostgresFleetRepository>();
    builder.Services.AddSingleton<IFleetPersistenceService, PostgresFleetPersistenceService>();
    builder.Services.AddHostedService<SchemaInitializer>();
    builder.Services.AddHostedService<TopologyStartupLoader>();
}
else
{
    builder.Services.AddSingleton<IFleetPersistenceService>(_ => NoOpFleetPersistenceService.Instance);
}

// ── Hosted service: connect MQTT on startup ───────────────────────────────────
builder.Services.AddHostedService<MqttBackgroundService>();

// ── Web API ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

// ── Endpoints ─────────────────────────────────────────────────────────────────

// GET /fleet/status — current fleet status
app.MapGet("/fleet/status", (FleetController fc) =>
    Results.Ok(fc.GetStatus()))
    .WithName("GetFleetStatus")
    .WithSummary("Get status of all vehicles and orders");

// POST /fleet/orders — submit transport order from WMS/MFR
app.MapPost("/fleet/orders", async (TransportRequest req, FleetController fc, CancellationToken ct) =>
{
    await fc.RequestTransportAsync(req.SourceStationId, req.DestStationId, req.LoadId, ct);
    return Results.Accepted();
})
.WithName("RequestTransport")
.WithSummary("Submit a transport order (called by WMS/MFR)");

// POST /fleet/vehicles/{vehicleId}/pause
app.MapPost("/fleet/vehicles/{vehicleId}/pause",
    async (string vehicleId, FleetController fc, CancellationToken ct) =>
    {
        await fc.PauseVehicleAsync(vehicleId, ct);
        return Results.Ok();
    })
    .WithName("PauseVehicle");

// POST /fleet/vehicles/{vehicleId}/resume
app.MapPost("/fleet/vehicles/{vehicleId}/resume",
    async (string vehicleId, FleetController fc, CancellationToken ct) =>
    {
        await fc.ResumeVehicleAsync(vehicleId, ct);
        return Results.Ok();
    })
    .WithName("ResumeVehicle");

// POST /fleet/vehicles/{vehicleId}/charge
app.MapPost("/fleet/vehicles/{vehicleId}/charge",
    async (string vehicleId, FleetController fc, CancellationToken ct) =>
    {
        await fc.StartChargingAsync(vehicleId, ct);
        return Results.Ok();
    })
    .WithName("StartCharging");

app.MapHub<FleetStatusHub>("/hubs/fleet-status");

// GET /fleet/orders — active and pending orders
app.MapGet("/fleet/orders",
    async (IFleetPersistenceService persistence, CancellationToken ct) =>
        Results.Ok(await persistence.GetActiveOrdersAsync(ct)))
    .WithName("GetActiveOrders")
    .WithSummary("Get all active and pending orders from the database");

// GET /fleet/orders/history — completed / failed order history
app.MapGet("/fleet/orders/history",
    async (IFleetPersistenceService persistence, int limit = 100, CancellationToken ct = default) =>
        Results.Ok(await persistence.GetOrderHistoryAsync(limit, ct)))
    .WithName("GetOrderHistory")
    .WithSummary("Get completed and failed order history from the database");

// ── Topology Node Endpoints ───────────────────────────────────────────────────

// GET /fleet/topology/nodes
app.MapGet("/fleet/topology/nodes", (FleetController fc) =>
    Results.Ok(fc.GetStatus().Nodes))
    .WithName("GetTopologyNodes")
    .WithSummary("Get all topology nodes");

// POST /fleet/topology/nodes — add or update a node
app.MapPost("/fleet/topology/nodes",
    async (TopologyNode node, TopologyMap topology, IFleetPersistenceService persistence,
           FleetController fc, CancellationToken ct) =>
    {
        topology.AddNode(node.NodeId, node.X, node.Y, node.Theta, node.MapId);
        await persistence.SaveNodeAsync(node, ct);
        await fc.PublishStatusUpdateAsync(ct);
        return Results.Ok(node);
    })
    .WithName("UpsertTopologyNode")
    .WithSummary("Add or update a topology node");

// DELETE /fleet/topology/nodes/{nodeId}
app.MapDelete("/fleet/topology/nodes/{nodeId}",
    async (string nodeId, TopologyMap topology, IFleetPersistenceService persistence,
           FleetController fc, CancellationToken ct) =>
    {
        topology.RemoveNode(nodeId);
        await persistence.DeleteNodeAsync(nodeId, ct);
        await fc.PublishStatusUpdateAsync(ct);
        return Results.NoContent();
    })
    .WithName("DeleteTopologyNode")
    .WithSummary("Remove a topology node");

// ── Topology Edge Endpoints ───────────────────────────────────────────────────

// GET /fleet/topology/edges
app.MapGet("/fleet/topology/edges", (FleetController fc) =>
    Results.Ok(fc.GetStatus().Edges))
    .WithName("GetTopologyEdges")
    .WithSummary("Get all topology edges");

// POST /fleet/topology/edges — add or update an edge
app.MapPost("/fleet/topology/edges",
    async (TopologyEdge edge, TopologyMap topology, IFleetPersistenceService persistence,
           FleetController fc, CancellationToken ct) =>
    {
        topology.AddEdge(edge.EdgeId, edge.From, edge.To);
        await persistence.SaveEdgeAsync(edge, ct);
        await fc.PublishStatusUpdateAsync(ct);
        return Results.Ok(edge);
    })
    .WithName("UpsertTopologyEdge")
    .WithSummary("Add or update a topology edge");

// DELETE /fleet/topology/edges/{edgeId}
app.MapDelete("/fleet/topology/edges/{edgeId}",
    async (string edgeId, TopologyMap topology, IFleetPersistenceService persistence,
           FleetController fc, CancellationToken ct) =>
    {
        topology.RemoveEdge(edgeId);
        await persistence.DeleteEdgeAsync(edgeId, ct);
        await fc.PublishStatusUpdateAsync(ct);
        return Results.NoContent();
    })
    .WithName("DeleteTopologyEdge")
    .WithSummary("Remove a topology edge");

app.Run();

// ── Request DTOs ──────────────────────────────────────────────────────────────
record TransportRequest(string SourceStationId, string DestStationId, string? LoadId);

// ── Background Service: MQTT lifecycle ───────────────────────────────────────
public class MqttBackgroundService : BackgroundService
{
    private readonly IVda5050MqttService         _mqtt;
    private readonly FleetController             _fc;   // ensure singleton is created
    private readonly ILogger<MqttBackgroundService> _log;

    public MqttBackgroundService(IVda5050MqttService mqtt, FleetController fc,
        ILogger<MqttBackgroundService> log)
    {
        _mqtt = mqtt;
        _fc   = fc;
        _log  = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await _mqtt.ConnectAsync(ct);
        _log.LogInformation("Fleet Controller MQTT connected — waiting for vehicles...");

        await Task.Delay(Timeout.Infinite, ct);
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await _mqtt.DisconnectAsync(ct);
        await base.StopAsync(ct);
    }
}

// ── Startup: Load topology from DB (and optionally seed demo data) ───────────
public class TopologyStartupLoader : IHostedService
{
    private readonly IServiceScopeFactory             _scopeFactory;
    private readonly TopologyMap                      _topology;
    private readonly IFleetPersistenceService         _persistence;
    private readonly ILogger<TopologyStartupLoader>   _log;

    public TopologyStartupLoader(IServiceScopeFactory scopeFactory,
                                 TopologyMap topology,
                                 IFleetPersistenceService persistence,
                                 ILogger<TopologyStartupLoader> log)
    {
        _scopeFactory = scopeFactory;
        _topology     = topology;
        _persistence  = persistence;
        _log          = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _log.LogInformation("Loading topology from database...");
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IFleetRepository>();

        var nodes = await repo.GetAllNodesAsync(ct);
        foreach (var node in nodes)
            _topology.AddNode(node.NodeId, node.X, node.Y, node.Theta, node.MapId);

        var edges = await repo.GetAllEdgesAsync(ct);
        foreach (var edge in edges)
            _topology.AddEdge(edge.EdgeId, edge.FromNodeId, edge.ToNodeId);

        _log.LogInformation("Topology loaded: {NodeCount} nodes, {EdgeCount} edges",
            nodes.Count, edges.Count);

        var seedDemo = string.Equals(
            Environment.GetEnvironmentVariable("SEED_DEMO_TOPOLOGY"), "true",
            StringComparison.OrdinalIgnoreCase);

        if (nodes.Count == 0 && seedDemo)
            await SeedDemoTopologyAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    // ── Demo topology ─────────────────────────────────────────────────────────
    //
    //  Map: DEMO-WAREHOUSE  (canvas: 900×500 px @ 15 px/unit → 60×33 units)
    //
    //   CHG-1 (3,3)                                     CHG-2 (56,3)
    //
    //   IN-A  (5,8)  ─────────────────────────────────  OUT-A (54,8)
    //
    //   IN-B  (5,16) ─────────────────────────────────  OUT-B (54,16)
    //
    //   IN-C  (5,24) ─────────────────────────────────  OUT-C (54,24)
    //
    private static readonly string MapId = "DEMO-WAREHOUSE";
    private static readonly double Pi    = Math.PI;

    private static readonly TopologyNode[] DemoNodes =
    [
        new() { NodeId = "CHG-1", X =  3, Y =  3, Theta = 0,  MapId = "DEMO-WAREHOUSE" },
        new() { NodeId = "CHG-2", X = 56, Y =  3, Theta = Pi, MapId = "DEMO-WAREHOUSE" },
        new() { NodeId = "IN-A",  X =  5, Y =  8, Theta = 0,  MapId = "DEMO-WAREHOUSE" },
        new() { NodeId = "IN-B",  X =  5, Y = 16, Theta = 0,  MapId = "DEMO-WAREHOUSE" },
        new() { NodeId = "IN-C",  X =  5, Y = 24, Theta = 0,  MapId = "DEMO-WAREHOUSE" },
        new() { NodeId = "OUT-A", X = 54, Y =  8, Theta = Pi, MapId = "DEMO-WAREHOUSE" },
        new() { NodeId = "OUT-B", X = 54, Y = 16, Theta = Pi, MapId = "DEMO-WAREHOUSE" },
        new() { NodeId = "OUT-C", X = 54, Y = 24, Theta = Pi, MapId = "DEMO-WAREHOUSE" },
    ];

    private static readonly TopologyEdge[] DemoEdges =
    [
        // IN → OUT (all combinations)
        new() { EdgeId = "E-IN-A-OUT-A", From = "IN-A", To = "OUT-A" },
        new() { EdgeId = "E-IN-A-OUT-B", From = "IN-A", To = "OUT-B" },
        new() { EdgeId = "E-IN-A-OUT-C", From = "IN-A", To = "OUT-C" },
        new() { EdgeId = "E-IN-B-OUT-A", From = "IN-B", To = "OUT-A" },
        new() { EdgeId = "E-IN-B-OUT-B", From = "IN-B", To = "OUT-B" },
        new() { EdgeId = "E-IN-B-OUT-C", From = "IN-B", To = "OUT-C" },
        new() { EdgeId = "E-IN-C-OUT-A", From = "IN-C", To = "OUT-A" },
        new() { EdgeId = "E-IN-C-OUT-B", From = "IN-C", To = "OUT-B" },
        new() { EdgeId = "E-IN-C-OUT-C", From = "IN-C", To = "OUT-C" },
        // Charging → IN stations
        new() { EdgeId = "E-CHG-1-IN-A", From = "CHG-1", To = "IN-A" },
        new() { EdgeId = "E-CHG-1-IN-B", From = "CHG-1", To = "IN-B" },
        new() { EdgeId = "E-CHG-1-IN-C", From = "CHG-1", To = "IN-C" },
        // Charging → OUT stations
        new() { EdgeId = "E-CHG-2-OUT-A", From = "CHG-2", To = "OUT-A" },
        new() { EdgeId = "E-CHG-2-OUT-B", From = "CHG-2", To = "OUT-B" },
        new() { EdgeId = "E-CHG-2-OUT-C", From = "CHG-2", To = "OUT-C" },
    ];

    private async Task SeedDemoTopologyAsync(CancellationToken ct)
    {
        _log.LogInformation("Seeding demo topology ({NodeCount} nodes, {EdgeCount} edges)...",
            DemoNodes.Length, DemoEdges.Length);

        foreach (var node in DemoNodes)
        {
            _topology.AddNode(node.NodeId, node.X, node.Y, node.Theta, node.MapId);
            await _persistence.SaveNodeAsync(node, ct);
        }
        foreach (var edge in DemoEdges)
        {
            _topology.AddEdge(edge.EdgeId, edge.From, edge.To);
            await _persistence.SaveEdgeAsync(edge, ct);
        }

        _log.LogInformation("Demo topology seeded successfully");
    }
}
