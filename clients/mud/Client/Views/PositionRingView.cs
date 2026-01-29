using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Terminal.Gui;

namespace AshesAndAether_Client;

public sealed class PositionRingView : View
{
    private static readonly string[] DirectionLabels = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
    private static readonly string[] LayoutLines =
    {
        "______________ N:R N:J N:W",
        "___ NW:R NW:J NW:W ____ NE:W NE:J NE:R",
        "W:R W:J W:W _______Stop_______ E:W E:J E:R",
        "___ SW:R SW:J SW:W ____ SE:W SE:J SE:R",
        "______________ S:R S:J S:W"
    };
    private static readonly IReadOnlyList<NavSlot> Slots = BuildSlots();

    private readonly Label _wheelLabel;
    private int _angleDegrees;
    private int _bandIndex;
    private bool _initialized;
    private Rect _stopRect;
    private readonly List<NavSlotPosition> _slotPositions = new();

    public bool HasTarget { get; set; }
    public IReadOnlyList<string> RangeBands { get; set; } = Array.Empty<string>();
    public int AngleStep { get; set; } = 45;
    public NavigationRingStyle Style { get; set; } = NavigationRingStyle.Ring;

    public event Action<PositionSelection>? SelectionChanged;
    public event Action<PositionSelection>? SelectionConfirmed;
    public event Action? StopRequested;

    public PositionRingView()
    {
        _wheelLabel = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        Add(_wheelLabel);
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        UpdateWheelText();
    }

    public override bool OnMouseEvent(MouseEvent mouseEvent)
    {
        if (mouseEvent.Flags.HasFlag(MouseFlags.Button1Clicked))
        {
            UpdateWheelText();
            if (_stopRect.Contains(mouseEvent.X, mouseEvent.Y))
            {
                StopRequested?.Invoke();
                return true;
            }

            if (TryMapPoint(mouseEvent.X, mouseEvent.Y, out var selection))
            {
                SetSelection(selection);
                SelectionConfirmed?.Invoke(selection);
                return true;
            }
        }

        return false;
    }

    public override bool ProcessKey(KeyEvent keyEvent)
    {
        switch (keyEvent.Key)
        {
            case Key.CursorLeft:
                _angleDegrees = NormalizeAngle(_angleDegrees - AngleStep);
                RaiseSelectionChanged();
                UpdateWheelText();
                return true;
            case Key.CursorRight:
                _angleDegrees = NormalizeAngle(_angleDegrees + AngleStep);
                RaiseSelectionChanged();
                UpdateWheelText();
                return true;
            case Key.CursorUp:
                _bandIndex = Math.Clamp(_bandIndex - 1, 0, Math.Max(0, RangeBands.Count - 1));
                RaiseSelectionChanged();
                UpdateWheelText();
                return true;
            case Key.CursorDown:
                _bandIndex = Math.Clamp(_bandIndex + 1, 0, Math.Max(0, RangeBands.Count - 1));
                RaiseSelectionChanged();
                UpdateWheelText();
                return true;
            case Key.Enter:
            case Key.Space:
                SelectionConfirmed?.Invoke(CreateSelection());
                return true;
            case Key.S:
                StopRequested?.Invoke();
                return true;
            default:
                return base.ProcessKey(keyEvent);
        }
    }

    private bool TryMapPoint(int x, int y, out PositionSelection selection)
    {
        selection = CreateSelection();

        UpdateWheelText();
        foreach (var slot in _slotPositions)
        {
            if (slot.Rect.Contains(x, y))
            {
                selection = new PositionSelection(slot.AngleDegrees, slot.Speed);
                return true;
            }
        }

        return false;
    }

    private void SetSelection(PositionSelection selection)
    {
        _angleDegrees = selection.AngleDegrees;
        _bandIndex = Math.Max(0, IndexOfBand(selection.RangeBand));
        RaiseSelectionChanged();
        UpdateWheelText();
    }

    private void RaiseSelectionChanged()
    {
        SelectionChanged?.Invoke(CreateSelection());
    }

    private PositionSelection CreateSelection()
    {
        var band = RangeBands.ElementAtOrDefault(_bandIndex) ?? "unknown";
        return new PositionSelection(_angleDegrees, band);
    }

