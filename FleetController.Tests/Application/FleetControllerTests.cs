using FleetController.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Vda5050FleetController.Application;
using Vda5050FleetController.Domain.Models;
using FC = Vda5050FleetController.Application.FleetController;

namespace FleetController.Tests.Application;

public class FleetControllerTests
{
    // ── Test fixture helpers ──────────────────────────────────────────────────

    private record Fixture(
        FC    Controller,
        VehicleRegistry    Registry,
        TransportOrderQueue Queue,
        FakeMqttService    Mqtt,
        FakeFleetStatusPublisher StatusPublisher);

    private static Fixture CreateFixture()
    {
        var registry        = new VehicleRegistry(NullLogger<VehicleRegistry>.Instance);
        var queue           = new TransportOrderQueue(NullLogger<TransportOrderQueue>.Instance);
        var topology        = new TopologyMap();
        topology.AddNode("SRC", 0.0,  0.0, 0.0, "MAP-1");
        topology.AddNode("DST", 10.0, 0.0, 0.0, "MAP-1");
        var mqtt            = new FakeMqttService();
        var statusPublisher = new FakeFleetStatusPublisher();
        var dispatcher      = new VehicleDispatcher(registry, queue, topology, mqtt,
            NoOpFleetPersistenceService.Instance, NullLogger<VehicleDispatcher>.Instance);
        var controller      = new FC(
            registry, queue, topology, mqtt,
            statusPublisher,
            persistence: null,
            dispatcher,
            NullLogger<FC>.Instance);

        return new Fixture(controller, registry, queue, mqtt, statusPublisher);
    }

