using FleetController.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Vda5050FleetController.Application;
using Vda5050FleetController.Domain.Models;
using FC = Vda5050FleetController.Application.FleetController;

namespace FleetController.Tests.Application;

/// <summary>
/// Tests for automatic low-battery dispatch to charging stations and
/// eviction of fully-charged vehicles when all charging stations are occupied.
/// </summary>
public class FleetControllerBatteryTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private record Fixture(
        FC                   Controller,
        VehicleRegistry      Registry,
        FakeMqttService      Mqtt,
        BatteryChargingSettings Settings);

    private static Fixture CreateFixture(
        string[] chargingNodeIds,
        string[] extraNodeIds,
        BatteryChargingSettings? settings = null)
    {
        var registry   = new VehicleRegistry(NullLogger<VehicleRegistry>.Instance);
        var queue      = new TransportOrderQueue(NullLogger<TransportOrderQueue>.Instance);
        var topology   = new TopologyMap();

        foreach (var id in chargingNodeIds)
            topology.AddNode(id, 0.0, 0.0, 0.0, "MAP");
        foreach (var id in extraNodeIds)
            topology.AddNode(id, 5.0, 0.0, 0.0, "MAP");

        // Connect every charging node to every extra node (bidirectional via two edges)
        foreach (var chg in chargingNodeIds)
        foreach (var extra in extraNodeIds)
        {
            topology.AddEdge($"E-{chg}-{extra}", chg, extra);
            topology.AddEdge($"E-{extra}-{chg}", extra, chg);
        }

        var mqtt       = new FakeMqttService();
        var cfg        = settings ?? new BatteryChargingSettings { LowBatteryThreshold = 30.0 };
        var dispatcher = new VehicleDispatcher(registry, queue, topology, mqtt,
            NoOpFleetPersistenceService.Instance, NullLogger<VehicleDispatcher>.Instance);
        var controller = new FC(registry, queue, topology, mqtt,
            statusPublisher: null, persistence: null, dispatcher, cfg, NullLogger<FC>.Instance);

        return new Fixture(controller, registry, mqtt, cfg);
    }

    /// <summary>Sets a vehicle as idle at <paramref name="nodeId"/> with the given battery %.</summary>
    private static Vehicle MakeVehicleIdle(VehicleRegistry registry,
        string serial, string nodeId, double battery)
    {
        var v = registry.GetOrCreate("Acme", serial);
        v.ApplyState(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = serial,
            Driving      = false,
            LastNodeId   = nodeId,
            BatteryState = new BatteryState { BatteryCharge = battery, Charging = false },
            Errors       = [],
            NodeStates   = [],
            EdgeStates   = []
        });
        return v;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LowBatteryVehicle_IsDispatchedToFreeChargingStation()
    {
        var f = CreateFixture(["CHG-1"], ["STATION-A"]);
        MakeVehicleIdle(f.Registry, "SN-001", "STATION-A", battery: 15.0);

        // Simulate a state update to trigger the battery dispatch
        await f.Mqtt.SimulateStateAsync(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-001",
            Driving      = false,
            LastNodeId   = "STATION-A",
            BatteryState = new BatteryState { BatteryCharge = 15.0 },
            Errors       = [],
            NodeStates   = [],
            EdgeStates   = []
        });

        // Expect a DODGE order directing the vehicle to CHG-1
        var chargeOrder = f.Mqtt.PublishedOrders
            .FirstOrDefault(o => o.SerialNumber == "SN-001"
                                 && o.Nodes.Last().NodeId == "CHG-1");
        Assert.NotNull(chargeOrder);
    }

    [Fact]
    public async Task VehicleAtChargingNode_IsNotRedispatched()
    {
        var f = CreateFixture(["CHG-1"], ["STATION-A"]);
        // Vehicle is ALREADY at the charging station
        MakeVehicleIdle(f.Registry, "SN-001", "CHG-1", battery: 15.0);

        await f.Mqtt.SimulateStateAsync(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-001",
            Driving      = false,
            LastNodeId   = "CHG-1",
            BatteryState = new BatteryState { BatteryCharge = 15.0 },
            Errors       = [],
            NodeStates   = [],
            EdgeStates   = []
        });

        // No order should be published (vehicle already at charging node)
        Assert.Empty(f.Mqtt.PublishedOrders);
    }

    [Fact]
    public async Task FullBatteryVehicle_IsNotDispatchedToCharge()
    {
        var f = CreateFixture(["CHG-1"], ["STATION-A"]);
        MakeVehicleIdle(f.Registry, "SN-001", "STATION-A", battery: 85.0);

        await f.Mqtt.SimulateStateAsync(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-001",
            Driving      = false,
            LastNodeId   = "STATION-A",
            BatteryState = new BatteryState { BatteryCharge = 85.0 },
            Errors       = [],
            NodeStates   = [],
            EdgeStates   = []
        });

        // Battery is well above 30% threshold — no charge dispatch
        Assert.Empty(f.Mqtt.PublishedOrders);
    }

    [Fact]
    public async Task FullyChargedVehicle_IsEvicted_WhenLowBatteryVehicleNeedsSpot()
    {
        // CHG-1 is the only charging station, occupied by a fully-charged SN-001.
        // SN-002 has low battery and needs to charge.
        var f = CreateFixture(["CHG-1"], ["STATION-A", "STATION-B"]);
        MakeVehicleIdle(f.Registry, "SN-001", "CHG-1",     battery: 95.0);
        MakeVehicleIdle(f.Registry, "SN-002", "STATION-A", battery: 10.0);

        await f.Mqtt.SimulateStateAsync(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-002",
            Driving      = false,
            LastNodeId   = "STATION-A",
            BatteryState = new BatteryState { BatteryCharge = 10.0 },
            Errors       = [],
            NodeStates   = [],
            EdgeStates   = []
        });

        // SN-001 should be moved away from CHG-1
        var evictOrder = f.Mqtt.PublishedOrders
            .FirstOrDefault(o => o.SerialNumber == "SN-001"
                                 && !o.Nodes.Last().NodeId.StartsWith("CHG-"));
        Assert.NotNull(evictOrder);

        // SN-002 should be sent to CHG-1
        var chargeOrder = f.Mqtt.PublishedOrders
            .FirstOrDefault(o => o.SerialNumber == "SN-002"
                                 && o.Nodes.Last().NodeId == "CHG-1");
        Assert.NotNull(chargeOrder);
    }

    [Fact]
    public async Task GetStatus_IncludesLowBatteryThreshold()
    {
        var settings = new BatteryChargingSettings { LowBatteryThreshold = 42.0 };
        var f = CreateFixture(["CHG-1"], ["STATION-A"], settings);

        var status = f.Controller.GetStatus();

        Assert.Equal(42.0, status.LowBatteryThreshold);
    }

    [Fact]
    public void UpdateBatteryThreshold_UpdatesSetting()
    {
        var settings = new BatteryChargingSettings { LowBatteryThreshold = 30.0 };
        var f = CreateFixture(["CHG-1"], ["STATION-A"], settings);

        f.Controller.UpdateBatteryThreshold(50.0);

        Assert.Equal(50.0, settings.LowBatteryThreshold);
    }

    [Fact]
    public void UpdateBatteryThreshold_ThrowsOnInvalidValue()
    {
        var f = CreateFixture(["CHG-1"], ["STATION-A"]);

        Assert.Throws<ArgumentOutOfRangeException>(() => f.Controller.UpdateBatteryThreshold(-1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => f.Controller.UpdateBatteryThreshold(101.0));
    }

    [Fact]
    public async Task DetermineStatus_ReturnsCharging_WhenBatteryStateChargingIsTrue()
    {
        var f = CreateFixture(["CHG-1"], ["STATION-A"]);

        // Send a state with BatteryState.Charging = true
        await f.Mqtt.SimulateStateAsync(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-001",
            Driving      = false,
            LastNodeId   = "CHG-1",
            BatteryState = new BatteryState { BatteryCharge = 55.0, Charging = true },
            Errors       = [],
            NodeStates   = [],
            EdgeStates   = []
        });

        var vehicle = f.Registry.Find("Acme/SN-001");
        Assert.NotNull(vehicle);
        Assert.Equal(VehicleStatus.Charging, vehicle.Status);
    }
}
