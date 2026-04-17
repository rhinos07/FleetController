using FleetManager.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Vda5050FleetController.Application;
using Vda5050FleetController.Domain.Models;

namespace FleetManager.Tests.Application;

public class FleetControllerTests
{
    // ── Test fixture helpers ──────────────────────────────────────────────────

    private record Fixture(
        FleetController    Controller,
        VehicleRegistry    Registry,
        TransportOrderQueue Queue,
        FakeMqttService    Mqtt,
        FakeFleetStatusPublisher StatusPublisher);

    private static Fixture CreateFixture()
    {
        var registry = new VehicleRegistry(NullLogger<VehicleRegistry>.Instance);
        var queue    = new TransportOrderQueue(NullLogger<TransportOrderQueue>.Instance);
        var topology = new TopologyMap();
        topology.AddNode("SRC", 0.0,  0.0, 0.0, "MAP-1");
        topology.AddNode("DST", 10.0, 0.0, 0.0, "MAP-1");
        var mqtt       = new FakeMqttService();
        var statusPublisher = new FakeFleetStatusPublisher();
        var controller = new FleetController(
            registry, queue, topology, mqtt,
            statusPublisher,
            NullLogger<FleetController>.Instance);

        return new Fixture(controller, registry, queue, mqtt, statusPublisher);
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

    // ── GetStatus ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetStatus_ReturnsEmptyFleet_WhenNoVehiclesRegistered()
    {
        var f      = CreateFixture();
        var status = f.Controller.GetStatus();

        Assert.Empty(status.Vehicles);
        Assert.Equal(0, status.PendingOrders);
        Assert.Equal(0, status.ActiveOrders);
    }

    [Fact]
    public async Task GetStatus_ReflectsRegisteredVehicles()
    {
        var f = CreateFixture();
        await f.Mqtt.SimulateConnectionAsync(new ConnectionMessage
        {
            Manufacturer    = "Acme",
            SerialNumber    = "SN-001",
            ConnectionState = "ONLINE"
        });

        var status = f.Controller.GetStatus();

        Assert.Single(status.Vehicles);
        Assert.Equal("Acme/SN-001", status.Vehicles[0].VehicleId);
    }

    // ── RequestTransportAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task RequestTransportAsync_RequeuesOrder_WhenNoVehicleAvailable()
    {
        var f = CreateFixture();

        await f.Controller.RequestTransportAsync("SRC", "DST");

        Assert.Equal(1, f.Queue.PendingCount);
        Assert.Empty(f.Mqtt.PublishedOrders);
    }

    [Fact]
    public async Task RequestTransportAsync_DispatchesImmediately_WhenVehicleIsAvailable()
    {
        var f = CreateFixture();
        MakeVehicleAvailable(f.Registry);

        await f.Controller.RequestTransportAsync("SRC", "DST");

        Assert.Single(f.Mqtt.PublishedOrders);
        Assert.Equal(0, f.Queue.PendingCount);
        Assert.Equal(1, f.Queue.ActiveCount);
    }

    [Fact]
    public async Task RequestTransportAsync_OrderContainsExpectedNodes()
    {
        var f = CreateFixture();
        MakeVehicleAvailable(f.Registry);

        await f.Controller.RequestTransportAsync("SRC", "DST", "PALLET-01");

        var published = f.Mqtt.PublishedOrders.Single();
        Assert.Equal(2, published.Nodes.Count);
        Assert.Contains(published.Nodes, n => n.NodeId == "SRC");
        Assert.Contains(published.Nodes, n => n.NodeId == "DST");
    }

    [Fact]
    public async Task RequestTransportAsync_OrderContainsPickAndDropActions()
    {
        var f = CreateFixture();
        MakeVehicleAvailable(f.Registry);

        await f.Controller.RequestTransportAsync("SRC", "DST", "PALLET-01");

        var published = f.Mqtt.PublishedOrders.Single();
        var srcNode   = published.Nodes.Single(n => n.NodeId == "SRC");
        var dstNode   = published.Nodes.Single(n => n.NodeId == "DST");

        Assert.Contains(srcNode.Actions, a => a.ActionType == "pick");
        Assert.Contains(dstNode.Actions, a => a.ActionType == "drop");
    }

    [Fact]
    public async Task RequestTransportAsync_OrderAssignedToVehicle()
    {
        var f = CreateFixture();
        MakeVehicleAvailable(f.Registry);

        await f.Controller.RequestTransportAsync("SRC", "DST");

        var published = f.Mqtt.PublishedOrders.Single();
        Assert.Equal("Acme",   published.Manufacturer);
        Assert.Equal("SN-001", published.SerialNumber);
    }

    [Fact]
    public async Task RequestTransportAsync_PublishesFleetStatusUpdate()
    {
        var f = CreateFixture();

        await f.Controller.RequestTransportAsync("SRC", "DST");

        var status = Assert.Single(f.StatusPublisher.PublishedStatuses);
        Assert.Equal(1, status.PendingOrders);
    }

    // ── Instant actions ───────────────────────────────────────────────────────

    [Fact]
    public async Task PauseVehicleAsync_Throws_WhenVehicleNotFound()
    {
        var f = CreateFixture();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Controller.PauseVehicleAsync("UNKNOWN/SN-999"));
    }