    private static void MakeVehicleAvailable(VehicleRegistry registry,
        string manufacturer = "Acme", string serial = "SN-001",
        AgvPosition? position = null)
    {
        var vehicle = registry.GetOrCreate(manufacturer, serial);
        vehicle.ApplyState(new VehicleState
        {
            Manufacturer = manufacturer,
            SerialNumber = serial,
            Driving      = false,
            AgvPosition  = position,
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
    public async Task RequestTransportAsync_PrefersVehicleAtSourceNode()
    {
        var f = CreateFixture();
        MakeVehicleAvailable(f.Registry, "Acme", "SN-001", new AgvPosition
        {
            X = 10.0, Y = 0.0, Theta = 0.0, MapId = "MAP-1"
        });
        MakeVehicleAvailable(f.Registry, "Acme", "SN-002", new AgvPosition
        {
            X = 0.0, Y = 0.0, Theta = 0.0, MapId = "MAP-1"
        });

        await f.Controller.RequestTransportAsync("SRC", "DST");

        var published = Assert.Single(f.Mqtt.PublishedOrders);
        Assert.Equal("Acme", published.Manufacturer);
        Assert.Equal("SN-002", published.SerialNumber);
    }

    [Fact]
    public async Task RequestTransportAsync_PrefersNearestVehicleToSource_WhenNoneAtSource()
    {
        var f = CreateFixture();
        MakeVehicleAvailable(f.Registry, "Acme", "SN-001", new AgvPosition
        {
            X = 8.0, Y = 0.0, Theta = 0.0, MapId = "MAP-1"
        });
        MakeVehicleAvailable(f.Registry, "Acme", "SN-002", new AgvPosition
        {
            X = 2.0, Y = 0.0, Theta = 0.0, MapId = "MAP-1"
        });

        await f.Controller.RequestTransportAsync("SRC", "DST");

        var published = Assert.Single(f.Mqtt.PublishedOrders);
        Assert.Equal("Acme", published.Manufacturer);
        Assert.Equal("SN-002", published.SerialNumber);
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
    public async Task SimulatedState_VehicleBecomesIdle_WhenSubsequentStateStillCarriesCompletedOrderId()
    {
        // In VDA5050 AGVs keep reporting their last orderId forever; the fleet controller must
        // strip it once the order is no longer in the active queue so the vehicle can become Idle.
        var f = CreateFixture();
        MakeVehicleAvailable(f.Registry);
        await f.Controller.RequestTransportAsync("SRC", "DST");

        var orderId = f.Mqtt.PublishedOrders.Single().OrderId;

        // Completion state: nodeStates empty, not driving → order marked complete
        await f.Mqtt.SimulateStateAsync(StateFor("Acme", "SN-001",
            orderId: orderId,
            nodeStates: [],
            edgeStates: []));

        // Next state from AGV still carries the old orderId (normal VDA5050 behaviour)
        await f.Mqtt.SimulateStateAsync(StateFor("Acme", "SN-001",
            orderId: orderId,
            nodeStates: [],
            edgeStates: []));

        var vehicle = f.Registry.Find("Acme/SN-001");
        Assert.Equal("Idle", vehicle!.Status.ToString());
    }

    [Fact]
    public async Task SimulatedState_DispatchesPendingOrder_WhenVehicleFinishesAndReportsStaleOrderId()
    {
        // After order completion the vehicle should pick up a queued order on the next state update,
        // even though the AGV still reports the completed orderId.
        var f = CreateFixture();
        MakeVehicleAvailable(f.Registry);

        // First transport order → dispatched immediately
        await f.Controller.RequestTransportAsync("SRC", "DST");
        var firstOrderId = f.Mqtt.PublishedOrders.Single().OrderId;

        // AGV confirms it is now driving → vehicle becomes non-available
        await f.Mqtt.SimulateStateAsync(StateFor("Acme", "SN-001",
            orderId: firstOrderId, driving: true));

        // Second order queued while vehicle is busy
        await f.Controller.RequestTransportAsync("SRC", "DST");
        Assert.Equal(1, f.Queue.PendingCount);

        // Completion state: nodeStates empty, not driving → first order marked complete
        await f.Mqtt.SimulateStateAsync(StateFor("Acme", "SN-001",
            orderId: firstOrderId,
            nodeStates: [],
            edgeStates: []));

        // Next state still carries the stale orderId (normal VDA5050 behaviour)
        // → fleet controller strips it → vehicle becomes Idle → second order dispatched
        await f.Mqtt.SimulateStateAsync(StateFor("Acme", "SN-001",
            orderId: firstOrderId,
            nodeStates: [],
            edgeStates: []));

        Assert.Equal(2, f.Mqtt.PublishedOrders.Count); // first dispatch + second dispatch
        Assert.Equal(0, f.Queue.PendingCount);
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

    // ── Blocker resolution ────────────────────────────────────────────────────

    /// <summary>
    /// Creates a four-node topology:
    ///   SRC –E-SRC-MID– MID –E-MID-DST– DST
    ///                    |
    ///                 E-MID-SIDE
    ///                    |
    ///                   SIDE
    /// SIDE gives GetNeighborNodeIds a free escape route from MID so that tests covering
    /// blockers at MID have a valid non-conflicting dodge target.
    /// </summary>
    private static Fixture CreateFixtureWithEdges()
    {
        var registry        = new VehicleRegistry(NullLogger<VehicleRegistry>.Instance);
        var queue           = new TransportOrderQueue(NullLogger<TransportOrderQueue>.Instance);
        var topology        = new TopologyMap();
        topology.AddNode("SRC",  0.0,  0.0, 0.0, "MAP-1");
        topology.AddNode("MID",  5.0,  0.0, 0.0, "MAP-1");
        topology.AddNode("DST",  10.0, 0.0, 0.0, "MAP-1");
        topology.AddNode("SIDE", 5.0,  5.0, 0.0, "MAP-1");
        topology.AddEdge("E-SRC-MID",  "SRC", "MID");
        topology.AddEdge("E-MID-DST",  "MID", "DST");
        topology.AddEdge("E-MID-SIDE", "MID", "SIDE");
        var mqtt            = new FakeMqttService();
        var statusPublisher = new FakeFleetStatusPublisher();
        var dispatcher      = new VehicleDispatcher(registry, queue, topology, mqtt,
            NoOpFleetPersistenceService.Instance, NullLogger<VehicleDispatcher>.Instance);
        var controller      = new FC(
            registry, queue, topology, mqtt,
            statusPublisher,
            persistence: null,
            dispatcher,
            NullLogger<FC>.Instance);
        return new Fixture(controller, registry, queue, mqtt, statusPublisher);
    }

    private static void MakeVehicleIdleAtNode(VehicleRegistry registry,
        string manufacturer, string serial, string lastNodeId, AgvPosition? position = null)
    {
        var vehicle = registry.GetOrCreate(manufacturer, serial);
        vehicle.ApplyState(new VehicleState
        {
            Manufacturer = manufacturer,
            SerialNumber = serial,
            LastNodeId   = lastNodeId,
            Driving      = false,
            AgvPosition  = position,
            BatteryState = new BatteryState { BatteryCharge = 80.0 },
            Errors       = [],
            NodeStates   = [],
            EdgeStates   = []
        });
    }

    [Fact]
    public async Task DispatchOrder_SendsDodgeOrder_WhenIdleVehicleBlocksSourceNode()
    {
        var f = CreateFixtureWithEdges();

        // Vehicle A (the dispatcher) is available with no specific position.
        MakeVehicleIdleAtNode(f.Registry, "Acme", "SN-001", "DST");
        // Vehicle B is idle at SRC — the source node of the upcoming order.
        MakeVehicleIdleAtNode(f.Registry, "Acme", "SN-002", "SRC");

        await f.Controller.RequestTransportAsync("SRC", "DST");

        // A dodge order should have been sent to vehicle B (serial SN-002).
        var dodgeOrder = f.Mqtt.PublishedOrders.FirstOrDefault(o => o.OrderId.StartsWith("DODGE-"));
        Assert.NotNull(dodgeOrder);
        Assert.Equal("SN-002", dodgeOrder.SerialNumber);

        // The transport order should also have been dispatched.
        var transportOrder = f.Mqtt.PublishedOrders.FirstOrDefault(o => !o.OrderId.StartsWith("DODGE-"));
        Assert.NotNull(transportOrder);
    }

    [Fact]
    public async Task DispatchOrder_DodgeOrderTargetIsFreeNeighbour()
    {
        var f = CreateFixtureWithEdges();

        MakeVehicleIdleAtNode(f.Registry, "Acme", "SN-001", "DST");
        MakeVehicleIdleAtNode(f.Registry, "Acme", "SN-002", "SRC");

        await f.Controller.RequestTransportAsync("SRC", "DST");

        var dodgeOrder = f.Mqtt.PublishedOrders.Single(o => o.OrderId.StartsWith("DODGE-"));
        // Blocker was at SRC; MID is the only free neighbour of SRC.
        Assert.Contains(dodgeOrder.Nodes, n => n.NodeId == "MID");
    }

    [Fact]
    public async Task DispatchOrder_NoDodgeOrder_WhenNoVehicleBlocksPath()
    {
        var f = CreateFixtureWithEdges();

        // Only the dispatched vehicle; it is not blocking any path node.
        MakeVehicleIdleAtNode(f.Registry, "Acme", "SN-001", "DST");

        await f.Controller.RequestTransportAsync("SRC", "DST");

        Assert.DoesNotContain(f.Mqtt.PublishedOrders, o => o.OrderId.StartsWith("DODGE-"));
    }

    [Fact]
    public async Task DispatchOrder_DodgeOrderHasNoPickOrDropActions()
    {
        var f = CreateFixtureWithEdges();

        MakeVehicleIdleAtNode(f.Registry, "Acme", "SN-001", "DST");
        MakeVehicleIdleAtNode(f.Registry, "Acme", "SN-002", "SRC");

        await f.Controller.RequestTransportAsync("SRC", "DST");

        var dodgeOrder = f.Mqtt.PublishedOrders.Single(o => o.OrderId.StartsWith("DODGE-"));
        Assert.All(dodgeOrder.Nodes, n => Assert.Empty(n.Actions));
    }

    [Fact]
    public async Task DispatchOrder_DodgeSentBeforeTransportOrder()
    {
        var f = CreateFixtureWithEdges();

        MakeVehicleIdleAtNode(f.Registry, "Acme", "SN-001", "DST");
        MakeVehicleIdleAtNode(f.Registry, "Acme", "SN-002", "SRC");

        await f.Controller.RequestTransportAsync("SRC", "DST");

        // The DODGE order must appear before the transport order in the publish list.
        var dodgeIndex     = f.Mqtt.PublishedOrders.FindIndex(o => o.OrderId.StartsWith("DODGE-"));
        var transportIndex = f.Mqtt.PublishedOrders.FindIndex(o => !o.OrderId.StartsWith("DODGE-"));
        Assert.True(dodgeIndex < transportIndex,
            "Dodge order should be published before the transport order");
    }

    [Fact]
    public async Task Vehicle_TracksLastNodeId_AfterApplyState()
    {
        var f       = CreateFixtureWithEdges();
        var vehicle = f.Registry.GetOrCreate("Acme", "SN-001");

        vehicle.ApplyState(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-001",
            LastNodeId   = "SRC",
            Driving      = false,
            BatteryState = new BatteryState { BatteryCharge = 80.0 },
            Errors       = [],
            NodeStates   = [],
            EdgeStates   = []
        });

        Assert.Equal("SRC", vehicle.LastNodeId);
    }

    [Fact]
    public async Task DispatchOrder_DodgeTargetIsNotSourceNode_WhenSourceIsNeighbourOfBlockedDest()
    {
        // Regression: if SRC is a direct neighbour of DST and a blocker stands at DST,
        // the dodge must NOT target SRC — the assigned vehicle holds SRC during pick,
        // causing a simulator deadlock (each AGV waits for the node the other holds).
        //
        // Topology:  SRC ─── DST ─── SIDE
        //                        ↑
        //                    blocker here
        var registry = new VehicleRegistry(NullLogger<VehicleRegistry>.Instance);
        var queue    = new TransportOrderQueue(NullLogger<TransportOrderQueue>.Instance);
        var topology = new TopologyMap();
        topology.AddNode("SRC",  0.0, 0.0, 0.0, "MAP-1");
        topology.AddNode("DST", 10.0, 0.0, 0.0, "MAP-1");
        topology.AddNode("SIDE", 10.0, 5.0, 0.0, "MAP-1");
        topology.AddEdge("E-SRC-DST",  "SRC",  "DST");
        topology.AddEdge("E-DST-SIDE", "DST",  "SIDE");
        var mqtt       = new FakeMqttService();
        var dispatcher = new VehicleDispatcher(registry, queue, topology, mqtt,
            NoOpFleetPersistenceService.Instance, NullLogger<VehicleDispatcher>.Instance);
        var f          = new FC(registry, queue, topology, mqtt,
            statusPublisher: null, persistence: null, dispatcher, NullLogger<FC>.Instance);

        // SN-001 (dispatcher) starts at SRC; SN-002 (blocker) is at DST
        MakeVehicleIdleAtNode(registry, "Acme", "SN-001", "SRC");
        MakeVehicleIdleAtNode(registry, "Acme", "SN-002", "DST");

        await f.RequestTransportAsync("SRC", "DST");

        var dodge = mqtt.PublishedOrders.FirstOrDefault(o => o.OrderId.StartsWith("DODGE-"));
        Assert.NotNull(dodge);
        Assert.Equal("SN-002", dodge.SerialNumber);
        // Dodge target must be SIDE, not SRC (SRC is held by the dispatched vehicle)
        Assert.DoesNotContain(dodge.Nodes, n => n.NodeId == "SRC");
        Assert.Contains(dodge.Nodes, n => n.NodeId == "SIDE");
    }

    [Fact]
    public async Task DispatchOrder_SendsDodgeOrder_WhenBlockingVehicleIsStillBusyFromCompletedOrder()
    {
        // Regression: blocker detection must catch vehicles in Busy status whose order just
        // completed (order no longer in active queue) — not only fully-Idle vehicles.
        var f = CreateFixtureWithEdges();

        // SN-001 is the dispatcher; SN-002 just finished an order at SRC (still Busy status)
        MakeVehicleIdleAtNode(f.Registry, "Acme", "SN-001", "DST");

        // Simulate SN-002 completing an order: it arrives at SRC with a completed orderId
        // The fleet controller marks the order done but SN-002's status remains Busy
        // until the next heartbeat clears the orderId.
        var completedOrderId = "TO-completed-earlier";
        var vehicle2 = f.Registry.GetOrCreate("Acme", "SN-002");
        vehicle2.ApplyState(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-002",
            OrderId      = completedOrderId,   // stale orderId — not in active queue
            LastNodeId   = "SRC",
            Driving      = false,
            BatteryState = new BatteryState { BatteryCharge = 80.0 },
            Errors       = [],
            NodeStates   = [],
            EdgeStates   = []
        });
        // vehicle2.Status is now Busy (orderId set), but the order is NOT in the queue

        // Dispatch a new order for SN-001: SRC → DST; SN-002 is blocking SRC
        await f.Controller.RequestTransportAsync("SRC", "DST");

        var dodge = f.Mqtt.PublishedOrders.FirstOrDefault(o => o.OrderId.StartsWith("DODGE-"));
        Assert.NotNull(dodge);
        Assert.Equal("SN-002", dodge.SerialNumber);
    }

    // ── Dynamic blocker resolution (mid-path) ─────────────────────────────────

    [Fact]
    public async Task MidPathState_SendsDodgeOrder_WhenIdleVehicleBlocksRemainingNode()
    {
        var f = CreateFixtureWithEdges();

        // SN-001 has an active order; simulate it stopped at SRC with MID still remaining
        MakeVehicleIdleAtNode(f.Registry, "Acme", "SN-001", "DST");
        await f.Controller.RequestTransportAsync("SRC", "DST");
        f.Mqtt.PublishedOrders.Clear(); // clear dispatch orders; only care about dodge below

        // SN-002 parks at MID after dispatch
        MakeVehicleIdleAtNode(f.Registry, "Acme", "SN-002", "MID");

        // SN-001 stops at SRC with MID and DST still in its nodeStates (not driving → likely blocked)
        await f.Mqtt.SimulateStateAsync(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-001",
            OrderId      = f.Mqtt.PublishedOrders.FirstOrDefault()?.OrderId ?? "TO-test",
            LastNodeId   = "SRC",
            Driving      = false,
            BatteryState = new BatteryState { BatteryCharge = 80.0 },
            Errors       = [],
            NodeStates   = [new NodeState { NodeId = "MID" }, new NodeState { NodeId = "DST" }],
            EdgeStates   = []
        });

        var dodge = f.Mqtt.PublishedOrders.FirstOrDefault(o => o.OrderId.StartsWith("DODGE-"));
        Assert.NotNull(dodge);
        Assert.Equal("SN-002", dodge.SerialNumber);
    }

    [Fact]
    public async Task MidPathState_NoDodgeOrder_WhenVehicleIsStillDriving()
    {
        var f = CreateFixtureWithEdges();

        MakeVehicleIdleAtNode(f.Registry, "Acme", "SN-001", "DST");
        await f.Controller.RequestTransportAsync("SRC", "DST");
        f.Mqtt.PublishedOrders.Clear();

        MakeVehicleIdleAtNode(f.Registry, "Acme", "SN-002", "MID");

        // driving=true → vehicle is moving, not blocked; no dodge expected
        await f.Mqtt.SimulateStateAsync(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-001",
            OrderId      = "TO-test",
            LastNodeId   = "SRC",
            Driving      = true,
            BatteryState = new BatteryState { BatteryCharge = 80.0 },
            Errors       = [],
            NodeStates   = [new NodeState { NodeId = "MID" }, new NodeState { NodeId = "DST" }],
            EdgeStates   = []
        });

        Assert.DoesNotContain(f.Mqtt.PublishedOrders, o => o.OrderId.StartsWith("DODGE-"));
    }

    [Fact]
    public async Task MidPathState_NoDodgeOrder_WhenNoRemainingNodeStates()
    {
        var f = CreateFixtureWithEdges();

        MakeVehicleIdleAtNode(f.Registry, "Acme", "SN-001", "DST");
        MakeVehicleIdleAtNode(f.Registry, "Acme", "SN-002", "MID");
        await f.Controller.RequestTransportAsync("SRC", "DST");
        f.Mqtt.PublishedOrders.Clear();

        // Vehicle reached its last node (nodeStates empty) → not blocked mid-path
        await f.Mqtt.SimulateStateAsync(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-001",
            OrderId      = "TO-test",
            LastNodeId   = "DST",
            Driving      = false,
            BatteryState = new BatteryState { BatteryCharge = 80.0 },
            Errors       = [],
            NodeStates   = [],
            EdgeStates   = []
        });

        Assert.DoesNotContain(f.Mqtt.PublishedOrders, o => o.OrderId.StartsWith("DODGE-"));
    }

