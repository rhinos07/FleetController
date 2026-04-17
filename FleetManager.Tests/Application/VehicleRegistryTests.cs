using Microsoft.Extensions.Logging.Abstractions;
using Vda5050FleetController.Application;
using Vda5050FleetController.Domain.Models;

namespace FleetManager.Tests.Application;

public class VehicleRegistryTests
{
    private static VehicleRegistry CreateRegistry() =>
        new(NullLogger<VehicleRegistry>.Instance);

    [Fact]
    public void GetOrCreate_ReturnsNewVehicle_WhenNotRegistered()
    {
        var registry = CreateRegistry();
        var vehicle  = registry.GetOrCreate("Acme", "SN-001");

        Assert.NotNull(vehicle);
        Assert.Equal("Acme/SN-001", vehicle.VehicleId);
    }

    [Fact]
    public void GetOrCreate_ReturnsSameInstance_ForSameManufacturerAndSerial()
    {
        var registry = CreateRegistry();
        var first    = registry.GetOrCreate("Acme", "SN-001");
        var second   = registry.GetOrCreate("Acme", "SN-001");

        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrCreate_ReturnsDifferentInstances_ForDifferentVehicles()
    {
        var registry = CreateRegistry();
        var v1       = registry.GetOrCreate("Acme", "SN-001");
        var v2       = registry.GetOrCreate("Acme", "SN-002");

        Assert.NotSame(v1, v2);
    }

    [Fact]
    public void Find_ReturnsVehicle_WhenRegistered()
    {
        var registry = CreateRegistry();
        registry.GetOrCreate("Acme", "SN-001");

        var found = registry.Find("Acme/SN-001");

        Assert.NotNull(found);
    }

    [Fact]
    public void Find_ReturnsNull_WhenVehicleNotRegistered()
    {
        var registry = CreateRegistry();

        Assert.Null(registry.Find("Acme/SN-999"));
    }

    [Fact]
    public void All_ReturnsEmpty_WhenNoVehiclesRegistered()
    {
        var registry = CreateRegistry();

        Assert.Empty(registry.All());
    }

    [Fact]
    public void All_ReturnsAllRegisteredVehicles()
    {
        var registry = CreateRegistry();
        registry.GetOrCreate("Acme", "SN-001");
        registry.GetOrCreate("Acme", "SN-002");

        Assert.Equal(2, registry.All().Count());
    }

    [Fact]
    public void FindAvailable_ReturnsNull_WhenNoVehiclesRegistered()
    {
        var registry = CreateRegistry();

        Assert.Null(registry.FindAvailable());
    }

    [Fact]
    public void FindAvailable_ReturnsNull_WhenNoVehicleIsAvailable()
    {
        var registry = CreateRegistry();
        registry.GetOrCreate("Acme", "SN-001"); // status = Unknown → not available

        Assert.Null(registry.FindAvailable());
    }

    [Fact]
    public void FindAvailable_ReturnsAvailableVehicle()
    {
        var registry = CreateRegistry();
        var vehicle  = registry.GetOrCreate("Acme", "SN-001");
        vehicle.ApplyState(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-001",
            Driving      = false,
            BatteryState = new BatteryState { BatteryCharge = 80.0 },
            Errors       = [],
            NodeStates   = [],
            EdgeStates   = []
        });

        var available = registry.FindAvailable();

        Assert.NotNull(available);
        Assert.Equal("Acme/SN-001", available.VehicleId);
    }

    [Fact]
    public void FindAvailable_SkipsUnavailableVehicles()
    {
        var registry = CreateRegistry();
        var busy     = registry.GetOrCreate("Acme", "SN-001");
        var idle     = registry.GetOrCreate("Acme", "SN-002");

        busy.ApplyState(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-001",
            Driving      = true,
            BatteryState = new BatteryState { BatteryCharge = 80.0 },
            Errors       = [],
            NodeStates   = [],
            EdgeStates   = []
        });

        idle.ApplyState(new VehicleState
        {
            Manufacturer = "Acme",
            SerialNumber = "SN-002",
            Driving      = false,
            BatteryState = new BatteryState { BatteryCharge = 80.0 },
            Errors       = [],
            NodeStates   = [],
            EdgeStates   = []
        });

        var available = registry.FindAvailable();

        Assert.Equal("Acme/SN-002", available?.VehicleId);
    }
}
