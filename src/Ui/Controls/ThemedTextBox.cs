using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Constella.Ui.Controls;

/// <summary>Builds a TextBox with a flat dark template (the default one has a blue hover border).</summary>
internal static class ThemedTextBox
{
    public static TextBox Create(double width)
    {
        var box = new TextBox
        {
            Width = width,
            Background = Theme.Bg,
            Foreground = Theme.Text,
            BorderBrush = Theme.LineStrong,
            BorderThickness = new Thickness(1),
            CaretBrush = Theme.Accent,
            SelectionBrush = Theme.Accent,
            FontFamily = Theme.Mono,
            FontSize = 12,
            Template = CreateTemplate(),
        };
        box.GotKeyboardFocus += (_, _) => box.BorderBrush = Theme.Accent;
        box.LostKeyboardFocus += (_, _) => box.BorderBrush = Theme.LineStrong;
        InputMethod.SetIsInputMethodEnabled(box, false);
        return box;
    }

    private static ControlTemplate CreateTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty, TemplateBinding(Control.BackgroundProperty));
        border.SetBinding(Border.BorderBrushProperty, TemplateBinding(Control.BorderBrushProperty));
        border.SetBinding(Border.BorderThicknessProperty, TemplateBinding(Control.BorderThicknessProperty));

        // WPF puts the editable text inside the element named PART_ContentHost.
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 5, 8, 5));
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        border.AppendChild(host);

        var template = new ControlTemplate(typeof(TextBox)) { VisualTree = border };
        template.Seal();
        return template;
    }

    private static Binding TemplateBinding(DependencyProperty property) =>
        new(property.Name) { RelativeSource = RelativeSource.TemplatedParent };
}