    // ── Proactive dodge: blocker becomes idle while active vehicle is stopped ────

    [Fact]
    public async Task ProactiveDodge_SendsDodgeToBlocker_WhenItBecomesIdleAfterActiveVehicleWasStopped()
    {
        // Core regression scenario:
        // 1. SN-001 arrives at DST and finishes its order (parked idle at DST).
        // 2. SN-002 has an active order whose path includes DST; it stops mid-path and
        //    reports remaining node states = [DST] (driving=false, nodeStates=[DST]).
        // 3. At that moment SN-001 is still Driving — not yet a blocker at dispatch time.
        // 4. Later SN-001 completes its order and becomes Idle at DST.
        // 5. Expected: the fleet controller proactively sends a dodge order to SN-001 so
        //    that SN-002 can continue to DST.

        // Topology: SRC --- DST --- SIDE  (SIDE is the only free dodge target)
        var registry = new VehicleRegistry(NullLogger<VehicleRegistry>.Instance);
        var queue    = new TransportOrderQueue(NullLogger<TransportOrderQueue>.Instance);
        var topology = new TopologyMap();
        topology.AddNode("SRC",  0.0,  0.0, 0.0, "MAP-1");
        topology.AddNode("DST",  10.0, 0.0, 0.0, "MAP-1");
        topology.AddNode("SIDE", 10.0, 5.0, 0.0, "MAP-1");
        topology.AddEdge("E-SRC-DST",  "SRC", "DST");
        topology.AddEdge("E-DST-SIDE", "DST", "SIDE");
        var mqtt       = new FakeMqttService();
        var dispatcher = new VehicleDispatcher(registry, queue, topology, mqtt,
            NoOpFleetPersistenceService.Instance, NullLogger<VehicleDispatcher>.Instance);
        var controller = new FC(registry, queue, topology, mqtt,
            statusPublisher: null, persistence: null, dispatcher, NullLogger<FC>.Instance);

        // SN-001 is driving — not yet at DST so it is NOT detected as a stationary blocker
        // at dispatch time.
        var vehicle1 = registry.GetOrCreate("Acme", "SN-001");
        vehicle1.ApplyState(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-001",
            OrderId      = "TO-agv1-mission",
            LastNodeId   = "SRC",
            Driving      = true,
            BatteryState = new BatteryState { BatteryCharge = 80.0 },
            Errors       = [],
            NodeStates   = [new NodeState { NodeId = "DST" }],
            EdgeStates   = []
        });

