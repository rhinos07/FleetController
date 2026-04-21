using FleetController.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Vda5050FleetController.Application;
using Vda5050FleetController.Domain.Models;
using FC = Vda5050FleetController.Application.FleetController;

namespace FleetController.Tests.Application;

public class FleetControllerPersistenceTests
{
    // ── Test fixture helpers ──────────────────────────────────────────────────

    private record PersistenceFixture(
        FC             Controller,
        VehicleRegistry             Registry,
        TransportOrderQueue         Queue,
        FakeMqttService             Mqtt,
        FakeFleetPersistenceService Persistence);

    private static PersistenceFixture CreateFixture()
    {
        var registry    = new VehicleRegistry(NullLogger<VehicleRegistry>.Instance);
        var queue       = new TransportOrderQueue(NullLogger<TransportOrderQueue>.Instance);
        var topology    = new TopologyMap();
        topology.AddNode("SRC", 0.0,  0.0, 0.0, "MAP-1");
        topology.AddNode("DST", 10.0, 0.0, 0.0, "MAP-1");
        var mqtt        = new FakeMqttService();
        var persistence = new FakeFleetPersistenceService();
        var dispatcher  = new VehicleDispatcher(registry, queue, topology, mqtt,
            persistence, NullLogger<VehicleDispatcher>.Instance);
        var controller  = new FC(
            registry, queue, topology, mqtt,
            statusPublisher: null,
            persistence,
            dispatcher,
            NullLogger<FC>.Instance);

        return new PersistenceFixture(controller, registry, queue, mqtt, persistence);
    }

    private static void MakeVehicleAvailable(VehicleRegistry registry,
        string manufacturer = "Acme", string serial = "SN-001")
    {
        var vehicle = registry.GetOrCreate(manufacturer, serial);
        vehicle.ApplyState(new VehicleState
        {
            Manufacturer = manufacturer,
            SerialNumber = serial,
            Driving      = false,
            BatteryState = new BatteryState { BatteryCharge = 80.0 },
            Errors       = [],
            NodeStates   = [],
            EdgeStates   = []
        });
    }

    private static VehicleState StateFor(string manufacturer, string serial,
        bool driving = false, string orderId = "",
        List<NodeState>? nodeStates = null,
        List<EdgeState>? edgeStates = null) => new()
    {
        Manufacturer = manufacturer,
        SerialNumber = serial,
        Driving      = driving,
        OrderId      = orderId,
        BatteryState = new BatteryState { BatteryCharge = 80.0 },
        Errors       = [],
        NodeStates   = nodeStates ?? [],
        EdgeStates   = edgeStates ?? []
    };

    // ── Order persistence ──────────────────────────────────────────────────────

    [Fact]
    public async Task RequestTransportAsync_SavesOrderToPersistence()
    {
        var f = CreateFixture();

        await f.Controller.RequestTransportAsync("SRC", "DST");

        Assert.NotEmpty(f.Persistence.SavedOrders);
    }

    [Fact]
    public async Task RequestTransportAsync_SavesOrderWithCorrectEndpoints()
    {
        var f = CreateFixture();

        await f.Controller.RequestTransportAsync("SRC", "DST", "PALLET-01");

        var saved = f.Persistence.SavedOrders.First();
        Assert.Equal("SRC",       saved.SourceId);
        Assert.Equal("DST",       saved.DestId);
        Assert.Equal("PALLET-01", saved.LoadId);
    }

    [Fact]
    public async Task Dispatch_SavesOrderWithInProgressStatus()
    {
        var f = CreateFixture();
        MakeVehicleAvailable(f.Registry);

        await f.Controller.RequestTransportAsync("SRC", "DST");

        // After dispatch the order status transitions to InProgress
        var dispatched = f.Persistence.SavedOrders
            .LastOrDefault(o => o.Status == TransportStatus.InProgress);
        Assert.NotNull(dispatched);
    }

    // ── Order completion / history persistence ─────────────────────────────────

    [Fact]
    public async Task VehicleStateCompletion_CallsCompleteOrderAsync()
    {
        var f = CreateFixture();
        MakeVehicleAvailable(f.Registry);
        await f.Controller.RequestTransportAsync("SRC", "DST");

        var orderId = f.Mqtt.PublishedOrders.Single().OrderId;

        // Simulate vehicle reporting order finished
        await f.Mqtt.SimulateStateAsync(StateFor("Acme", "SN-001",
            orderId: orderId,
            nodeStates: [],
            edgeStates: []));

        Assert.Single(f.Persistence.CompletedOrders);
        Assert.Equal(orderId, f.Persistence.CompletedOrders[0].OrderId);
    }

    [Fact]
    public async Task VehicleStateCompletion_CompletedOrderHasCompletedStatus()
    {
        var f = CreateFixture();
        MakeVehicleAvailable(f.Registry);
        await f.Controller.RequestTransportAsync("SRC", "DST");

        var orderId = f.Mqtt.PublishedOrders.Single().OrderId;
        await f.Mqtt.SimulateStateAsync(StateFor("Acme", "SN-001",
            orderId: orderId,
            nodeStates: [],
            edgeStates: []));

        Assert.Equal(TransportStatus.Completed, f.Persistence.CompletedOrders[0].Status);
    }

    // ── Vehicle persistence ────────────────────────────────────────────────────

    [Fact]
    public async Task VehicleStateUpdate_SavesVehicleToPersistence()
    {
        var f = CreateFixture();

        await f.Mqtt.SimulateStateAsync(StateFor("Acme", "SN-001"));

        Assert.NotEmpty(f.Persistence.SavedVehicles);
        Assert.Contains(f.Persistence.SavedVehicles, v => v.VehicleId == "Acme/SN-001");
    }

    [Fact]
    public async Task VehicleConnectionUpdate_SavesVehicleToPersistence()
    {
        var f = CreateFixture();

        await f.Mqtt.SimulateConnectionAsync(new ConnectionMessage
        {
            Manufacturer    = "Acme",
            SerialNumber    = "SN-001",
            ConnectionState = "ONLINE"
        });

        Assert.NotEmpty(f.Persistence.SavedVehicles);
        Assert.Contains(f.Persistence.SavedVehicles, v => v.VehicleId == "Acme/SN-001");
    }

    [Fact]
    public async Task VehicleConnectionOffline_SavesVehicleWithOfflineStatus()
    {
        var f = CreateFixture();

        await f.Mqtt.SimulateConnectionAsync(new ConnectionMessage
        {
            Manufacturer    = "Acme",
            SerialNumber    = "SN-001",
            ConnectionState = "OFFLINE"
        });

        var saved = f.Persistence.SavedVehicles.Single(v => v.VehicleId == "Acme/SN-001");
        Assert.Equal(VehicleStatus.Offline, saved.Status);
    }
}
