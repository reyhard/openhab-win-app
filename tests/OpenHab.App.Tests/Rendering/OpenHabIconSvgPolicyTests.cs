using System.Text;
using OpenHab.Windows.Tray.Rendering;

namespace OpenHab.App.Tests.Rendering;

public sealed class OpenHabIconSvgPolicyTests
{
    [Theory]
    [InlineData("#123456", "#123456")]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("#80123456", "#123456")]
    [InlineData("Red", "#FF0000")]
    [InlineData("blue", "#0000FF")]
    public void TryNormalizeColorToHex_NormalizesSupportedValues(string input, string expected)
    {
        var result = OpenHabIconSvgPolicy.TryNormalizeColorToHex(input, out var normalized);

        Assert.True(result);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123456")]
    [InlineData("abc")]
    [InlineData("#12")]
    [InlineData("#12345g")]
    public void TryNormalizeColorToHex_RejectsUnsupportedValues(string input)
    {
        var result = OpenHabIconSvgPolicy.TryNormalizeColorToHex(input, out var normalized);

        Assert.False(result);
        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public void LooksLikeSvg_ReturnsTrueForSvgMediaType()
    {
        var result = OpenHabIconSvgPolicy.LooksLikeSvg("image/svg+xml", Encoding.UTF8.GetBytes("not svg"));

        Assert.True(result);
    }

    [Fact]
    public void LooksLikeSvg_ReturnsTrueForSvgPayload()
    {
        var result = OpenHabIconSvgPolicy.LooksLikeSvg(null, Encoding.UTF8.GetBytes("  <svg viewBox=\"0 0 1 1\" />"));

        Assert.True(result);
    }

    [Fact]
    public void LooksLikeSvg_ReturnsTrueForXmlSvgPayload()
    {
        var result = OpenHabIconSvgPolicy.LooksLikeSvg(
            null,
            Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg viewBox=\"0 0 1 1\" />"));

        Assert.True(result);
    }

    [Fact]
    public void LooksLikeSvg_TrimsBomAndWhitespaceBeforeSvgPayload()
    {
        var result = OpenHabIconSvgPolicy.LooksLikeSvg(
            null,
            Encoding.UTF8.GetBytes("\uFEFF\r\n\t <svg viewBox=\"0 0 1 1\" />"));

        Assert.True(result);
    }

    [Fact]
    public void TryApplySvgColorTint_InsertsSvgColorStyle()
    {
        var svg = Encoding.UTF8.GetBytes("<svg viewBox=\"0 0 1 1\"><path fill=\"currentColor\" /></svg>");

        var tinted = OpenHabIconSvgPolicy.TryApplySvgColorTint(svg, "#0a1b2c");

        Assert.Equal(
            "<svg style=\"color:#0A1B2C;\" viewBox=\"0 0 1 1\"><path fill=\"currentColor\" /></svg>",
            Encoding.UTF8.GetString(tinted!));
    }

    [Fact]
    public void TryApplySvgColorTint_ReturnsNullForUnsupportedColor()
    {
        var svg = Encoding.UTF8.GetBytes("<svg><path fill=\"currentColor\" /></svg>");

        var tinted = OpenHabIconSvgPolicy.TryApplySvgColorTint(svg, "not-a-color");

        Assert.Null(tinted);
    }
}