        // SN-002 is idle at SRC and gets dispatched to DST.
        MakeVehicleIdleAtNode(registry, "Acme", "SN-002", "SRC");
        await controller.RequestTransportAsync("SRC", "DST");

        // No dodge at dispatch time: SN-001 is still Driving, not a stationary blocker.
        Assert.DoesNotContain(mqtt.PublishedOrders, o => o.OrderId.StartsWith("DODGE-"));

        // Capture the transport order ID assigned to SN-002.
        var transportOrderId = mqtt.PublishedOrders.Single(o => !o.OrderId.StartsWith("DODGE-")).OrderId;
        mqtt.PublishedOrders.Clear();

        // SN-002 stops mid-path waiting for DST (the simulator publishes this blocked state).
        await mqtt.SimulateStateAsync(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-002",
            OrderId      = transportOrderId,
            LastNodeId   = "SRC",
            Driving      = false,
            BatteryState = new BatteryState { BatteryCharge = 80.0 },
            Errors       = [],
            NodeStates   = [new NodeState { NodeId = "DST" }],
            EdgeStates   = []
        });

        // SN-001 is still Driving — no dodge can be sent yet.
        Assert.DoesNotContain(mqtt.PublishedOrders, o => o.OrderId.StartsWith("DODGE-"));

        // SN-001 now arrives at DST and completes its order (becomes Idle at DST).
        await mqtt.SimulateStateAsync(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-001",
            OrderId      = "TO-agv1-mission",
            LastNodeId   = "DST",
            Driving      = false,
            BatteryState = new BatteryState { BatteryCharge = 80.0 },
            Errors       = [],
            NodeStates   = [],
            EdgeStates   = []
        });

        // The fleet controller must proactively send a dodge to SN-001 (now idle at DST)
        // because SN-002 had previously reported DST in its remaining nodes.
        var dodge = mqtt.PublishedOrders.FirstOrDefault(o => o.OrderId.StartsWith("DODGE-"));
        Assert.NotNull(dodge);
        Assert.Equal("SN-001", dodge.SerialNumber);
        // SN-001 should move to SIDE (the only free neighbour of DST).
        Assert.Contains(dodge.Nodes, n => n.NodeId == "SIDE");
    }

    [Fact]
    public async Task ProactiveDodge_NoDodge_WhenNoVehicleHasRemainingNodeAtBlockerPosition()
    {
        // If no active vehicle has a remaining-node matching the newly-idle vehicle's position,
        // no proactive dodge should be issued.
        var f = CreateFixtureWithEdges();

        MakeVehicleIdleAtNode(f.Registry, "Acme", "SN-001", "DST");
        await f.Controller.RequestTransportAsync("SRC", "DST");
        f.Mqtt.PublishedOrders.Clear();

        // SN-001 completes the order at DST; no other vehicle is blocked waiting for DST.
        var orderId = f.Mqtt.PublishedOrders.FirstOrDefault()?.OrderId ?? "TO-test";
        await f.Mqtt.SimulateStateAsync(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-001",
            OrderId      = orderId,
            LastNodeId   = "DST",
            Driving      = false,
            BatteryState = new BatteryState { BatteryCharge = 80.0 },
            Errors       = [],
            NodeStates   = [],
            EdgeStates   = []
        });

        Assert.DoesNotContain(f.Mqtt.PublishedOrders, o => o.OrderId.StartsWith("DODGE-"));
    }

    [Fact]
    public void Vehicle_TracksRemainingNodeIds_WhenStoppedMidPath()
    {
        var vehicle = new Vehicle("Acme", "SN-001");

        vehicle.ApplyState(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-001",
            OrderId      = "TO-test",
            LastNodeId   = "SRC",
            Driving      = false,
            BatteryState = new BatteryState { BatteryCharge = 80.0 },
            Errors       = [],
            NodeStates   = [new NodeState { NodeId = "MID" }, new NodeState { NodeId = "DST" }],
            EdgeStates   = []
        });

        Assert.NotNull(vehicle.RemainingNodeIds);
        Assert.Contains("MID", vehicle.RemainingNodeIds);
        Assert.Contains("DST", vehicle.RemainingNodeIds);
    }

    [Fact]
    public void Vehicle_ClearsRemainingNodeIds_WhenDriving()
    {
        var vehicle = new Vehicle("Acme", "SN-001");

        // First: stopped mid-path
        vehicle.ApplyState(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-001",
            OrderId      = "TO-test",
            LastNodeId   = "SRC",
            Driving      = false,
            BatteryState = new BatteryState { BatteryCharge = 80.0 },
            Errors       = [],
            NodeStates   = [new NodeState { NodeId = "DST" }],
            EdgeStates   = []
        });
        Assert.NotNull(vehicle.RemainingNodeIds);

        // Then: vehicle starts driving again (dodge was effective)
        vehicle.ApplyState(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-001",
            OrderId      = "TO-test",
            LastNodeId   = "SRC",
            Driving      = true,
            BatteryState = new BatteryState { BatteryCharge = 80.0 },
            Errors       = [],
            NodeStates   = [new NodeState { NodeId = "DST" }],
            EdgeStates   = []
        });
        Assert.Null(vehicle.RemainingNodeIds);
    }

    // ── CancelOrderAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task CancelOrderAsync_ReturnsFalse_WhenOrderNotPending()
    {
        var f = CreateFixture();

        var result = await f.Controller.CancelOrderAsync("DOES-NOT-EXIST");

        Assert.False(result);
    }

    [Fact]
    public async Task CancelOrderAsync_ReturnsTrue_AndRemovesFromQueue()
    {
        var f = CreateFixture();
        await f.Controller.RequestTransportAsync("SRC", "DST");
        var orderId = f.Controller.GetStatus().Orders.Single().OrderId;

        var result = await f.Controller.CancelOrderAsync(orderId);

        Assert.True(result);
        Assert.Equal(0, f.Queue.PendingCount);
    }

    [Fact]
    public async Task CancelOrderAsync_PublishesStatusUpdate()
    {
        var f = CreateFixture();
        await f.Controller.RequestTransportAsync("SRC", "DST");
        var orderId = f.Controller.GetStatus().Orders.Single().OrderId;
        f.StatusPublisher.PublishedStatuses.Clear();

        await f.Controller.CancelOrderAsync(orderId);

        Assert.Single(f.StatusPublisher.PublishedStatuses);
    }

    [Fact]
    public async Task CancelOrderAsync_ReturnsFalse_ForActiveOrder()
    {
        var f = CreateFixture();
        MakeVehicleAvailable(f.Registry);
        await f.Controller.RequestTransportAsync("SRC", "DST");
        var orderId = f.Controller.GetStatus().Orders.Single().OrderId;

        // Order was dispatched, so it is now active (not pending)
        var result = await f.Controller.CancelOrderAsync(orderId);

        Assert.False(result);
        Assert.Equal(1, f.Queue.ActiveCount);
    }

    // ── UpdateOrderAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateOrderAsync_ReturnsFalse_WhenOrderNotPending()
    {
        var f = CreateFixture();

        var result = await f.Controller.UpdateOrderAsync("DOES-NOT-EXIST", "SRC", "DST", null);

        Assert.False(result);
    }

    [Fact]
    public async Task UpdateOrderAsync_ReturnsTrue_AndUpdatesOrder()
    {
        var f = CreateFixture();
        await f.Controller.RequestTransportAsync("SRC", "DST");
        var orderId = f.Controller.GetStatus().Orders.Single().OrderId;

        var result = await f.Controller.UpdateOrderAsync(orderId, "NEW-SRC", "NEW-DST", "PAL-01");

        Assert.True(result);
        var updated = f.Controller.GetStatus().Orders.Single();
        Assert.Equal("NEW-SRC", updated.SourceId);
        Assert.Equal("NEW-DST", updated.DestId);
        Assert.Equal("PAL-01",  updated.LoadId);
    }

    [Fact]
    public async Task UpdateOrderAsync_PublishesStatusUpdate()
    {
        var f = CreateFixture();
        await f.Controller.RequestTransportAsync("SRC", "DST");
        var orderId = f.Controller.GetStatus().Orders.Single().OrderId;
        f.StatusPublisher.PublishedStatuses.Clear();

        await f.Controller.UpdateOrderAsync(orderId, "SRC", "DST", null);

        Assert.Single(f.StatusPublisher.PublishedStatuses);
    }
}
