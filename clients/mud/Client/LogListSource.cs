using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace AshesAndAether_Client;

internal enum LogKind
{
    Default,
    System,
    Outbound,
    Inbound,
    Chat,
    Warning,
    Error
}

internal enum LogEffect
{
    None,
    Shake
}

internal sealed record LogEntry(
    string Text,
    LogKind Kind,
    Color? ForegroundOverride = null,
    Color? BackgroundOverride = null,
    LogEffect Effect = LogEffect.None,
    double EffectStrength = 0,
    int EffectSeed = 0);

internal sealed record LogRow(
    string Text,
    LogKind Kind,
    Color? ForegroundOverride = null,
    Color? BackgroundOverride = null,
    LogEffect Effect = LogEffect.None,
    double EffectStrength = 0,
    int EffectSeed = 0);

internal sealed class LogListSource : IListDataSource
{
    private readonly List<LogEntry> _entries;
    private readonly List<LogRow> _rows = new();
    private readonly HashSet<int> _marks = new();
    private Dictionary<LogKind, Attribute> _palette = new();
    private int _wrapWidth;
    private bool _wordWrapEnabled;
    private ChatStyleConfig? _chatStyle;

    public LogListSource(List<LogEntry> entries)
    {
        _entries = entries;
        UpdatePalette(Application.Top?.ColorScheme ?? Colors.Base);
    }

    public int Count => _rows.Count;

    public int Length
    {
        get
        {
            if (_rows.Count == 0)
            {
                return 0;
            }

            var max = 0;
            foreach (var entry in _rows)
            {
                if (entry.Text.Length > max)
                {
                    max = entry.Text.Length;
                }
            }

            return max;
        }
    }

    public bool IsMarked(int item)
    {
        return _marks.Contains(item);
    }

    public IList ToList()
    {
        return _rows.Select(entry => entry.Text).ToList();
    }

    public void SetMark(int item, bool value)
    {
        if (value)
        {
            _marks.Add(item);
        }
        else
        {
            _marks.Remove(item);
        }
    }

    public void Render(ListView listView, ConsoleDriver driver, bool marked, int item, int col, int line, int width, int height)
    {
        if (item < 0 || item >= _rows.Count)
        {
            return;
        }

        var entry = _rows[item];
        var text = entry.Text ?? string.Empty;
        if (col < 0)
        {
            col = 0;
        }

        if (col > 0 && col < text.Length)
        {
            text = text.Substring(col);
        }
        else if (col >= text.Length)
        {
            text = string.Empty;
        }

        if (text.Length > width)
        {
            text = text.Substring(0, width);
        }
        else if (text.Length < width)
        {
            text = text.PadRight(width);
        }

        listView.Move(0, line);
        var scheme = listView.ColorScheme ?? Colors.Base;
        var attribute = marked ? scheme.Focus : GetAttribute(entry.Kind);
        if (entry.ForegroundOverride.HasValue || entry.BackgroundOverride.HasValue)
        {
            var foreground = entry.ForegroundOverride ?? attribute.Foreground;
            var background = entry.BackgroundOverride ?? attribute.Background;
            attribute = new Attribute(foreground, background);
        }
        if (entry.Effect == LogEffect.Shake && entry.EffectStrength > 0)
        {
            RenderShakeText(driver, attribute, text, entry);
            return;
        }

        driver.SetAttribute(attribute);
        driver.AddStr(text);
    }

    public void UpdatePalette(ColorScheme scheme, ChatStyleConfig? chatStyle = null)
    {
        _chatStyle = chatStyle;
        var bg = scheme.Normal.Background;
        var chatFg = scheme.HotNormal.Foreground;
        var chatBg = bg;
        if (_chatStyle != null)
        {
            if (!string.IsNullOrWhiteSpace(_chatStyle.Foreground) &&
                ColorParser.TryParse(_chatStyle.Foreground, out var parsedForeground))
            {
                chatFg = parsedForeground;
            }

            if (!string.IsNullOrWhiteSpace(_chatStyle.Background) &&
                ColorParser.TryParse(_chatStyle.Background, out var parsedBackground))
            {
                chatBg = parsedBackground;
            }
        }

        _palette = new Dictionary<LogKind, Attribute>
        {
            [LogKind.Default] = scheme.Normal,
            [LogKind.System] = new Attribute(Color.BrightCyan, bg),
            [LogKind.Outbound] = new Attribute(Color.BrightMagenta, bg),
            [LogKind.Inbound] = new Attribute(Color.BrightGreen, bg),
            [LogKind.Chat] = new Attribute(chatFg, chatBg),
            [LogKind.Warning] = new Attribute(Color.BrightYellow, bg),
            [LogKind.Error] = new Attribute(Color.BrightRed, bg)
        };
    }