    [Fact]
    public async Task PauseVehicleAsync_PublishesStopPause_WhenVehicleFound()
    {
        var f = CreateFixture();
        f.Registry.GetOrCreate("Acme", "SN-001");

        await f.Controller.PauseVehicleAsync("Acme/SN-001");

        var ia = f.Mqtt.PublishedInstantActions.Single();
        Assert.Single(ia.Actions);
        Assert.Equal("stopPause", ia.Actions[0].ActionType);
    }

    [Fact]
    public async Task ResumeVehicleAsync_PublishesStartPause_WhenVehicleFound()
    {
        var f = CreateFixture();
        f.Registry.GetOrCreate("Acme", "SN-001");

        await f.Controller.ResumeVehicleAsync("Acme/SN-001");

        var ia = f.Mqtt.PublishedInstantActions.Single();
        Assert.Equal("startPause", ia.Actions[0].ActionType);
    }

    [Fact]
    public async Task StartChargingAsync_PublishesStartCharging_WhenVehicleFound()
    {
        var f = CreateFixture();
        f.Registry.GetOrCreate("Acme", "SN-001");

        await f.Controller.StartChargingAsync("Acme/SN-001");

        var ia = f.Mqtt.PublishedInstantActions.Single();
        Assert.Equal("startCharging", ia.Actions[0].ActionType);
    }

    [Fact]
    public async Task InstantActions_ContainHardBlockingType()
    {
        var f = CreateFixture();
        f.Registry.GetOrCreate("Acme", "SN-001");

        await f.Controller.PauseVehicleAsync("Acme/SN-001");

        Assert.Equal("HARD", f.Mqtt.PublishedInstantActions.Single().Actions[0].BlockingType);
    }

    // ── Event handling: vehicle state ─────────────────────────────────────────

    [Fact]
    public async Task SimulatedState_RegistersVehicleInRegistry()
    {
        var f = CreateFixture();

        await f.Mqtt.SimulateStateAsync(StateFor("Acme", "SN-001"));

        Assert.NotNull(f.Registry.Find("Acme/SN-001"));
    }

    [Fact]
    public async Task SimulatedState_CompletesActiveOrder_WhenVehicleFinishes()
    {
        var f = CreateFixture();
        MakeVehicleAvailable(f.Registry);
        await f.Controller.RequestTransportAsync("SRC", "DST");

        var orderId = f.Mqtt.PublishedOrders.Single().OrderId;

        // Vehicle reports back: order finished (no node/edge states, not driving)
        await f.Mqtt.SimulateStateAsync(StateFor("Acme", "SN-001",
            orderId: orderId,
            nodeStates: [],
            edgeStates: []));

        Assert.Equal(0, f.Queue.ActiveCount);
    }

    [Fact]
    public async Task SimulatedState_DispatchesPendingOrder_WhenVehicleBecomesIdle()
    {
        var f = CreateFixture();

        // Enqueue order while no vehicle is available
        await f.Controller.RequestTransportAsync("SRC", "DST");
        Assert.Equal(1, f.Queue.PendingCount);

        // Vehicle comes online → becomes available → dispatch should trigger
        await f.Mqtt.SimulateStateAsync(StateFor("Acme", "SN-001"));

        Assert.Empty(f.Mqtt.PublishedOrders is { Count: > 0 }
            ? Array.Empty<string>()
            : ["no dispatch"]);  // assert dispatch happened
        Assert.Single(f.Mqtt.PublishedOrders);
    }

    [Fact]
    public async Task SimulatedState_PublishesFleetStatusUpdate_WithVehicleData()
    {
        var f = CreateFixture();

        await f.Mqtt.SimulateStateAsync(StateFor("Acme", "SN-001"));

        var status = Assert.Single(f.StatusPublisher.PublishedStatuses);
        var vehicle = Assert.Single(status.Vehicles);
        Assert.Equal("Acme/SN-001", vehicle.VehicleId);
        Assert.Equal("Idle", vehicle.Status);
    }

    // ── Event handling: connection ────────────────────────────────────────────

    [Fact]
    public async Task SimulatedConnection_RegistersVehicle()
    {
        var f = CreateFixture();

        await f.Mqtt.SimulateConnectionAsync(new ConnectionMessage
        {
            Manufacturer    = "Acme",
            SerialNumber    = "SN-001",
            ConnectionState = "ONLINE"
        });

        Assert.NotNull(f.Registry.Find("Acme/SN-001"));
    }

    [Fact]
    public async Task SimulatedConnection_SetsVehicleOffline()
    {
        var f = CreateFixture();

        await f.Mqtt.SimulateConnectionAsync(new ConnectionMessage
        {
            Manufacturer    = "Acme",
            SerialNumber    = "SN-001",
            ConnectionState = "OFFLINE"
        });

        var vehicle = f.Registry.Find("Acme/SN-001");
        Assert.Equal(VehicleStatus.Offline, vehicle?.Status);
    }

    [Fact]
    public async Task SimulatedConnection_PublishesFleetStatusUpdate()
    {
        var f = CreateFixture();

        await f.Mqtt.SimulateConnectionAsync(new ConnectionMessage
        {
            Manufacturer    = "Acme",
            SerialNumber    = "SN-001",
            ConnectionState = "ONLINE"
        });

        var status = Assert.Single(f.StatusPublisher.PublishedStatuses);
        Assert.Single(status.Vehicles);
        Assert.Equal("Acme/SN-001", status.Vehicles[0].VehicleId);
    }
}
