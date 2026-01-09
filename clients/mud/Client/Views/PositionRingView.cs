using Terminal.Gui;

namespace WodMudClient;

public sealed class PositionRingView : View
{
    private int _angleDegrees;
    private int _bandIndex;

    public bool HasTarget { get; set; }
    public IReadOnlyList<string> RangeBands { get; set; } = Array.Empty<string>();
    public int AngleStep { get; set; } = 15;

    public event Action<PositionSelection>? SelectionChanged;
    public event Action<PositionSelection>? SelectionConfirmed;

    public override void Redraw(Rect bounds)
    {
        Clear(bounds);

        var driver = Application.Driver;
        var width = bounds.Width;
        var height = bounds.Height;
        if (width <= 2 || height <= 2)
        {
            return;
        }

        var centerX = width / 2;
        var centerY = height / 2;
        var maxRadius = Math.Min(width, height) / 2 - 1;
        if (maxRadius <= 0)
        {
            return;
        }

        if (!HasTarget)
        {
            var text = "No target";
            driver.Move(0, 0);
            driver.AddStr(text);
            return;
        }

        var bandCount = Math.Max(1, RangeBands.Count);
        var innerRadius = Math.Max(1, maxRadius / 3);
        var ringThickness = Math.Max(1, maxRadius - innerRadius + 1);
        var bandWidth = Math.Max(1, ringThickness / bandCount);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var dx = x - centerX;
                var dy = y - centerY;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < innerRadius || dist > maxRadius)
                {
                    continue;
                }

                driver.Move(x, y);
                driver.AddRune('.');
            }
        }

        driver.Move(centerX, centerY);
        driver.AddRune('O');

        var northY = centerY - maxRadius;
        if (northY >= 0)
        {
            driver.Move(centerX, northY);
            driver.AddRune('^');
        }

        var selectedRadius = innerRadius + (_bandIndex * bandWidth) + (bandWidth / 2.0);
        var angleRad = _angleDegrees * Math.PI / 180.0;
        var markerX = centerX + (int)Math.Round(Math.Sin(angleRad) * selectedRadius);
        var markerY = centerY - (int)Math.Round(Math.Cos(angleRad) * selectedRadius);
        if (markerX >= 0 && markerX < width && markerY >= 0 && markerY < height)
        {
            driver.Move(markerX, markerY);
            driver.AddRune('X');
        }
    }

    public override bool OnMouseEvent(MouseEvent mouseEvent)
    {
        if (!HasTarget)
        {
            return false;
        }

        if (mouseEvent.Flags.HasFlag(MouseFlags.Button1Clicked))
        {
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
        if (!HasTarget)
        {
            return base.ProcessKey(keyEvent);
        }

        switch (keyEvent.Key)
        {
            case Key.CursorLeft:
                _angleDegrees = NormalizeAngle(_angleDegrees - AngleStep);
                RaiseSelectionChanged();
                SetNeedsDisplay();
                return true;
            case Key.CursorRight:
                _angleDegrees = NormalizeAngle(_angleDegrees + AngleStep);
                RaiseSelectionChanged();
                SetNeedsDisplay();
                return true;
            case Key.CursorUp:
                _bandIndex = Math.Clamp(_bandIndex - 1, 0, Math.Max(0, RangeBands.Count - 1));
                RaiseSelectionChanged();
                SetNeedsDisplay();
                return true;
            case Key.CursorDown:
                _bandIndex = Math.Clamp(_bandIndex + 1, 0, Math.Max(0, RangeBands.Count - 1));
                RaiseSelectionChanged();
                SetNeedsDisplay();
                return true;
            case Key.Enter:
            case Key.Space:
                SelectionConfirmed?.Invoke(CreateSelection());
                return true;
            default:
                return base.ProcessKey(keyEvent);
        }
    }

    private bool TryMapPoint(int x, int y, out PositionSelection selection)
    {
        selection = CreateSelection();

        var width = Bounds.Width;
        var height = Bounds.Height;
        var centerX = width / 2;
        var centerY = height / 2;
        var maxRadius = Math.Min(width, height) / 2 - 1;
        if (maxRadius <= 0)
        {
            return false;
        }

        var dx = x - centerX;
        var dy = y - centerY;
        var dist = Math.Sqrt(dx * dx + dy * dy);

        var bandCount = Math.Max(1, RangeBands.Count);
        var innerRadius = Math.Max(1, maxRadius / 3);
        if (dist < innerRadius || dist > maxRadius)
        {
            return false;
        }

        var ringThickness = Math.Max(1, maxRadius - innerRadius + 1);
        var bandWidth = Math.Max(1, ringThickness / bandCount);
        var bandIndex = (int)Math.Floor((dist - innerRadius) / bandWidth);
        bandIndex = Math.Clamp(bandIndex, 0, bandCount - 1);

        var angleRad = Math.Atan2(dx, -dy);
        var angleDegrees = (int)Math.Round(angleRad * 180.0 / Math.PI);
        angleDegrees = NormalizeAngle(angleDegrees);

        selection = new PositionSelection(angleDegrees, RangeBands.ElementAtOrDefault(bandIndex) ?? "unknown");
        return true;
    }

    private void SetSelection(PositionSelection selection)
    {
        _angleDegrees = selection.AngleDegrees;
        _bandIndex = Math.Max(0, IndexOfBand(selection.RangeBand));
        RaiseSelectionChanged();
        SetNeedsDisplay();
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
}

public readonly record struct PositionSelection(int AngleDegrees, string RangeBand);
