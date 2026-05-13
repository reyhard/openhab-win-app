using System.Collections.Immutable;

namespace OpenHab.App.Shortcuts;

public sealed record ShortcutIconDefinition(string Id, string Label, string Group);

public static class ShortcutIconCatalog
{
    public static ImmutableArray<ShortcutIconDefinition> All { get; } =
    [
        // Lighting
        new("light-bulb", "Light Bulb", "lighting"),
        new("ceiling-light", "Ceiling Light", "lighting"),
        new("lamp", "Lamp", "lighting"),
        new("strip-light", "Strip Light", "lighting"),
        new("brightness", "Brightness", "lighting"),
        new("color-wheel", "Color Wheel", "lighting"),
        new("power", "Power", "lighting"),
        // Openings
        new("blinds", "Blinds", "openings"),
        new("curtains", "Curtains", "openings"),
        new("garage", "Garage", "openings"),
        new("door", "Door", "openings"),
        new("lock", "Lock", "openings"),
        // Climate
        new("thermostat", "Thermostat", "climate"),
        new("fan", "Fan", "climate"),
        new("snowflake", "Snowflake", "climate"),
        new("flame", "Flame", "climate"),
        new("humidity", "Humidity", "climate"),
        // Media
        new("play", "Play", "media"),
        new("pause", "Pause", "media"),
        new("stop", "Stop", "media"),
        new("speaker", "Speaker", "media"),
        new("tv", "TV", "media"),
        new("cast", "Cast", "media"),
        new("music", "Music", "media"),
        new("volume", "Volume", "media"),
        // Scenes and tools
        new("scene", "Scene", "scenes-tools"),
        new("movie", "Movie", "scenes-tools"),
        new("sleep", "Sleep", "scenes-tools"),
        new("away", "Away", "scenes-tools"),
        new("sparkle", "Sparkle", "scenes-tools"),
        new("timer", "Timer", "scenes-tools"),
        new("custom", "Custom", "scenes-tools")
    ];

    public static bool Contains(string iconId) =>
        All.Any(icon => string.Equals(icon.Id, iconId, StringComparison.Ordinal));
}
