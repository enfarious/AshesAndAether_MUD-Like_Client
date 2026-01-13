using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace WodMudClient;

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

internal sealed record LogEntry(string Text, LogKind Kind);

internal sealed class LogListSource : IListDataSource
{
    private readonly List<LogEntry> _entries;
    private readonly HashSet<int> _marks = new();
    private Dictionary<LogKind, Attribute> _palette = new();

    public LogListSource(List<LogEntry> entries)
    {
        _entries = entries;
        UpdatePalette(Application.Top?.ColorScheme ?? Colors.Base);
    }

    public int Count => _entries.Count;

    public int Length
    {
        get
        {
            if (_entries.Count == 0)
            {
                return 0;
            }

            var max = 0;
            foreach (var entry in _entries)
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
        return _entries.Select(entry => entry.Text).ToList();
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
        if (item < 0 || item >= _entries.Count)
        {
            return;
        }

        var entry = _entries[item];
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

        driver.Move(col, line);
        var scheme = listView.ColorScheme ?? Colors.Base;
        driver.SetAttribute(marked ? scheme.Focus : GetAttribute(entry.Kind));
        driver.AddStr(text);
    }

    public void UpdatePalette(ColorScheme scheme)
    {
        var bg = scheme.Normal.Background;
        _palette = new Dictionary<LogKind, Attribute>
        {
            [LogKind.Default] = scheme.Normal,
            [LogKind.System] = new Attribute(Color.BrightCyan, bg),
            [LogKind.Outbound] = new Attribute(Color.BrightMagenta, bg),
            [LogKind.Inbound] = new Attribute(Color.BrightGreen, bg),
            [LogKind.Chat] = new Attribute(scheme.HotNormal.Foreground, bg),
            [LogKind.Warning] = new Attribute(Color.BrightYellow, bg),
            [LogKind.Error] = new Attribute(Color.BrightRed, bg)
        };
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
