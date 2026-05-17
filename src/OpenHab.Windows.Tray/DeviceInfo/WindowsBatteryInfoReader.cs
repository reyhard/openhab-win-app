using System.Diagnostics.CodeAnalysis;
using Windows.System.Power;

namespace OpenHab.Windows.Tray.DeviceInfo;

[ExcludeFromCodeCoverage(Justification = "Windows device-state reader for live OS battery state.")]
internal sealed record WindowsBatteryInfo(int? BatteryLevelPercent, bool? IsCharging);

[ExcludeFromCodeCoverage(Justification = "Windows device-state reader for live OS battery state.")]
internal static class WindowsBatteryInfoReader
{
    public static WindowsBatteryInfo Read()
    {
        var report = PowerManager.BatteryStatus;
        bool? isCharging = report switch
        {
            BatteryStatus.Charging => true,
            BatteryStatus.Discharging => false,
            BatteryStatus.Idle => true,
            BatteryStatus.NotPresent => ReadPowerSupplyChargingStatus(),
            _ => (bool?)null
        };

        var level = TryReadBatteryLevel();
        return new WindowsBatteryInfo(level, isCharging);
    }

    private static int? TryReadBatteryLevel()
    {
        try
        {
            var aggregateBattery = global::Windows.Devices.Power.Battery.AggregateBattery;
            var report = aggregateBattery.GetReport();
            if (report.FullChargeCapacityInMilliwattHours is null ||
                report.RemainingCapacityInMilliwattHours is null ||
                report.FullChargeCapacityInMilliwattHours <= 0)
            {
                return null;
            }

            var percent = (double)report.RemainingCapacityInMilliwattHours.Value /
                report.FullChargeCapacityInMilliwattHours.Value * 100;
            return Math.Clamp((int)Math.Round(percent), 0, 100);
        }
        catch
        {
            return null;
        }
    }

    private static bool? ReadPowerSupplyChargingStatus()
    {
        return PowerManager.PowerSupplyStatus switch
        {
            PowerSupplyStatus.Adequate => true,
            PowerSupplyStatus.Inadequate => true,
            PowerSupplyStatus.NotPresent => false,
            _ => (bool?)null
        };
    }
}
