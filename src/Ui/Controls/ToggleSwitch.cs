using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Constella.Core;

namespace Constella.Ui.Controls;

/// <summary>An on/off switch. Click or press Space to flip it.</summary>
internal sealed class ToggleSwitch : FrameworkElement
{
    private static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(ToggleSwitch),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private bool _isOn;

    public ToggleSwitch()
    {
        Width = 40;
        Height = 22;
        Cursor = Cursors.Hand;
        Focusable = true;
        FocusVisualStyle = null;
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>Raised when the user flips the switch (not when <see cref="IsOn"/> is set in code).</summary>
    public event Action<bool>? Toggled;

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (value == _isOn)
            {
                return;
            }
            _isOn = value;
            double target = value ? 1 : 0;
            if (Theme.MotionEnabled && IsLoaded)
            {
                BeginAnimation(ProgressProperty, new DoubleAnimation(target, Theme.Smooth) { EasingFunction = Theme.EaseOut });
            }
            else
            {
                BeginAnimation(ProgressProperty, null);
                Progress = target;
            }
        }
    }

    private double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        Flip();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Space or Key.Enter)
        {
            Flip();
            e.Handled = true;
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e) => InvalidateVisual();

    protected override void OnMouseLeave(MouseEventArgs e) => InvalidateVisual();

    protected override void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        double p = Progress;
        double w = ActualWidth, h = ActualHeight, r = h / 2;
        var track = new Rect(0.5, 0.5, w - 1, h - 1);

        var fill = new SolidColorBrush(Ease.Mix(Theme.RaisedColor, Theme.AccentColor, p));
        var border = new Pen(new SolidColorBrush(Ease.Mix(IsMouseOver ? Theme.FaintColor : Theme.LineStrongColor, Theme.AccentColor, p)), 1);
        dc.DrawRoundedRectangle(fill, border, track, r - 0.5, r - 0.5);

        if (IsKeyboardFocused)
        {
            var ring = new Rect(-2.5, -2.5, w + 5, h + 5);
            dc.DrawRoundedRectangle(null, new Pen(Theme.Accent, 1), ring, r + 2.5, r + 2.5);
        }

        double knob = r - 4;
        double x = Ease.Lerp(r, w - r, p);
        var knobBrush = new SolidColorBrush(Ease.Mix(Theme.MutedColor, Theme.BgColor, p));
        dc.DrawEllipse(knobBrush, null, new Point(x, r), knob, knob);
    }

    private void Flip()
    {
        IsOn = !IsOn;
        Toggled?.Invoke(IsOn);
    }
}
