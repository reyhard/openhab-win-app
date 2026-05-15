using OpenHab.Windows.Tray.DeviceInfo;

namespace OpenHab.App.Tests.DeviceInfo;

public sealed class WindowsFocusInfoReaderTests
{
    [Fact]
    public void ReadStateReturnsUnsupportedWhenFocusSessionApiIsUnavailable()
    {
        var reader = new WindowsFocusInfoReader(
            readQuietHoursProfile: () => null,
            isSupported: () => false,
            isFocusActive: () => throw new InvalidOperationException("Should not read focus state"));

        Assert.Equal("UNSUPPORTED", reader.ReadState());
    }

    [Theory]
    [InlineData("Microsoft.QuietHoursProfile.AlarmsOnly")]
    [InlineData("Microsoft.QuietHoursProfile.PriorityOnly")]
    [InlineData("AlarmsOnly")]
    [InlineData("PriorityOnly")]
    public void ReadStateReturnsOnWhenDoNotDisturbProfileIsActive(string profile)
    {
        var reader = new WindowsFocusInfoReader(
            readQuietHoursProfile: () => profile,
            isSupported: () => true,
            isFocusActive: () => false);

        Assert.Equal("ON", reader.ReadState());
    }

    [Theory]
    [InlineData("Microsoft.QuietHoursProfile.Unrestricted")]
    [InlineData("Unrestricted")]
    public void ReadStateReturnsOnWhenFocusSessionIsActiveAndDoNotDisturbProfileIsUnrestricted(string profile)
    {
        var reader = new WindowsFocusInfoReader(
            readQuietHoursProfile: () => profile,
            isSupported: () => true,
            isFocusActive: () => true);

        Assert.Equal("ON", reader.ReadState());
    }

    [Theory]
    [InlineData("Microsoft.QuietHoursProfile.Unrestricted")]
    [InlineData("Unrestricted")]
    public void ReadStateReturnsOffWhenFocusSessionIsInactiveAndDoNotDisturbProfileIsUnrestricted(string profile)
    {
        var reader = new WindowsFocusInfoReader(
            readQuietHoursProfile: () => profile,
            isSupported: () => true,
            isFocusActive: () => false);

        Assert.Equal("OFF", reader.ReadState());
    }

    [Theory]
    [InlineData("Microsoft.QuietHoursProfile.Unrestricted")]
    [InlineData("Unrestricted")]
    public void ReadStateReturnsOffWhenFocusSessionApiIsUnavailableAndDoNotDisturbProfileIsUnrestricted(string profile)
    {
        var reader = new WindowsFocusInfoReader(
            readQuietHoursProfile: () => profile,
            isSupported: () => false,
            isFocusActive: () => throw new InvalidOperationException("Should not read focus state"));

        Assert.Equal("OFF", reader.ReadState());
    }

    [Theory]
    [InlineData(true, "ON")]
    [InlineData(false, "OFF")]
    public void ReadStateFallsBackToFocusSessionStateWhenDoNotDisturbProfileIsUnavailable(
        bool isFocusActive,
        string expected)
    {
        var reader = new WindowsFocusInfoReader(
            readQuietHoursProfile: () => null,
            isSupported: () => true,
            isFocusActive: () => isFocusActive);

        Assert.Equal(expected, reader.ReadState());
    }

    [Fact]
    public void ReadStateReturnsUnsupportedWhenFocusSessionApiFails()
    {
        var reader = new WindowsFocusInfoReader(
            readQuietHoursProfile: () => null,
            isSupported: () => true,
            isFocusActive: () => throw new InvalidOperationException("Focus API failed"));

        Assert.Equal("UNSUPPORTED", reader.ReadState());
    }

    [Fact]
    public void TryReadQuietHoursProfileExtractsSelectedProfileFromCloudDataStoreOutput()
    {
        var output = """
            /type: windows.data.notifications.quiethourssettings

            [
                {"Data":{"isInitialized":true,"selectedProfile":"Microsoft.QuietHoursProfile.AlarmsOnly"}}
            ]
            """;

        Assert.Equal(
            "Microsoft.QuietHoursProfile.AlarmsOnly",
            WindowsFocusInfoReader.TryReadQuietHoursProfile(output));
    }

    [Fact]
    public void ReadQuietHoursProfileFromCloudDataStorePrefersCurrentDoNotDisturbSettingOverLegacyNotificationsSetting()
    {
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["windows.data.notifications.quiethourssettings"] = """
                [
                    {"Data":{"selectedProfile":"Microsoft.QuietHoursProfile.AlarmsOnly"}}
                ]
                """,
            ["windows.data.donotdisturb.QuietHoursSettings"] = """
                [
                    {"Data":{"selectedProfile":"Microsoft.QuietHoursProfile.Unrestricted"}}
                ]
                """
        };

        var profile = WindowsFocusInfoReader.ReadQuietHoursProfileFromCloudDataStore(typeName => outputs[typeName]);

        Assert.Equal("Microsoft.QuietHoursProfile.Unrestricted", profile);
    }
}
