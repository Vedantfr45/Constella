using System;

namespace Constella.Core;

/// <summary>
/// Creates <see cref="Random"/> instances from text. string.GetHashCode() changes between runs,
/// so we use FNV-1a to get the same layout for the same lyric line every time.
/// </summary>
internal static class StableRandom
{
    public static Random For(string text, int salt = 0)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in text)
            {
                hash = (hash ^ c) * 16777619;
            }
            hash = (hash ^ (uint)salt) * 16777619;
            return new Random((int)(hash & 0x7FFFFFFF));
        }
    }

    public static double Range(this Random random, double min, double max) =>
        min + random.NextDouble() * (max - min);
}
