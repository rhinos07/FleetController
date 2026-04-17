using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vda5050FleetController.Application;
using Vda5050FleetController.Domain.Models;
using Vda5050FleetController.Infrastructure.Mqtt;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────────
builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection("Mqtt"));

// ── Domain / Application ──────────────────────────────────────────────────────
builder.Services.AddSingleton<VehicleRegistry>();
builder.Services.AddSingleton<TransportOrderQueue>();
builder.Services.AddSingleton<FleetController>();
builder.Services.AddSingleton<TopologyMap>(sp =>
{
    // Demo topology — in production: load from DB or config file
    var map = new TopologyMap();
    map.AddNode("STATION-IN-01",  x:  5.0, y:  3.0, theta: 0.0,    mapId: "FLOOR-1");
    map.AddNode("STATION-IN-02",  x:  5.0, y:  8.0, theta: 0.0,    mapId: "FLOOR-1");
    map.AddNode("STATION-OUT-01", x: 40.0, y:  3.0, theta: 3.1415, mapId: "FLOOR-1");
    map.AddNode("STATION-OUT-02", x: 40.0, y:  8.0, theta: 3.1415, mapId: "FLOOR-1");
    map.AddNode("CHARGING-01",    x:  2.0, y:  2.0, theta: 0.0,    mapId: "FLOOR-1");
    map.AddEdge("E-IN01-OUT01",   "STATION-IN-01",  "STATION-OUT-01");
    map.AddEdge("E-IN01-OUT02",   "STATION-IN-01",  "STATION-OUT-02");
    map.AddEdge("E-IN02-OUT01",   "STATION-IN-02",  "STATION-OUT-01");
    map.AddEdge("E-IN02-OUT02",   "STATION-IN-02",  "STATION-OUT-02");
    return map;
});

// ── Infrastructure ────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IVda5050MqttService, Vda5050MqttService>();

// ── Hosted service: connect MQTT on startup ───────────────────────────────────
builder.Services.AddHostedService<MqttBackgroundService>();

// ── Web API ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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
