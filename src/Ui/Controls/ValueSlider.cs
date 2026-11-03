using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Constella.Core;

namespace Constella.Ui.Controls;

/// <summary>A minimal slider: thin track, accent fill, round thumb. Drag, click, or use the arrow keys.</summary>
internal sealed class ValueSlider : FrameworkElement
{
    private const double ThumbRadius = 7;
    private double _value;

    public ValueSlider(double minimum, double maximum, double step)
    {
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        Height = 26;
        MinWidth = 120;
        Cursor = Cursors.Hand;
        Focusable = true;
        FocusVisualStyle = null;
    }

    /// <summary>Raised when the user changes the value.</summary>
    public event Action<double>? ValueChanged;

    public double Minimum { get; }

    public double Maximum { get; }

    public double Step { get; }

    public double Value
    {
        get => _value;
        set
        {
            double snapped = Math.Round(Math.Clamp(value, Minimum, Maximum) / Step) * Step;
            if (Math.Abs(snapped - _value) > 1e-9)
            {
                _value = snapped;
                InvalidateVisual();
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        CaptureMouse();
        SetFromPointer(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured)
        {
            SetFromPointer(e.GetPosition(this).X);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double delta = e.Key switch
        {
            Key.Left or Key.Down => -Step,
            Key.Right or Key.Up => Step,
            _ => 0,
        };
        if (delta != 0)
        {
            SetByUser(Value + delta);
            e.Handled = true;
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e) => InvalidateVisual();

    protected override void OnMouseLeave(MouseEventArgs e) => InvalidateVisual();

    protected override void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, mid = ActualHeight / 2;
        double left = ThumbRadius + 1, right = w - ThumbRadius - 1;
        double fraction = Maximum > Minimum ? (Value - Minimum) / (Maximum - Minimum) : 0;
        double x = Ease.Lerp(left, right, fraction);

        // Transparent background so the whole strip is clickable, not just the thin line.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        dc.DrawRoundedRectangle(Theme.LineStrong, null, new Rect(left, mid - 1, right - left, 2), 1, 1);
        dc.DrawRoundedRectangle(Theme.Accent, null, new Rect(left, mid - 1, Math.Max(0, x - left), 2), 1, 1);

        bool active = IsMouseOver || IsMouseCaptured;
        double radius = active ? ThumbRadius + 1 : ThumbRadius;
        if (IsKeyboardFocused)
        {
            dc.DrawEllipse(null, new Pen(Theme.Accent, 1), new Point(x, mid), radius + 3, radius + 3);
        }
        dc.DrawEllipse(Theme.Text, new Pen(Theme.Bg, 2), new Point(x, mid), radius, radius);
    }

    private void SetFromPointer(double pointerX)
    {
        double left = ThumbRadius + 1, right = ActualWidth - ThumbRadius - 1;
        double fraction = right > left ? Ease.Clamp01((pointerX - left) / (right - left)) : 0;
        SetByUser(Minimum + fraction * (Maximum - Minimum));
    }

    private void SetByUser(double value)
    {
        double before = _value;
        Value = value;
        if (Math.Abs(before - _value) > 1e-9)
        {
            ValueChanged?.Invoke(_value);
        }
    }
}
