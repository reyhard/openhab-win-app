using Windows.System.Power;

namespace OpenHab.Windows.Tray.DeviceInfo;

internal sealed record WindowsBatteryInfo(int? BatteryLevelPercent, bool? IsCharging);

internal sealed class WindowsBatteryInfoReader
{
    public WindowsBatteryInfo Read()
    {
        var report = PowerManager.BatteryStatus;
        bool? isCharging = report switch
        {
            BatteryStatus.Charging => true,
            BatteryStatus.Discharging => false,
            BatteryStatus.Idle => true,
            BatteryStatus.NotPresent => (bool?)null,
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
}
