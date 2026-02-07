using System;
using System.Collections.Generic;
using System.Linq;
using Terminal.Gui;

namespace AshesAndAether_Client;

public sealed class CompassView : View
{
    private static readonly string[] Lines =
    {
        "------ N ------",
        "-- NW --- NE --",
        "W -- Stop -- E",
        "-- SW --- SE --",
        "------ S ------"
    };

    private readonly Dictionary<string, Rect> _hitTargets = new(StringComparer.OrdinalIgnoreCase);
    private int _lastOffsetX;

    /// <summary>Direction clicked with speed: "jog" (click) or "run" (shift+click).</summary>
    public event Action<string, string>? DirectionSelected;
    public event Action? StopSelected;

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        BuildHitTargets();
    }

    public override bool OnMouseEvent(MouseEvent mouseEvent)
    {
        if (mouseEvent.Flags.HasFlag(MouseFlags.Button1Clicked))
        {
            var shift = mouseEvent.Flags.HasFlag(MouseFlags.ButtonShift);
            var speed = shift ? "run" : "jog";
            var point = new Point(mouseEvent.X, mouseEvent.Y);

            foreach (var entry in _hitTargets)
            {
                if (!entry.Value.Contains(point))
                {
                    continue;
                }

                if (entry.Key == "Stop")
                {
                    StopSelected?.Invoke();
                    return true;
                }

                DirectionSelected?.Invoke(entry.Key, speed);
                return true;
            }
        }

        return false;
    }

    public override void Redraw(Rect bounds)
    {
        Driver.SetAttribute(ColorScheme?.Normal ?? Colors.Base.Normal);

        var maxLineWidth = Lines.Max(line => line.Length);
        var offsetX = Math.Max(0, (bounds.Width - maxLineWidth) / 2);
        _lastOffsetX = offsetX;
        BuildHitTargets();

        for (var y = 0; y < bounds.Height; y++)
        {
            Move(0, y);
            Driver.AddStr(new string(' ', bounds.Width));
        }

        for (var y = 0; y < Lines.Length && y < bounds.Height; y++)
        {
            Move(offsetX, y);
            Driver.AddStr(Lines[y]);
        }
    }

    private void BuildHitTargets()
    {
        _hitTargets.Clear();
        var tokens = new[] { "NW", "NE", "SW", "SE", "N", "E", "S", "W" };

        for (var row = 0; row < Lines.Length; row++)
        {
            var line = Lines[row];

            foreach (var token in tokens)
            {
                var index = FindToken(line, token);
                if (index >= 0 && !_hitTargets.ContainsKey(token))
                {
                    _hitTargets[token] = new Rect(_lastOffsetX + index, row, token.Length, 1);
                }
            }

            var stopIndex = line.IndexOf("Stop", StringComparison.Ordinal);
            if (stopIndex >= 0)
            {
                _hitTargets["Stop"] = new Rect(_lastOffsetX + stopIndex, row, 4, 1);
            }
        }
    }

    private static int FindToken(string line, string token)
    {
        var index = 0;
        while (index < line.Length)
        {
            var pos = line.IndexOf(token, index, StringComparison.Ordinal);
            if (pos < 0)
            {
                return -1;
            }

            var before = pos > 0 ? line[pos - 1] : '-';
            var after = pos + token.Length < line.Length ? line[pos + token.Length] : '-';
            if (char.IsLetter(before) || char.IsLetter(after))
            {
                index = pos + 1;
                continue;
            }

            return pos;
        }

        return -1;
    }
}
