using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace AshesAndAether_Client;

public sealed class ThemeManager
{
    private readonly Dictionary<string, ThemeDefinition> _presets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ember"] = new ThemeDefinition(
            "ember",
            Color.BrightYellow,
            Color.Black,
            Color.BrightRed,
            Color.Black,
            Color.DarkGray),
        ["dusk"] = new ThemeDefinition(
            "dusk",
            Color.BrightCyan,
            Color.Black,
            Color.BrightMagenta,
            Color.Black,
            Color.DarkGray),
        ["terminal"] = new ThemeDefinition(
            "terminal",
            Color.BrightGreen,
            Color.Black,
            Color.BrightGreen,
            Color.Black,
            Color.DarkGray),
        ["parchment"] = new ThemeDefinition(
            "parchment",
            Color.Black,
            Color.BrightYellow,
            Color.Blue,
            Color.BrightYellow,
            Color.DarkGray)
    };

    public IReadOnlyList<string> PresetNames => _presets.Keys.OrderBy(name => name).ToList();

    public ColorScheme Resolve(string? themeName, ThemeConfig? customTheme, out string resolvedName)
    {
        if (string.Equals(themeName, "custom", StringComparison.OrdinalIgnoreCase) && HasCustomTheme(customTheme))
        {
            resolvedName = "custom";
            var fallback = _presets["ember"];
            return BuildScheme(ApplyCustomTheme(customTheme!, fallback));
        }

        if (string.IsNullOrWhiteSpace(themeName) || !_presets.TryGetValue(themeName, out var preset))
        {
            preset = _presets["ember"];
        }

        resolvedName = preset.Name;
        return BuildScheme(preset);
    }

    public bool HasCustomTheme(ThemeConfig? customTheme)
    {
        if (customTheme == null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(customTheme.NormalForeground) ||
               !string.IsNullOrWhiteSpace(customTheme.NormalBackground) ||
               !string.IsNullOrWhiteSpace(customTheme.AccentForeground) ||
               !string.IsNullOrWhiteSpace(customTheme.AccentBackground) ||
               !string.IsNullOrWhiteSpace(customTheme.MutedForeground) ||
               !string.IsNullOrWhiteSpace(customTheme.MutedBackground);
    }

    private static ColorScheme BuildScheme(ThemeDefinition theme)
    {
        var normal = new Attribute(theme.NormalForeground, theme.NormalBackground);
        var focus = new Attribute(theme.AccentForeground, theme.AccentBackground);
        var hotNormal = new Attribute(theme.AccentForeground, theme.NormalBackground);
        var hotFocus = new Attribute(theme.AccentForeground, theme.AccentBackground);
        var disabled = new Attribute(theme.MutedForeground, theme.NormalBackground);

        return new ColorScheme
        {
            Normal = normal,
            Focus = focus,
            HotNormal = hotNormal,
            HotFocus = hotFocus,
            Disabled = disabled
        };
    }

    private static ThemeDefinition ApplyCustomTheme(ThemeConfig custom, ThemeDefinition fallback)
    {
        var normalForeground = ParseColor(custom.NormalForeground, fallback.NormalForeground);
        var normalBackground = ParseColor(custom.NormalBackground, fallback.NormalBackground);
        var accentForeground = ParseColor(custom.AccentForeground, fallback.AccentForeground);
        var accentBackground = ParseColor(custom.AccentBackground, fallback.AccentBackground);
        var mutedForeground = ParseColor(custom.MutedForeground, fallback.MutedForeground);

        return new ThemeDefinition(
            "custom",
            normalForeground,
            normalBackground,
            accentForeground,
            accentBackground,
            mutedForeground);
    }

    private static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (ColorParser.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }
}

public sealed record ThemeDefinition(
    string Name,
    Color NormalForeground,
    Color NormalBackground,
    Color AccentForeground,
    Color AccentBackground,
    Color MutedForeground);

internal static class ColorParser
{
    private static readonly Dictionary<string, Color> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = Color.Black,
        ["blue"] = Color.Blue,
        ["green"] = Color.Green,
        ["cyan"] = Color.Cyan,
        ["red"] = Color.Red,
        ["magenta"] = Color.Magenta,
        ["brown"] = Color.Brown,
        ["gray"] = Color.Gray,
        ["grey"] = Color.Gray,
        ["darkgray"] = Color.DarkGray,
        ["darkgrey"] = Color.DarkGray,
        ["brightblue"] = Color.BrightBlue,
        ["brightgreen"] = Color.BrightGreen,
        ["brightcyan"] = Color.BrightCyan,
        ["brightred"] = Color.BrightRed,
        ["brightmagenta"] = Color.BrightMagenta,
        ["brightyellow"] = Color.BrightYellow,
        ["white"] = Color.White
    };

    public static bool TryParse(string value, out Color color)
    {
        var normalized = Normalize(value);
        return Colors.TryGetValue(normalized, out color);
    }

    private static string Normalize(string value)
    {
        return value.Trim().Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);
    }
}
