using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Constella.Core;

namespace Constella.Ui.Controls;

/// <summary>A round colour swatch. Clicking it opens a small picker with presets and a hex field.</summary>
internal sealed class ColorSwatch : Border
{
    private static readonly string[] Presets =
    {
        "#F1E6C8", "#FFF7E2", "#E8C77D", "#F2A65A", "#FF8A6B", "#F2B8C6",
        "#CDB8F2", "#B8DCF2", "#B8F2D9", "#A8B89A", "#A89D84", "#FFFFFF",
    };

    private readonly SolidColorBrush _fill = new(Colors.White);
    private readonly Popup _popup;
    private readonly TextBox _hex;

    public ColorSwatch()
    {
        Width = 34;
        Height = 34;
        CornerRadius = new CornerRadius(17);
        Background = _fill;
        BorderBrush = Theme.LineStrong;
        BorderThickness = new Thickness(2);
        Cursor = Cursors.Hand;
        HorizontalAlignment = HorizontalAlignment.Left;

        _hex = ThemedTextBox.Create(96);
        _hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ApplyHex();
                e.Handled = true;
            }
        };
        _popup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 8,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            Child = BuildPicker(),
        };
    }

    /// <summary>Raised when the user picks a colour.</summary>
    public event Action<Color>? Picked;

    public Color Color
    {
        get => _fill.Color;
        set => Theme.FadeTo(_fill, value);
    }

    protected override void OnMouseEnter(MouseEventArgs e) => BorderBrush = Theme.Muted;

    protected override void OnMouseLeave(MouseEventArgs e) => BorderBrush = Theme.LineStrong;

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _hex.Text = ColorUtil.ToHex(Color);
        _popup.IsOpen = true;
        e.Handled = true;
    }

    private Border BuildPicker()
    {
        var grid = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 12) };
        foreach (string preset in Presets)
        {
            var color = Theme.Hex(preset);
            var dot = new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(color),
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = preset,
            };
            dot.MouseEnter += (_, _) => dot.BorderBrush = Theme.Text;
            dot.MouseLeave += (_, _) => dot.BorderBrush = Theme.Line;
            dot.MouseLeftButtonUp += (_, _) => Choose(color);
            grid.Children.Add(dot);
        }

        var apply = Theme.Label("Apply", 12, Theme.Accent);
        apply.Cursor = Cursors.Hand;
        apply.Margin = new Thickness(12, 0, 0, 0);
        apply.MouseLeftButtonUp += (_, _) => ApplyHex();

        var hexRow = new StackPanel { Orientation = Orientation.Horizontal };
        hexRow.Children.Add(_hex);
        hexRow.Children.Add(apply);

        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(hexRow);
        return new Border
        {
            Background = Theme.Raised,
            BorderBrush = Theme.LineStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Child = stack,
        };
    }

    private void ApplyHex()
    {
        if (ColorUtil.TryParseHex(_hex.Text, out var color))
        {
            Choose(color);
        }
        else
        {
            _hex.BorderBrush = Theme.Danger;
        }
    }

    private void Choose(Color color)
    {
        Color = color;
        _popup.IsOpen = false;
        Picked?.Invoke(color);
    }
}