    private int IndexOfBand(string band)
    {
        for (var i = 0; i < RangeBands.Count; i++)
        {
            if (string.Equals(RangeBands[i], band, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static int NormalizeAngle(int angleDegrees)
    {
        var normalized = angleDegrees % 360;
        if (normalized < 0)
        {
            normalized += 360;
        }

        return normalized;
    }

    private void UpdateWheelText()
    {
        if (!_initialized && RangeBands.Count > 0)
        {
            _bandIndex = RangeBands.Count - 1;
            _angleDegrees = 0;
            _initialized = true;
        }

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var selectedSpeed = RangeBands.ElementAtOrDefault(_bandIndex) ?? "walk";
        var selectedAngle = NormalizeAngle(_angleDegrees);

        if (Style == NavigationRingStyle.Grid)
        {
            BuildGridWheel(bounds, selectedSpeed, selectedAngle);
            return;
        }

        BuildRingWheel(bounds, selectedSpeed, selectedAngle);
    }

    private void BuildRingWheel(Rect bounds, string selectedSpeed, int selectedAngle)
    {
        _slotPositions.Clear();

        var maxWidth = Math.Min(bounds.Width, 45);
        var maxHeight = Math.Min(bounds.Height, 13);
        if (maxWidth < 40 || maxHeight < 9)
        {
            _wheelLabel.Text = "Navigation wheel needs 40x9 space.";
            _stopRect = Rect.Empty;
            return;
        }

        var grid = new char[maxHeight, maxWidth];
        for (var y = 0; y < maxHeight; y++)
        {
            for (var x = 0; x < maxWidth; x++)
            {
                grid[y, x] = ' ';
            }
        }

        var centerX = maxWidth / 2;
        var centerY = maxHeight / 2;
        var ringCount = Math.Min(3, Math.Max(1, RangeBands.Count));
        var ringStep = Math.Max(2, Math.Min(centerX, centerY) / (ringCount + 1));

        for (var ring = 1; ring <= ringCount; ring++)
        {
            var radius = ring * ringStep;
            for (var angle = 0; angle < 360; angle += 6)
            {
                var rad = angle * Math.PI / 180.0;
                var x = centerX + (int)Math.Round(Math.Sin(rad) * radius);
                var y = centerY - (int)Math.Round(Math.Cos(rad) * radius);
                if (x >= 0 && x < maxWidth && y >= 0 && y < maxHeight)
                {
                    grid[y, x] = '.';
                }
            }
        }

        var stopText = "STOP";
        PlaceText(grid, centerX - (stopText.Length / 2), centerY, stopText);
        _stopRect = new Rect(centerX - (stopText.Length / 2), centerY, stopText.Length, 1);

        foreach (var slot in Slots)
        {
            var ringIndex = SpeedToRingIndex(slot.Speed, ringCount);
            var radius = (ringIndex + 1) * ringStep;
            var angleRad = slot.AngleDegrees * Math.PI / 180.0;
            var x = centerX + (int)Math.Round(Math.Sin(angleRad) * radius);
            var y = centerY - (int)Math.Round(Math.Cos(angleRad) * radius);
            var token = slot.Token;
            if (slot.AngleDegrees == selectedAngle &&
                string.Equals(slot.Speed, selectedSpeed, StringComparison.OrdinalIgnoreCase))
            {
                token = slot.SelectedToken;
            }

            var left = x - (token.Length / 2);
            if (left < 0 || left + token.Length >= maxWidth || y < 0 || y >= maxHeight)
            {
                continue;
            }

            PlaceText(grid, left, y, token);
            _slotPositions.Add(new NavSlotPosition(new Rect(left, y, token.Length, 1), slot.AngleDegrees, slot.Speed));
        }

        var offsetX = Math.Max(0, (bounds.Width - maxWidth) / 2);
        var offsetY = Math.Max(0, (bounds.Height - maxHeight) / 2);
        _stopRect = new Rect(_stopRect.X + offsetX, _stopRect.Y + offsetY, _stopRect.Width, _stopRect.Height);
        for (var i = 0; i < _slotPositions.Count; i++)
        {
            var slot = _slotPositions[i];
            slot = slot with { Rect = new Rect(slot.Rect.X + offsetX, slot.Rect.Y + offsetY, slot.Rect.Width, slot.Rect.Height) };
            _slotPositions[i] = slot;
        }

        _wheelLabel.Text = BuildPaddedGrid(grid, offsetX, offsetY);
    }

    private void BuildGridWheel(Rect bounds, string selectedSpeed, int selectedAngle)
    {
        _slotPositions.Clear();

        var lineLength = LayoutLines.Max(line => line.Length);
        if (bounds.Width < lineLength || bounds.Height < LayoutLines.Length)
        {
            _wheelLabel.Text = "Navigation wheel needs 40x9 space.";
            _stopRect = Rect.Empty;
            return;
        }

        var lines = new string[LayoutLines.Length];
        for (var row = 0; row < LayoutLines.Length; row++)
        {
            var baseLine = LayoutLines[row];
            var line = baseLine;
            foreach (var slot in Slots)
            {
                var index = baseLine.IndexOf(slot.Token, StringComparison.Ordinal);
                if (index < 0)
                {
                    continue;
                }

                if (slot.AngleDegrees == selectedAngle &&
                    string.Equals(slot.Speed, selectedSpeed, StringComparison.OrdinalIgnoreCase))
                {
                    line = ReplaceAt(line, index, slot.SelectedToken);
                }

                _slotPositions.Add(new NavSlotPosition(new Rect(index, row, slot.Token.Length, 1), slot.AngleDegrees, slot.Speed));
            }

            lines[row] = line;
        }

        var offsetX = Math.Max(0, (bounds.Width - lineLength) / 2);
        var offsetY = Math.Max(0, (bounds.Height - lines.Length) / 2);

        var stopRow = Array.FindIndex(lines, line => line.Contains("Stop", StringComparison.Ordinal));
        if (stopRow >= 0)
        {
            var stopCol = lines[stopRow].IndexOf("Stop", StringComparison.Ordinal);
            _stopRect = new Rect(offsetX + stopCol, offsetY + stopRow, 4, 1);
        }
        else
        {
            _stopRect = Rect.Empty;
        }

        for (var i = 0; i < _slotPositions.Count; i++)
        {
            var slot = _slotPositions[i];
            slot = slot with { Rect = new Rect(slot.Rect.X + offsetX, slot.Rect.Y + offsetY, slot.Rect.Width, slot.Rect.Height) };
            _slotPositions[i] = slot;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < offsetY; i++)
        {
            builder.AppendLine();
        }

        var pad = new string(' ', offsetX);
        for (var i = 0; i < lines.Length; i++)
        {
            builder.Append(pad);
            builder.Append(lines[i]);
            if (i < lines.Length - 1)
            {
                builder.AppendLine();
            }
        }

        _wheelLabel.Text = builder.ToString();
    }

    private static string BuildPaddedGrid(char[,] grid, int offsetX, int offsetY)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < offsetY; i++)
        {
            builder.AppendLine();
        }

        var pad = new string(' ', offsetX);
        var maxHeight = grid.GetLength(0);
        var maxWidth = grid.GetLength(1);
        for (var y = 0; y < maxHeight; y++)
        {
            builder.Append(pad);
            for (var x = 0; x < maxWidth; x++)
            {
                builder.Append(grid[y, x]);
            }
            if (y < maxHeight - 1)
            {
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static void PlaceText(char[,] grid, int x, int y, string text)
    {
        var height = grid.GetLength(0);
        var width = grid.GetLength(1);
        if (y < 0 || y >= height)
        {
            return;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var px = x + i;
            if (px < 0 || px >= width)
            {
                continue;
            }

            grid[y, px] = text[i];
        }
    }

    private static int SpeedToRingIndex(string speed, int ringCount)
    {
        if (ringCount <= 1)
        {
            return 0;
        }

        if (string.Equals(speed, "run", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Min(ringCount - 1, 2);
        }

        if (string.Equals(speed, "jog", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Min(ringCount - 1, 1);
        }

        return 0;
    }

    private static string ReplaceAt(string source, int index, string replacement)
    {
        var chars = source.ToCharArray();
        for (var i = 0; i < replacement.Length && index + i < chars.Length; i++)
        {
            chars[index + i] = replacement[i];
        }

        return new string(chars);
    }

    private static IReadOnlyList<NavSlot> BuildSlots()
    {
        var slots = new List<NavSlot>();
        foreach (var direction in DirectionLabels)
        {
            var angle = DirectionToAngle(direction);
            slots.Add(new NavSlot($"{direction}:R", angle, "run"));
            slots.Add(new NavSlot($"{direction}:J", angle, "jog"));
            slots.Add(new NavSlot($"{direction}:W", angle, "walk"));
        }

        return slots;
    }

    private static int DirectionToAngle(string direction)
    {
        return direction switch
        {
            "N" => 0,
            "NE" => 45,
            "E" => 90,
            "SE" => 135,
            "S" => 180,
            "SW" => 225,
            "W" => 270,
            "NW" => 315,
            _ => 0
        };
    }
}

public readonly record struct PositionSelection(int AngleDegrees, string RangeBand);

public enum NavigationRingStyle
{
    Ring,
    Grid
}

internal readonly record struct NavSlot(string Token, int AngleDegrees, string Speed)
{
    public string SelectedToken => Token.Replace(':', '*');
}

internal record struct NavSlotPosition(Rect Rect, int AngleDegrees, string Speed);
