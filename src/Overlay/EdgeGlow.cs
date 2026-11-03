using System;
using System.Windows;
using System.Windows.Media;
using Constella.Core;

namespace Constella.Overlay;

/// <summary>
/// Light around the screen edges, in one of three motions:
/// <list type="bullet">
/// <item>Run: a snake of light chasing around the border (see <see cref="SnakeRim"/>), redrawn at 30 fps.</item>
/// <item>Breathe: a steady soft glow on all four edges whose opacity follows the music's loudness.</item>
/// <item>Still: the same glow, unchanging.</item>
/// </list>
/// Breathe and Still are four mitred trapezoids with a gradient fading inward, built once per size
/// and colour; the breathing only changes <see cref="UIElement.Opacity"/>, which is nearly free.
/// </summary>
internal sealed class EdgeGlow : FrameworkElement
{
    /// <summary>Breathe/Still glow depth as a fraction of the shorter screen side.</summary>
    private const double DepthFraction = 0.06;

    /// <summary>The running light is redrawn at most this often (it's smooth at 30 fps and costs half as much).</summary>
    private const double RunFrameSeconds = 1 / 30.0;

    /// <summary>Longest time step applied in one frame, so a stall doesn't make the light jump.</summary>
    private const double MaxStepSeconds = 0.1;

    /// <summary>(offset into the glow, brightness): bright rim with a long soft falloff.</summary>
    private static readonly (double Offset, double Alpha)[] Falloff =
    {
        (0, 1), (0.06, 0.7), (0.2, 0.34), (0.45, 0.12), (0.75, 0.03), (1, 0),
    };

    private (Geometry Shape, Brush Fill)[] _edges = Array.Empty<(Geometry, Brush)>();
    private Size _edgesFor;
    private Color _edgesColor;
    private SnakeRim? _snake;
    private double _snakeDrawnAt = double.NegativeInfinity;
    private Color _color = Colors.Wheat;
    private bool _enabled = true;
    private GlowMode _mode = GlowMode.Run;
    private double _speed = 5;
    private double _intensity = 0.9;
    private Color? _customColor;

