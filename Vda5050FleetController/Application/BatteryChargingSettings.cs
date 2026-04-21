namespace Vda5050FleetController.Application;

/// <summary>
/// Runtime-configurable settings for battery-based charging dispatch.
/// </summary>
public class BatteryChargingSettings
{
    /// <summary>
    /// Battery charge percentage below which an idle vehicle is automatically
    /// dispatched to a free charging station.  Default: 30 %.
    /// </summary>
    public double LowBatteryThreshold { get; set; } = 30.0;
}
