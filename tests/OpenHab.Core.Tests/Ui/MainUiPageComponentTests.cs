using System.Collections.ObjectModel;
using System.Text.Json;
using OpenHab.Core.Ui;

namespace OpenHab.Core.Tests.Ui;

public sealed class MainUiPageComponentTests
{
    private static MainUiPageComponent CreateComponent(params (string Key, string RawJson)[] configEntries)
    {
        var config = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, rawJson) in configEntries)
        {
            using var document = JsonDocument.Parse(rawJson);
            config[key] = document.RootElement.Clone();
        }

        return new MainUiPageComponent(
            "page",
            "oh-layout-page",
            new ReadOnlyDictionary<string, JsonElement>(config));
    }

    [Fact]
    public void GetConfigString_ReturnsNullForMissingKey()
    {
        var component = CreateComponent(("label", "\"Energy\""));

        Assert.Null(component.GetConfigString("icon"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void GetConfigString_ReturnsNullForUnsupportedKinds(string rawJson)
    {
        var component = CreateComponent(("value", rawJson));

        Assert.Null(component.GetConfigString("value"));
    }

    [Theory]
    [InlineData("\"Energy\"", "Energy")]
    [InlineData("\"  padded label  \"", "  padded label  ")]
    [InlineData("\"\"", "")]
    [InlineData("20", "20")]
    [InlineData("-1.50", "-1.50")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    public void GetConfigString_ConvertsSupportedScalarKinds(string rawJson, string expected)
    {
        var component = CreateComponent(("value", rawJson));

        Assert.Equal(expected, component.GetConfigString("value"));
    }

    [Fact]
    public void GetConfigBoolean_ReturnsFalseForMissingKey()
    {
        var component = CreateComponent(("sidebar", "true"));

        Assert.False(component.GetConfigBoolean("visible"));
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("\"not-a-bool\"", false)]
    [InlineData("\"\"", false)]
    [InlineData("1", false)]
    [InlineData("null", false)]
    [InlineData("{}", false)]
    [InlineData("true", true)]
    [InlineData("\"TrUe\"", true)]
    [InlineData("\"false\"", false)]
    public void GetConfigBoolean_ConvertsSupportedValuesAndRejectsOthers(string rawJson, bool expected)
    {
        var component = CreateComponent(("value", rawJson));

        Assert.Equal(expected, component.GetConfigBoolean("value"));
    }

    [Fact]
    public void GetConfigInt32_ReturnsNullForMissingKey()
    {
        var component = CreateComponent(("order", "20"));

        Assert.Null(component.GetConfigInt32("priority"));
    }

    [Theory]
    [InlineData("20", 20)]
    [InlineData("-7", -7)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    [InlineData("\"20\"", 20)]
    [InlineData("\"-7\"", -7)]
    [InlineData("\"  42  \"", 42)]
    [InlineData("\"2147483647\"", int.MaxValue)]
    [InlineData("\"-2147483648\"", int.MinValue)]
    [InlineData("\"\"", null)]
    [InlineData("1.5", null)]
    [InlineData("2147483648", null)]
    [InlineData("\"abc\"", null)]
    [InlineData("\"2147483648\"", null)]
    [InlineData("\"-2147483649\"", null)]
    [InlineData("true", null)]
    [InlineData("null", null)]
    [InlineData("{}", null)]
    public void GetConfigInt32_ConvertsInvariantIntegersAndRejectsOthers(string rawJson, int? expected)
    {
        var component = CreateComponent(("value", rawJson));

        Assert.Equal(expected, component.GetConfigInt32("value"));
    }
}