    public EdgeGlow()
    {
        IsHitTestVisible = false;
        // The snake's strips are drawn 1:1 onto device pixels; no smoothing, so they meet exactly.
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        // Aliased edges make the mitred trapezoids meet without faint seams.
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    /// <summary>True when the glow needs the music's loudness (Breathe mode), so the meter can stay off otherwise.</summary>
    public bool NeedsLoudness => _enabled && _mode == GlowMode.Breathe;

    public void ApplySettings(AppSettings settings)
    {
        _enabled = settings.EdgeGlow;
        _speed = settings.GlowSpeed;
        _intensity = settings.GlowIntensity;
        _customColor = settings.GlowColor == AppSettings.GlowMatchesLyrics
            ? null
            : ColorUtil.ParseHexOr(settings.GlowColor, Colors.Red);
        if (settings.GlowMode != _mode)
        {
            _mode = settings.GlowMode;
            InvalidateVisual();
        }
        Visibility = _enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Advances the glow by one frame.</summary>
    /// <param name="wall">Seconds on the app clock.</param>
    /// <param name="lyricColor">Used when the glow follows the lyric colour.</param>
    /// <param name="loudness">0..1 music loudness (only used in Breathe mode).</param>
    public void Tick(double wall, Color lyricColor, double loudness)
    {
        if (!_enabled || ActualWidth < 1 || ActualHeight < 1)
        {
            return;
        }
        var color = _customColor ?? lyricColor;
        if (color != _color)
        {
            _color = color;
            if (_mode != GlowMode.Run)
            {
                InvalidateVisual();
            }
        }

        if (_mode == GlowMode.Run)
        {
            Opacity = 1; // Brightness is baked into the snake's pixels.
            RunSnake(wall);
        }
        else
        {
            Opacity = _mode == GlowMode.Breathe ? _intensity * (0.3 + 0.7 * loudness) : _intensity;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_mode == GlowMode.Run)
        {
            _snake?.Draw(dc);
            return;
        }

        var size = new Size(ActualWidth, ActualHeight);
        if (size.Width < 1 || size.Height < 1)
        {
            return;
        }
        if (size != _edgesFor || _color != _edgesColor)
        {
            BuildEdges(size);
        }
        foreach (var (shape, fill) in _edges)
        {
            dc.DrawGeometry(fill, null, shape);
        }
    }

    /// <summary>Moves the snake on (at most 30 times a second) and rebuilds it when the screen changes.</summary>
    private void RunSnake(double wall)
    {
        double step = wall - _snakeDrawnAt;
        if (step < RunFrameSeconds)
        {
            return;
        }
        _snakeDrawnAt = wall;

        var dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        if (_snake is null || !_snake.IsFor(width, height, dpi.DpiScaleX, dpi.DpiScaleY))
        {
            _snake = new SnakeRim(width, height, dpi.DpiScaleX, dpi.DpiScaleY, _snake?.HeadFraction ?? 0);
            step = 0;
            InvalidateVisual(); // New bitmaps to draw; after this, updating their pixels is enough.
        }
        _snake.SetLook(_color, Warmer(_color), _intensity);
        _snake.Advance(Math.Min(step, MaxStepSeconds), _speed);
    }

    /// <summary>
    /// The colour the light warms to further from the edge: a deeper, slightly richer version of the
    /// edge colour (cream becomes amber, bright red becomes deep red).
    /// </summary>
    private static Color Warmer(Color edge)
    {
        var (hue, saturation, lightness) = ColorUtil.ToHsl(edge);
        return ColorUtil.FromHsl(hue, Math.Min(1, saturation * 1.05), lightness * 0.62);
    }

    /// <summary>The four mitred trapezoids for Breathe and Still, each with a gradient from the edge inward.</summary>
    private void BuildEdges(Size size)
    {
        _edgesFor = size;
        _edgesColor = _color;
        double w = size.Width, h = size.Height;
        double depth = Math.Min(w, h) * DepthFraction;
        // The outermost pixels are a touch whiter, like light hitting the bezel.
        Color rim = Ease.Mix(_color, Colors.White, 0.35);

        // Each edge: outer corners clockwise, and the inward direction.
        var sides = new (Point A, Point B, Vector Inward)[]
        {
            (new Point(0, 0), new Point(w, 0), new Vector(0, 1)),
            (new Point(w, 0), new Point(w, h), new Vector(-1, 0)),
            (new Point(w, h), new Point(0, h), new Vector(0, -1)),
            (new Point(0, h), new Point(0, 0), new Vector(1, 0)),
        };
        _edges = new (Geometry, Brush)[sides.Length];
        for (int s = 0; s < sides.Length; s++)
        {
            var (a, b, inward) = sides[s];
            var along = b - a;
            along.Normalize();
            // Inner corners sit on the 45° mitres.
            var innerA = a + inward * depth + along * depth;
            var innerB = b + inward * depth - along * depth;

            var shape = new StreamGeometry();
            using (var ctx = shape.Open())
            {
                ctx.BeginFigure(a, isFilled: true, isClosed: true);
                ctx.LineTo(b, isStroked: false, isSmoothJoin: false);
                ctx.LineTo(innerB, isStroked: false, isSmoothJoin: false);
                ctx.LineTo(innerA, isStroked: false, isSmoothJoin: false);
            }
            shape.Freeze();

            var fill = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = a,
                EndPoint = a + inward * depth,
            };
            foreach (var (offset, alpha) in Falloff)
            {
                var color = offset < 0.1 ? Ease.Mix(rim, _color, offset / 0.1) : _color;
                fill.GradientStops.Add(new GradientStop(Ease.WithAlpha(color, alpha), offset));
            }
            fill.Freeze();
            _edges[s] = (shape, fill);
        }
    }
}
