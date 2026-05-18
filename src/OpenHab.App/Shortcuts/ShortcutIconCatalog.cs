using System.Collections.Immutable;

namespace OpenHab.App.Shortcuts;

public sealed record ShortcutIconDefinition(string Id, string Label, string Group);

public static class ShortcutIconCatalog
{
    private const string LightingGroup = "lighting";
    private const string OpeningsGroup = "openings";
    private const string ClimateGroup = "climate";
    private const string MediaGroup = "media";
    private const string ScenesToolsGroup = "scenes-tools";

    public static ImmutableArray<ShortcutIconDefinition> All { get; } =
    [
        // Lighting
        new("light-bulb", "Light Bulb", LightingGroup),
        new("ceiling-light", "Ceiling Light", LightingGroup),
        new("lamp", "Lamp", LightingGroup),
        new("strip-light", "Strip Light", LightingGroup),
        new("brightness", "Brightness", LightingGroup),
        new("color-wheel", "Color Wheel", LightingGroup),
        new("power", "Power", LightingGroup),
        // Openings
        new("blinds", "Blinds", OpeningsGroup),
        new("curtains", "Curtains", OpeningsGroup),
        new("garage", "Garage", OpeningsGroup),
        new("door", "Door", OpeningsGroup),
        new("lock", "Lock", OpeningsGroup),
        // Climate
        new("thermostat", "Thermostat", ClimateGroup),
        new("fan", "Fan", ClimateGroup),
        new("snowflake", "Snowflake", ClimateGroup),
        new("flame", "Flame", ClimateGroup),
        new("humidity", "Humidity", ClimateGroup),
        // Media
        new("play", "Play", MediaGroup),
        new("pause", "Pause", MediaGroup),
        new("stop", "Stop", MediaGroup),
        new("speaker", "Speaker", MediaGroup),
        new("tv", "TV", MediaGroup),
        new("cast", "Cast", MediaGroup),
        new("music", "Music", MediaGroup),
        new("volume", "Volume", MediaGroup),
        new("microphone", "Microphone", MediaGroup),
        // Scenes and tools
        new("scene", "Scene", ScenesToolsGroup),
        new("movie", "Movie", ScenesToolsGroup),
        new("sleep", "Sleep", ScenesToolsGroup),
        new("away", "Away", ScenesToolsGroup),
        new("sparkle", "Sparkle", ScenesToolsGroup),
        new("timer", "Timer", ScenesToolsGroup),
        new("custom", "Custom", ScenesToolsGroup)
    ];

    public static bool Contains(string iconId) =>
        All.Any(icon => string.Equals(icon.Id, iconId, StringComparison.Ordinal));
}
