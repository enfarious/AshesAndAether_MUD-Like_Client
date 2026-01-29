using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Terminal.Gui;

namespace AshesAndAether_Client;

public sealed class CompassView : View
{
    private static readonly string[] Lines =
    {
        "       N       ",
        "   NW  |  NE   ",
        "W -----+----- E",
        "   SW  |  SE   ",
        "       S       ",
        "",
        "Stop Walk Jog Run",
        "          ^"
    };

    private readonly Dictionary<string, Rect> _hitTargets = new(StringComparer.OrdinalIgnoreCase);
    private int _lastOffsetX;

    public string? SelectedDirection { get; set; }
    public string? SelectedSpeed { get; set; }

    public event Action<string>? DirectionSelected;
    public event Action<string>? SpeedSelected;
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

                if (IsDirection(entry.Key))
                {
                    DirectionSelected?.Invoke(entry.Key);
                    return true;
                }

                SpeedSelected?.Invoke(entry.Key);
                return true;
            }
        }

        return false;
    }

    public override void Redraw(Rect bounds)
    {
        Driver.SetAttribute(ColorScheme?.Normal ?? Colors.Base.Normal);

        var content = BuildDisplay();
        var lines = content.Split('\n');
        var offsetX = Math.Max(0, (bounds.Width - lines.Max(line => line.Length)) / 2);
        _lastOffsetX = offsetX;
        BuildHitTargets();

        for (var y = 0; y < bounds.Height; y++)
        {
            Driver.Move(0, y);
            Driver.AddStr(new string(' ', bounds.Width));
        }

        for (var y = 0; y < lines.Length && y < bounds.Height; y++)
        {
            var line = lines[y];
            Driver.Move(offsetX, y);
            Driver.AddStr(line);
        }
    }

    private string BuildDisplay()
    {
        var lines = Lines.ToArray();
        var selectedSpeed = NormalizeSpeed(SelectedSpeed);
        if (!string.IsNullOrWhiteSpace(selectedSpeed))
        {
            var speedLine = lines[6];
            var caretLine = new char[speedLine.Length];
            Array.Fill(caretLine, ' ');
            var token = selectedSpeed switch
            {
                "jog" => "Jog",
                "run" => "Run",
                _ => "Walk"
            };
            var start = speedLine.IndexOf(token, StringComparison.Ordinal);
            if (start >= 0)
            {
                for (var i = 0; i < token.Length && start + i < caretLine.Length; i++)
                {
                    caretLine[start + i] = '^';
                }
                lines[7] = new string(caretLine);
            }
        }

        var builder = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            builder.Append(lines[i]);
            if (i < lines.Length - 1)
            {
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private void BuildHitTargets()
    {
        _hitTargets.Clear();
        for (var row = 0; row < Lines.Length; row++)
        {
            var line = Lines[row];
            var tokens = new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            foreach (var token in tokens)
            {
                var index = line.IndexOf(token, StringComparison.Ordinal);
                if (index >= 0)
                {
                    _hitTargets[token] = new Rect(_lastOffsetX + index, row, token.Length, 1);
                }
            }

            var stopIndex = line.IndexOf("Stop", StringComparison.Ordinal);
            if (stopIndex >= 0)
            {
                _hitTargets["Stop"] = new Rect(_lastOffsetX + stopIndex, row, 4, 1);
            }

            var walkIndex = line.IndexOf("Walk", StringComparison.Ordinal);
            if (walkIndex >= 0)
            {
                _hitTargets["Walk"] = new Rect(_lastOffsetX + walkIndex, row, 4, 1);
            }

            var jogIndex = line.IndexOf("Jog", StringComparison.Ordinal);
            if (jogIndex >= 0)
            {
                _hitTargets["Jog"] = new Rect(_lastOffsetX + jogIndex, row, 3, 1);
            }

            var runIndex = line.IndexOf("Run", StringComparison.Ordinal);
            if (runIndex >= 0)
            {
                _hitTargets["Run"] = new Rect(_lastOffsetX + runIndex, row, 3, 1);
            }
        }
    }

    private static bool IsDirection(string token)
    {
        return token.Length <= 2 && token.All(char.IsLetter) && char.IsUpper(token[0]);
    }

    private static string? NormalizeSpeed(string? speed)
    {
        if (string.IsNullOrWhiteSpace(speed))
        {
            return null;
        }

        var normalized = speed.Trim().ToLowerInvariant();
        return normalized switch
        {
            "sprint" => "run",
            _ => normalized
        };
    }
}
