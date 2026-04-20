using Vda5050FleetController.Domain.Models;

namespace FleetManager.Tests.Domain;

public class VehicleTests
{
    // ── Construction ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_SetsVehicleIdFromManufacturerAndSerial()
    {
        var vehicle = new Vehicle("Acme", "SN-001");

        Assert.Equal("Acme/SN-001", vehicle.VehicleId);
        Assert.Equal("Acme",        vehicle.Manufacturer);
        Assert.Equal("SN-001",      vehicle.SerialNumber);
    }

    [Fact]
    public void Constructor_InitialStatusIsUnknown()
    {
        var vehicle = new Vehicle("Acme", "SN-001");

        Assert.Equal(VehicleStatus.Unknown, vehicle.Status);
    }

    // ── IsAvailable ───────────────────────────────────────────────────────────

    [Fact]
    public void IsAvailable_FalseByDefault_WhenNoBatteryAndUnknownStatus()
    {
        var vehicle = new Vehicle("Acme", "SN-001");

        Assert.False(vehicle.IsAvailable);
    }

    [Fact]
    public void IsAvailable_TrueWhenIdleWithSufficientBattery()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState());

        Assert.True(vehicle.IsAvailable);
    }

    [Fact]
    public void IsAvailable_FalseWhenDriving()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState() with { Driving = true });

        Assert.False(vehicle.IsAvailable);
    }

    [Fact]
    public void IsAvailable_FalseWhenBatteryExactlyAtThreshold()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState() with
        {
            BatteryState = new BatteryState { BatteryCharge = 20.0 }
        });

        Assert.False(vehicle.IsAvailable);
    }

    [Fact]
    public void IsAvailable_FalseWhenBatteryBelowThreshold()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState() with
        {
            BatteryState = new BatteryState { BatteryCharge = 10.0 }
        });

        Assert.False(vehicle.IsAvailable);
    }

    [Fact]
    public void IsAvailable_FalseWhenBatteryIsNull()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState() with { BatteryState = null });

        Assert.False(vehicle.IsAvailable);
    }

    [Fact]
    public void IsAvailable_FalseWhenFatalErrorPresent()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState() with
        {
            Errors = [new VdaError { ErrorLevel = "FATAL", ErrorType = "EMERGENCY_STOP" }]
        });

        Assert.False(vehicle.IsAvailable);
    }

    [Fact]
    public void IsAvailable_TrueWhenOnlyNonFatalErrorPresent()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState() with
        {
            Errors = [new VdaError { ErrorLevel = "WARNING", ErrorType = "SENSOR_WARNING" }]
        });

        Assert.True(vehicle.IsAvailable);
    }

    // ── ApplyState ────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyState_SetsStatusToIdle_WhenNotDrivingAndNoOrder()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState());

        Assert.Equal(VehicleStatus.Idle, vehicle.Status);
    }

    [Fact]
    public void ApplyState_SetsStatusToDriving_WhenDrivingFlagIsTrue()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState() with { Driving = true });

        Assert.Equal(VehicleStatus.Driving, vehicle.Status);
    }

    [Fact]
    public void ApplyState_SetsStatusToBusy_WhenHasActiveOrderId()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState() with { OrderId = "TO-ABCDEF12345678" });

        Assert.Equal(VehicleStatus.Busy, vehicle.Status);
    }

    [Fact]
    public void ApplyState_SetsStatusToError_WhenFatalErrorPresent()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState() with
        {
            Errors = [new VdaError { ErrorLevel = "FATAL" }]
        });

        Assert.Equal(VehicleStatus.Error, vehicle.Status);
    }

    [Fact]
    public void ApplyState_FatalErrorTakesPrecedenceOverDriving()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState() with
        {
            Driving = true,
            Errors  = [new VdaError { ErrorLevel = "FATAL" }]
        });

        Assert.Equal(VehicleStatus.Error, vehicle.Status);
    }

    [Fact]
    public void ApplyState_UpdatesBattery()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState() with
        {
            BatteryState = new BatteryState { BatteryCharge = 42.5, Charging = true }
        });

        Assert.Equal(42.5, vehicle.Battery!.BatteryCharge);
        Assert.True(vehicle.Battery.Charging);
    }

    [Fact]
    public void ApplyState_UpdatesPosition()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState() with
        {
            AgvPosition = new AgvPosition { X = 5.0, Y = 3.0, MapId = "FLOOR-1" }
        });

        Assert.NotNull(vehicle.Position);
        Assert.Equal(5.0, vehicle.Position.X);
        Assert.Equal(3.0, vehicle.Position.Y);
    }

    [Fact]
    public void ApplyState_SetsCurrentOrderId()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState() with { OrderId = "TO-123" });

        Assert.Equal("TO-123", vehicle.CurrentOrderId);
    }

    [Fact]
    public void ApplyState_ClearsCurrentOrderId_WhenOrderIdIsEmpty()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState() with { OrderId = "TO-123" });
        vehicle.ApplyState(IdleState() with { OrderId = "" });

        Assert.Null(vehicle.CurrentOrderId);
    }

    [Fact]
    public void ApplyState_UpdatesLastSeen()
    {
        var before  = DateTime.UtcNow.AddSeconds(-1);
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState());

        Assert.True(vehicle.LastSeen >= before);
    }

    // ── ApplyConnection ───────────────────────────────────────────────────────

    [Fact]
    public void ApplyConnection_SetsIdle_WhenOnline()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyConnection(new ConnectionMessage
        {
            Manufacturer    = "Acme",
            SerialNumber    = "SN-001",
            ConnectionState = "ONLINE"
        });

        Assert.Equal(VehicleStatus.Idle, vehicle.Status);
    }

    [Fact]
    public void ApplyConnection_SetsOffline_WhenDisconnected()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyConnection(new ConnectionMessage
        {
            Manufacturer    = "Acme",
            SerialNumber    = "SN-001",
            ConnectionState = "OFFLINE"
        });

        Assert.Equal(VehicleStatus.Offline, vehicle.Status);
    }

    // ── NextHeaderId ──────────────────────────────────────────────────────────

    [Fact]
    public void NextHeaderId_StartsAtOne()
    {
        var vehicle = new Vehicle("Acme", "SN-001");

        Assert.Equal(1, vehicle.NextHeaderId());
    }

    [Fact]
    public void NextHeaderId_IncrementsOnEachCall()
    {
        var vehicle = new Vehicle("Acme", "SN-001");

        var first  = vehicle.NextHeaderId();
        var second = vehicle.NextHeaderId();
        var third  = vehicle.NextHeaderId();

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(3, third);
    }

    [Fact]
    public void NextHeaderId_IsThreadSafe()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        var ids     = new int[100];

        Parallel.For(0, 100, i => ids[i] = vehicle.NextHeaderId());

        Assert.Equal(100, ids.Distinct().Count());
        Assert.Equal(100, ids.Max());
    }

    // ── Constructor validation ────────────────────────────────────────────────

    [Fact]
    public void Constructor_ThrowsArgumentException_WhenManufacturerIsNullOrEmpty()
    {
        Assert.Throws<ArgumentException>(() => new Vehicle(null!, "SN-001"));
        Assert.Throws<ArgumentException>(() => new Vehicle("", "SN-001"));
        Assert.Throws<ArgumentException>(() => new Vehicle("  ", "SN-001"));
    }

    [Fact]
    public void Constructor_ThrowsArgumentException_WhenSerialNumberIsNullOrEmpty()
    {
        Assert.Throws<ArgumentException>(() => new Vehicle("Acme", null!));
        Assert.Throws<ArgumentException>(() => new Vehicle("Acme", ""));
        Assert.Throws<ArgumentException>(() => new Vehicle("Acme", "  "));
    }

    // ── HasFatalError ─────────────────────────────────────────────────────────

    [Fact]
    public void HasFatalError_True_WhenFatalErrorPresent()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState() with
        {
            Errors = [new VdaError { ErrorLevel = "FATAL", ErrorType = "EMERGENCY_STOP" }]
        });

        Assert.True(vehicle.HasFatalError);
    }

    [Fact]
    public void HasFatalError_False_WhenNoFatalError()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState() with
        {
            Errors = [new VdaError { ErrorLevel = "WARNING", ErrorType = "SENSOR_WARNING" }]
        });

        Assert.False(vehicle.HasFatalError);
    }

    [Fact]
    public void HasFatalError_False_WhenNoErrors()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState());

        Assert.False(vehicle.HasFatalError);
    }

    // ── ActiveErrors ──────────────────────────────────────────────────────────

    [Fact]
    public void ActiveErrors_IsReadOnlyList()
    {
        var vehicle = new Vehicle("Acme", "SN-001");
        vehicle.ApplyState(IdleState() with
        {
            Errors = [new VdaError { ErrorLevel = "WARNING" }]
        });

        Assert.IsAssignableFrom<IReadOnlyList<VdaError>>(vehicle.ActiveErrors);
        Assert.Single(vehicle.ActiveErrors);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static VehicleState IdleState() => new()
    {
        Manufacturer = "Acme",
        SerialNumber = "SN-001",
        Driving      = false,
        BatteryState = new BatteryState { BatteryCharge = 80.0 },
        Errors       = [],
        NodeStates   = [],
        EdgeStates   = []
    };
}
