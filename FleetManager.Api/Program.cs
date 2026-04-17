using FleetManager.Api.Data;
using FleetManager.Api.Hubs;
using FleetManager.Api.Models;
using FleetManager.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

builder.Services.AddSingleton<RouteGraphService>();
builder.Services.AddSingleton<VehicleStateStore>();
builder.Services.AddSingleton<OrderService>();
builder.Services.AddSingleton<IDashboardNotifier, SignalRDashboardNotifier>();

builder.Services.AddScoped(_ =>
    new FleetDb(builder.Configuration.GetConnectionString("FleetManager")
                ?? throw new InvalidOperationException("Connection string 'FleetManager' is required.")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/api/routes/graph", (RouteGraphService graph) =>
    Results.Ok(new
    {
        nodes = graph.Nodes,
        edges = graph.Edges,
        blockedZones = graph.BlockedZones
    }));

app.MapPost("/api/orders", async (TransportOrderRequest request, OrderService orderService) =>
{
    var outcome = await orderService.CreateOrderAsync(request);
    return outcome.Status == TransportOrderStatus.Accepted
        ? Results.Created($"/api/orders/{outcome.Order!.OrderId}", outcome)
        : Results.BadRequest(outcome);
});

app.MapGet("/api/vehicles", (VehicleStateStore vehicles) => Results.Ok(vehicles.GetAll()));

app.MapPost("/api/agv/vda5050/state", async (VehicleStateUpdateRequest request, VehicleStateStore vehicles, IDashboardNotifier notifier) =>
{
    var updated = vehicles.Upsert(request.VehicleId, request.CurrentNode, request.State, request.LastMessageAtUtc ?? DateTimeOffset.UtcNow);
    await notifier.VehicleUpdatedAsync(updated);

    return Results.Ok(updated);
});

app.MapPost("/api/zones/{zoneId}/block", async (string zoneId, bool blocked, RouteGraphService graph, IDashboardNotifier notifier) =>
{
    graph.SetZoneBlocked(zoneId, blocked);
    await notifier.ZoneBlockChangedAsync(zoneId, blocked);

    return Results.Ok(new { zoneId, blocked });
});

app.MapHub<DashboardHub>("/hubs/dashboard");

app.Run();
