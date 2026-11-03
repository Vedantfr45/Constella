using System;
using System.Windows.Media;

namespace Constella.Core;

/// <summary>Easing curves and interpolation helpers used by every animation.</summary>
internal static class Ease
{
    public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    public static double Lerp(double from, double to, double t) => from + (to - from) * t;

    /// <summary>0..1 progress of <paramref name="now"/> through [start, start + duration].</summary>
    public static double Progress(double now, double start, double duration) =>
        duration <= 0 ? (now >= start ? 1 : 0) : Clamp01((now - start) / duration);

    public static double OutCubic(double t)
    {
        double u = 1 - Clamp01(t);
        return 1 - u * u * u;
    }

    public static double OutQuint(double t)
    {
        double u = 1 - Clamp01(t);
        return 1 - u * u * u * u * u;
    }

    public static double InOutCubic(double t)
    {
        t = Clamp01(t);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    public static double InOutSine(double t) => -(Math.Cos(Math.PI * Clamp01(t)) - 1) / 2;

    /// <summary>Ease-out with a small overshoot, used for the "pop" of a new star.</summary>
    public static double OutBack(double t, double overshoot = 1.6)
    {
        t = Clamp01(t) - 1;
        return 1 + (overshoot + 1) * t * t * t + overshoot * t * t;
    }

    /// <summary>
    /// Frame-rate independent exponential approach: moves <paramref name="current"/> toward
    /// <paramref name="target"/> with time constant <paramref name="tau"/> seconds.
    /// </summary>
    public static double Approach(double current, double target, double dt, double tau) =>
        tau <= 0 ? target : current + (target - current) * (1 - Math.Exp(-dt / tau));

    public static Color Mix(Color from, Color to, double t)
    {
        t = Clamp01(t);
        return Color.FromArgb(
            (byte)Math.Round(Lerp(from.A, to.A, t)),
            (byte)Math.Round(Lerp(from.R, to.R, t)),
            (byte)Math.Round(Lerp(from.G, to.G, t)),
            (byte)Math.Round(Lerp(from.B, to.B, t)));
    }

    public static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Round(255 * Clamp01(alpha)), color.R, color.G, color.B);
}
