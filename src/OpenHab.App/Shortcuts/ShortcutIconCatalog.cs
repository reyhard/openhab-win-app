using System.Collections.Immutable;

namespace OpenHab.App.Shortcuts;

public sealed record ShortcutIconDefinition(string Id, string Label, string Group);

public static class ShortcutIconCatalog
{
    public static ImmutableArray<ShortcutIconDefinition> All { get; } =
    [
        // Lighting
        new("light-bulb", "Light Bulb", "Lighting"),
        new("ceiling-light", "Ceiling Light", "Lighting"),
        new("lamp", "Lamp", "Lighting"),
        new("strip-light", "Strip Light", "Lighting"),
        new("brightness", "Brightness", "Lighting"),
        new("color-wheel", "Color Wheel", "Lighting"),
        new("power", "Power", "Lighting"),
        // Openings
        new("blinds", "Blinds", "Openings"),
        new("curtains", "Curtains", "Openings"),
        new("garage", "Garage", "Openings"),
        new("door", "Door", "Openings"),
        new("lock", "Lock", "Openings"),
        // Climate
        new("thermostat", "Thermostat", "Climate"),
        new("fan", "Fan", "Climate"),
        new("snowflake", "Snowflake", "Climate"),
        new("flame", "Flame", "Climate"),
        new("humidity", "Humidity", "Climate"),
        // Media
        new("play", "Play", "Media"),
        new("pause", "Pause", "Media"),
        new("stop", "Stop", "Media"),
        new("speaker", "Speaker", "Media"),
        new("tv", "TV", "Media"),
        new("cast", "Cast", "Media"),
        new("music", "Music", "Media"),
        new("volume", "Volume", "Media"),
        // Scenes and tools
        new("scene", "Scene", "Scenes and tools"),
        new("movie", "Movie", "Scenes and tools"),
        new("sleep", "Sleep", "Scenes and tools"),
        new("away", "Away", "Scenes and tools"),
        new("sparkle", "Sparkle", "Scenes and tools"),
        new("timer", "Timer", "Scenes and tools"),
        new("custom", "Custom", "Scenes and tools")
    ];

    public static bool Contains(string iconId) =>
        All.Any(icon => string.Equals(icon.Id, iconId, StringComparison.Ordinal));
}