    public void RebuildRows(int width, bool wordWrapEnabled)
    {
        _wrapWidth = Math.Max(0, width);
        _wordWrapEnabled = wordWrapEnabled;
        _rows.Clear();

        foreach (var entry in _entries)
        {
            AppendWrappedRows(entry);
        }
    }

    private void AppendWrappedRows(LogEntry entry)
    {
        var text = entry.Text ?? string.Empty;
        if (!_wordWrapEnabled || _wrapWidth <= 0 || text.Length <= _wrapWidth)
        {
            _rows.Add(new LogRow(text, entry.Kind, entry.ForegroundOverride, entry.BackgroundOverride, entry.Effect, entry.EffectStrength, entry.EffectSeed));
            return;
        }

        foreach (var line in WrapLine(text, _wrapWidth))
        {
            _rows.Add(new LogRow(line, entry.Kind, entry.ForegroundOverride, entry.BackgroundOverride, entry.Effect, entry.EffectStrength, entry.EffectSeed));
        }
    }

    private static readonly Color[] ShakePalette =
    {
        Color.BrightYellow,
        Color.BrightCyan,
        Color.BrightMagenta,
        Color.BrightRed,
        Color.BrightGreen,
        Color.BrightBlue,
        Color.White
    };

    private static void RenderShakeText(ConsoleDriver driver, Attribute baseAttribute, string text, LogRow entry)
    {
        var intensity = Math.Clamp(entry.EffectStrength, 0, 1);
        var invertSpan = Math.Max(1, (int)Math.Round(4 - intensity * 3));

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == ' ')
            {
                driver.SetAttribute(baseAttribute);
                driver.AddRune(' ');
                continue;
            }

            var colorIndex = (Hash(entry.EffectSeed, i) & int.MaxValue) % ShakePalette.Length;
            var fg = ShakePalette[colorIndex];
            var invert = (i / invertSpan) % 2 == 1;
            var foreground = invert ? baseAttribute.Background : fg;
            var background = invert ? fg : baseAttribute.Background;
            driver.SetAttribute(new Attribute(foreground, background));
            driver.AddRune(ch);
        }
    }

    private static int Hash(int seed, int value)
    {
        unchecked
        {
            var hash = seed;
            hash = (hash * 397) ^ value;
            return hash;
        }
    }

    private static IEnumerable<string> WrapLine(string text, int width)
    {
        if (width <= 0 || text.Length <= width)
        {
            yield return text;
            yield break;
        }

        var start = 0;
        while (start < text.Length)
        {
            var remaining = text.Length - start;
            if (remaining <= width)
            {
                yield return text.Substring(start);
                yield break;
            }

            var slice = text.Substring(start, width);
            var breakIndex = slice.LastIndexOf(' ');
            if (breakIndex <= 0)
            {
                yield return slice;
                start += width;
            }
            else
            {
                yield return text.Substring(start, breakIndex);
                start += breakIndex + 1;
                while (start < text.Length && text[start] == ' ')
                {
                    start++;
                }
            }
        }
    }

    public static LogKind Classify(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return LogKind.Default;
        }

        var trimmed = line.TrimStart();
        if (trimmed.StartsWith(">>>", StringComparison.Ordinal))
        {
            return LogKind.Outbound;
        }

        if (trimmed.StartsWith("<", StringComparison.Ordinal))
        {
            return LogKind.Inbound;
        }

        if (trimmed.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Auth: error", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Connect failed", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Disconnected", StringComparison.OrdinalIgnoreCase))
        {
            return LogKind.Error;
        }

        if (trimmed.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("WARN", StringComparison.OrdinalIgnoreCase))
        {
            return LogKind.Warning;
        }

        if (trimmed.StartsWith("Connected", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Handshake", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Auth:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Entering world", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Character", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Theme set", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Config reloaded", StringComparison.OrdinalIgnoreCase))
        {
            return LogKind.System;
        }

        if (IsChatLine(trimmed))
        {
            return LogKind.Chat;
        }

        if (trimmed.StartsWith(">", StringComparison.Ordinal))
        {
            return LogKind.Outbound;
        }

        return LogKind.Default;
    }

    private Attribute GetAttribute(LogKind kind)
    {
        if (_palette.TryGetValue(kind, out var attr))
        {
            return attr;
        }

        return _palette[LogKind.Default];
    }

    private static bool IsChatLine(string text)
    {
        return text.Contains(" says,", StringComparison.OrdinalIgnoreCase) ||
               text.Contains(" shouts,", StringComparison.OrdinalIgnoreCase) ||
               text.Contains(" calls for help!", StringComparison.OrdinalIgnoreCase);
    }
}
