using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Constella.Ui.Controls;

/// <summary>A row of pill-shaped options where exactly one is selected (like radio buttons).</summary>
internal sealed class ChipGroup : WrapPanel
{
    private readonly List<Chip> _chips = new();
    private object? _selected;

    /// <summary>Raised when the user picks an option.</summary>
    public event Action<object>? SelectionChanged;

    public object? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            foreach (var chip in _chips)
            {
                chip.Restyle(Equals(chip.Key, value));
            }
        }
    }

    /// <param name="font">Optional font for the label, e.g. to preview a font choice.</param>
    public void Add(object key, string label, FontFamily? font = null)
    {
        var chip = new Chip(key, label, font);
        chip.Picked += () =>
        {
            if (!Equals(_selected, chip.Key))
            {
                Selected = chip.Key;
                SelectionChanged?.Invoke(chip.Key);
            }
        };
        chip.Restyle(Equals(key, _selected));
        _chips.Add(chip);
        Children.Add(chip);
    }

    public void Clear()
    {
        _chips.Clear();
        Children.Clear();
    }

    private sealed class Chip : Border
    {
        private readonly SolidColorBrush _background = new(Colors.Transparent);
        private readonly SolidColorBrush _border = new(Theme.LineColor);
        private readonly SolidColorBrush _foreground = new(Theme.MutedColor);
        private bool _isSelected;

        public Chip(object key, string label, FontFamily? font)
        {
            Key = key;
            CornerRadius = new CornerRadius(999);
            BorderThickness = new Thickness(1);
            Padding = new Thickness(14, 6, 14, 7);
            Margin = new Thickness(0, 0, 8, 8);
            Cursor = Cursors.Hand;
            Focusable = true;
            FocusVisualStyle = null;
            Background = _background;
            BorderBrush = _border;
            var text = Theme.Label(label, 13, _foreground, font);
            text.FontWeight = FontWeights.Medium;
            Child = text;
        }

        public event Action? Picked;

        public object Key { get; }

        public void Restyle(bool selected)
        {
            _isSelected = selected;
            Theme.FadeTo(_background, selected ? Theme.AccentSoftColor : Colors.Transparent);
            Theme.FadeTo(_border, selected ? Theme.AccentColor : IsMouseOver ? Theme.FaintColor : Theme.LineColor);
            Theme.FadeTo(_foreground, selected ? Theme.TextColor : IsMouseOver ? Theme.TextColor : Theme.MutedColor);
        }

        protected override void OnMouseEnter(MouseEventArgs e) => Restyle(_isSelected);

        protected override void OnMouseLeave(MouseEventArgs e) => Restyle(_isSelected);

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            Picked?.Invoke();
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key is System.Windows.Input.Key.Space or System.Windows.Input.Key.Enter)
            {
                Picked?.Invoke();
                e.Handled = true;
            }
        }

        protected override void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e) =>
            Theme.FadeTo(_border, IsKeyboardFocused || _isSelected ? Theme.AccentColor : Theme.LineColor);
    }
}
