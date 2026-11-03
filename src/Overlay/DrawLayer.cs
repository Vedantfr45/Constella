using System;
using System.Windows;
using System.Windows.Media;

namespace Constella.Overlay;

/// <summary>A full-size element whose content is drawn by a callback each time it is invalidated.</summary>
internal sealed class DrawLayer : FrameworkElement
{
    private readonly Action<DrawingContext> _draw;

    public DrawLayer(Action<DrawingContext> draw)
    {
        _draw = draw;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawingContext) => _draw(drawingContext);
}
