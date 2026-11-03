using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Constella.Overlay;

/// <summary>
/// The "Run" edge light: a snake of light chasing clockwise around the screen border, drawn per
/// pixel the same way as Rimic's overlay so the two look identical.
/// <para>
/// Geometry: every pixel near the border has a distance d to the nearest edge (square corners,
/// mitred diagonally) and a perimeter position t taken from that edge (ties go top, right,
/// bottom, left), clockwise from the top-left corner. Brightness across the border follows
/// alpha(d) = min(1, 0.95·core + 0.55·tail); along the border the snake's tail fades out with a
/// smoothstep over 30% of the perimeter, and the head is rounded off just ahead of it.
/// </para>
/// <para>
/// Cost: four strip bitmaps (top, bottom, left, right) with d and t precomputed per pixel, and a
/// table of finished premultiplied pixels for 257 tail levels × every d, so a frame is one table
/// lookup per pixel. Strips the snake doesn't touch are skipped.
/// </para>
/// </summary>
internal sealed class SnakeRim
{
    /// <summary>Rimic's sizes are for a 1080-pixel-tall screen; everything scales from there.</summary>
    private const double ReferenceHeight = 1080;

    private const double Thickness = 14;      // th: core thickness
    private const double Reach = 15;          // rc: how far the soft tail reaches inward
    private const double HeadSize = 10;       // hr: length of the rounded head
    private const double ReachSpan = 3.2;     // maxD = th + 3.2·rc
    private const double CoreFalloff = 1.4;
    private const double CoreWeight = 0.95;
    private const double TailWeight = 0.55;
    private const double TailStart = 0.5;     // the tail starts half a core thickness in
    private const double HeadReach = 1.5;     // head pixels end 1.5 head sizes ahead
    private const double HeadStretch = 1.2;
    private const double Cutoff = 0.004;      // dimmer pixels are left off

    /// <summary>Snake length as a share of the perimeter.</summary>
    private const double LengthShare = 0.30;

    /// <summary>Head speed: this share of the perimeter per second for each unit of the speed setting.</summary>
    private const double RunRate = 0.025;

    /// <summary>Tail brightness steps in the lookup table (0 = off … 256 = full).</summary>
    private const int Levels = 256;

    /// <summary>One rectangle of border pixels.</summary>
    /// <param name="Rect">Device pixels, screen-local.</param>
    /// <param name="Distance">Each pixel's d, row by row.</param>
    /// <param name="Position">Each pixel's t, row by row.</param>
    /// <param name="Used">The distinct t values in the strip (to tell quickly if the snake touches it).</param>
    private sealed record Strip(Int32Rect Rect, int[] Distance, int[] Position, int[] Used, WriteableBitmap Bitmap, uint[] Pixels)
    {
        /// <summary>True while the bitmap holds lit pixels (so it needs clearing once the snake leaves).</summary>
        public bool Lit { get; set; }
    }

    private readonly int _width;
    private readonly int _height;
    private readonly int _depth;
    private readonly int _perimeter;
    private readonly double _th;
    private readonly double _rc;
    private readonly double _maxD;
    private readonly double _headPx;
    private readonly double _dpiScaleX;
    private readonly double _dpiScaleY;
    private readonly Strip[] _strips;
    private readonly int[] _level;
    private readonly double[] _ahead;
    private uint[] _table = Array.Empty<uint>();
    private Color _edge;
    private Color _warm;
    private double _brightness = -1;
    private double _head;

