using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Constella.Core;

namespace Constella.Overlay;

/// <summary>A small message pill near the bottom of the screen (e.g. "Lyrics 0.2 s earlier"). Fades away on its own.</summary>
internal sealed class Toast : FrameworkElement
{
    private const double FadeIn = 0.15;
    private const double DefaultHold = 1.6;
    private const double FadeOut = 0.45;

    private static readonly Brush Fill = Frozen(Color.FromArgb(200, 18, 18, 20));
    private static readonly Brush Outline = Frozen(Color.FromArgb(60, 255, 255, 255));
    private static readonly Brush TextBrush = Frozen(Color.FromRgb(0xF4, 0xF4, 0xF6));

    private string _message = string.Empty;
    private double _shownAt = double.NegativeInfinity;
    private double _alpha;
    private double _hold = DefaultHold;

    public Toast() => IsHitTestVisible = false;

    /// <param name="holdSeconds">How long it stays fully visible before fading.</param>
    public void Show(string message, double wall, double holdSeconds = DefaultHold)
    {
        _message = message;
        _hold = holdSeconds;
        _shownAt = wall;
        InvalidateVisual();
    }

    /// <returns>True while the toast is visible.</returns>
    public bool Update(double wall)
    {
        double age = wall - _shownAt;
        double alpha = Ease.OutCubic(Ease.Progress(age, 0, FadeIn)) * (1 - Ease.InOutSine(Ease.Progress(age, FadeIn + _hold, FadeOut)));
        if (Math.Abs(alpha - _alpha) > 0.002)
        {
            _alpha = alpha;
            InvalidateVisual();
        }
        return alpha > 0.002;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_alpha <= 0.002 || ActualHeight < 1)
        {
            return;
        }
        double unit = ActualHeight / 1000.0;
        var text = new FormattedText(_message, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            15 * unit, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double padX = 16 * unit, padY = 8 * unit;
        var pill = new Rect(
            ActualWidth / 2 - text.Width / 2 - padX, ActualHeight * 0.86,
            text.Width + 2 * padX, text.Height + 2 * padY);

        dc.PushOpacity(_alpha);
        dc.DrawRoundedRectangle(Fill, new Pen(Outline, 1), pill, pill.Height / 2, pill.Height / 2);
        dc.DrawText(text, new Point(pill.X + padX, pill.Y + padY));
        dc.Pop();
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
