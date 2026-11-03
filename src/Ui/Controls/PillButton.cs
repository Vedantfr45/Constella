using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Constella.Ui.Controls;

/// <summary>Rounded button: solid accent (primary) or outlined (ghost), with an optional key-cap hint.</summary>
internal sealed class PillButton : Border
{
    private readonly bool _primary;
    private readonly SolidColorBrush _background;
    private readonly TextBlock _label;
    private readonly TextBlock _keycapText;
    private readonly Border _keycap;
    private readonly ScaleTransform _press = new(1, 1);

    /// <summary>True between a mouse press on this button and its release.</summary>
    private bool _pressed;

    public PillButton(string text, bool primary, string? keycap = null)
    {
        _primary = primary;
        _background = new SolidColorBrush(primary ? Theme.AccentColor : Colors.Transparent);
        Background = _background;
        BorderBrush = primary ? Brushes.Transparent : Theme.LineStrong;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(999);
        Padding = new Thickness(18, 8, keycap is null ? 18 : 8, 9);
        Cursor = Cursors.Hand;
        Focusable = true;
        FocusVisualStyle = null;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _press;

        _label = Theme.Label(text, 13, primary ? Theme.Bg : Theme.Text);
        _label.FontWeight = FontWeights.SemiBold;
        _keycapText = Theme.Label(keycap ?? string.Empty, 11, primary ? Theme.Bg : Theme.Muted, Theme.Mono);
        _keycap = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 3),
            Margin = new Thickness(10, 0, 0, 0),
            Child = _keycapText,
            Visibility = keycap is null ? Visibility.Collapsed : Visibility.Visible,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_label);
        row.Children.Add(_keycap);
        Child = row;
    }

    public event Action? Click;

    public string Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    public string Keycap
    {
        set => _keycapText.Text = value;
    }

    protected override void OnMouseEnter(MouseEventArgs e) =>
        Theme.FadeTo(_background, _primary ? Theme.AccentHoverColor : Theme.RaisedColor);

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        Theme.FadeTo(_background, _primary ? Theme.AccentColor : Colors.Transparent);
        Press(false);
        _pressed = false;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Press(true);
        _pressed = true;
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        Press(false);
        // Only a click that also started here counts: a stray button release (e.g. from the click
        // that closed the lock screen) must not press Quit.
        if (_pressed && IsMouseOver)
        {
            Click?.Invoke();
        }
        _pressed = false;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Space or Key.Enter)
        {
            Click?.Invoke();
            e.Handled = true;
        }
    }

    private void Press(bool down)
    {
        double scale = down ? 0.97 : 1;
        if (!Theme.MotionEnabled)
        {
            _press.ScaleX = _press.ScaleY = scale;
            return;
        }
        var animation = new DoubleAnimation(scale, Theme.Quick) { EasingFunction = Theme.EaseOut };
        _press.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        _press.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
}