    /// <param name="width">Screen width in device pixels.</param>
    /// <param name="height">Screen height in device pixels.</param>
    /// <param name="dpiScaleX">Device pixels per device-independent pixel.</param>
    /// <param name="headFraction">Where the head starts, 0..1 around the screen (kept across rebuilds).</param>
    public SnakeRim(int width, int height, double dpiScaleX, double dpiScaleY, double headFraction)
    {
        _width = width;
        _height = height;
        _dpiScaleX = dpiScaleX;
        _dpiScaleY = dpiScaleY;
        _perimeter = 2 * (width + height);
        double scale = height / ReferenceHeight;
        _th = Thickness * scale;
        _rc = Reach * scale;
        _maxD = _th + ReachSpan * _rc;
        _headPx = HeadSize * scale;
        _depth = Math.Min(Margin(_maxD, Math.Max(dpiScaleX, dpiScaleY)), Math.Min(width, height) / 2);
        _head = headFraction * _perimeter;
        _level = new int[_perimeter];
        _ahead = new double[_perimeter];

        int m = _depth;
        _strips = new[]
        {
            BuildStrip(new Int32Rect(0, 0, width, m)),
            BuildStrip(new Int32Rect(0, height - m, width, m)),
            BuildStrip(new Int32Rect(0, m, m, height - 2 * m)),
            BuildStrip(new Int32Rect(width - m, m, m, height - 2 * m)),
        };
    }

    /// <summary>Where the head is, 0..1 around the screen.</summary>
    public double HeadFraction => _head / _perimeter;

    public bool IsFor(int width, int height, double dpiScaleX, double dpiScaleY) =>
        width == _width && height == _height && dpiScaleX == _dpiScaleX && dpiScaleY == _dpiScaleY;

    /// <summary>Colours and brightness; the lookup table is rebuilt only when they change.</summary>
    /// <param name="edge">Colour at the very edge of the screen.</param>
    /// <param name="warm">Colour the light warms to further in.</param>
    /// <param name="brightness">0..1, multiplies every pixel's alpha.</param>
    public void SetLook(Color edge, Color warm, double brightness)
    {
        if (edge == _edge && warm == _warm && brightness == _brightness && _table.Length > 0)
        {
            return;
        }
        _edge = edge;
        _warm = warm;
        _brightness = brightness;
        _table = new uint[(Levels + 1) * _depth];
        for (int d = 0; d < _depth; d++)
        {
            double alpha = Alpha(d);
            for (int level = 0; level <= Levels; level++)
            {
                _table[level * _depth + d] = Pack(d, alpha * level / Levels);
            }
        }
    }

    /// <summary>Moves the head on and redraws the strips.</summary>
    /// <param name="step">Seconds since the last frame (already clamped by the caller).</param>
    /// <param name="speed">The speed setting (Rimic's default is 5).</param>
    public void Advance(double step, double speed)
    {
        _head = (_head + speed * RunRate * _perimeter * step) % _perimeter;
        Render();
    }

    /// <summary>Draws the strips 1:1 onto device pixels.</summary>
    public void Draw(DrawingContext dc)
    {
        foreach (var strip in _strips)
        {
            var r = strip.Rect;
            dc.DrawImage(strip.Bitmap, new Rect(r.X / _dpiScaleX, r.Y / _dpiScaleY, r.Width / _dpiScaleX, r.Height / _dpiScaleY));
        }
    }

    /// <summary>Fills every strip the snake touches, and clears any it has just left.</summary>
    private void Render()
    {
        double length = LengthShare * _perimeter;
        double headEnd = HeadReach * _headPx;
        for (int t = 0; t < _perimeter; t++)
        {
            // Signed distance ahead of the head, wrapped into [-P/2, P/2).
            double ahead = Mod(t - _head + _perimeter / 2.0, _perimeter) - _perimeter / 2.0;
            double behind = -ahead;
            int level = 0;
            if (ahead <= 0 && behind < length)
            {
                double q = 1 - behind / length;
                level = (int)Math.Round(q * q * (3 - 2 * q) * Levels);
            }
            _level[t] = level;
            _ahead[t] = ahead > 0 && ahead <= headEnd ? ahead : 0;
        }

        foreach (var strip in _strips)
        {
            bool touched = false;
            foreach (int t in strip.Used)
            {
                if (_level[t] > 0 || _ahead[t] > 0)
                {
                    touched = true;
                    break;
                }
            }
            if (!touched && !strip.Lit)
            {
                continue;
            }

            var pixels = strip.Pixels;
            if (touched)
            {
                var distance = strip.Distance;
                var position = strip.Position;
                for (int i = 0; i < pixels.Length; i++)
                {
                    int t = position[i];
                    double ahead = _ahead[t];
                    pixels[i] = ahead > 0 ? HeadPixel(distance[i], ahead) : _table[_level[t] * _depth + distance[i]];
                }
            }
            else
            {
                Array.Clear(pixels);
            }
            strip.Lit = touched;
            var r = strip.Rect;
            strip.Bitmap.WritePixels(new Int32Rect(0, 0, r.Width, r.Height), pixels, r.Width * 4, 0);
        }
    }

    /// <summary>A pixel of the rounded head: its distance grows with how far ahead of the head it is.</summary>
    private uint HeadPixel(int d, double ahead)
    {
        double stretch = ahead * _th / _headPx * HeadStretch;
        double d2 = Math.Sqrt(d * (double)d + stretch * stretch);
        return Pack(d2, Alpha(d2));
    }

    /// <summary>Brightness across the border at distance d, times the overall brightness; 0 past maxD.</summary>
    private double Alpha(double d)
    {
        if (d > _maxD)
        {
            return 0;
        }
        double core = Math.Exp(-CoreFalloff * (d / _th) * (d / _th));
        double tail = Math.Exp(-Math.Max(0, d - TailStart * _th) / _rc) * Math.Max(0, 1 - d / _maxD);
        return Math.Min(1, CoreWeight * core + TailWeight * tail) * _brightness;
    }

    /// <summary>The colour at distance d (edge colour warming inward) with this alpha, premultiplied, as Pbgra32.</summary>
    private uint Pack(double d, double alpha)
    {
        if (alpha < Cutoff)
        {
            return 0;
        }
        double k = Math.Min(1, d / (_th + _rc));
        uint Channel(byte edge, byte warm) => (uint)Math.Round((edge + (warm - edge) * k) * alpha);
        uint a = (uint)Math.Round(255 * alpha);
        return a << 24 | Channel(_edge.R, _warm.R) << 16 | Channel(_edge.G, _warm.G) << 8 | Channel(_edge.B, _warm.B);
    }

    private Strip BuildStrip(Int32Rect rect)
    {
        int count = Math.Max(0, rect.Width * rect.Height);
        var distance = new int[count];
        var position = new int[count];
        var used = new HashSet<int>();
        int w = _width, h = _height;
        for (int row = 0; row < rect.Height; row++)
        {
            int y = rect.Y + row;
            for (int col = 0; col < rect.Width; col++)
            {
                int x = rect.X + col;
                int top = y, right = w - 1 - x, bottom = h - 1 - y, left = x;
                int d = Math.Min(Math.Min(top, right), Math.Min(bottom, left));
                int t = d == top ? x
                    : d == right ? w + y
                    : d == bottom ? w + h + (w - 1 - x)
                    : 2 * w + h + (h - 1 - y);
                int i = row * rect.Width + col;
                distance[i] = Math.Min(d, _depth - 1);
                position[i] = t;
                used.Add(t);
            }
        }
        var bitmap = new WriteableBitmap(Math.Max(1, rect.Width), Math.Max(1, rect.Height),
            96 * _dpiScaleX, 96 * _dpiScaleY, PixelFormats.Pbgra32, null);
        return new Strip(rect, distance, position, new List<int>(used).ToArray(), bitmap, new uint[count]);
    }

    /// <summary>
    /// Strip depth in device pixels: covers every pixel with d ≤ maxD, rounded up to a whole number
    /// of device-independent pixels so the strips sit exactly side by side at any scaling.
    /// </summary>
    private static int Margin(double maxD, double dpiScale)
    {
        int margin = (int)Math.Floor(maxD) + 1;
        for (int candidate = margin; candidate < margin + 64; candidate++)
        {
            double logical = candidate / dpiScale;
            if (Math.Abs(logical - Math.Round(logical)) < 1e-6)
            {
                return candidate;
            }
        }
        return margin;
    }

    private static double Mod(double value, double modulus) => ((value % modulus) + modulus) % modulus;
}
