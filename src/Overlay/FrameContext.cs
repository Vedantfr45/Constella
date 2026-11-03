namespace Constella.Overlay;

/// <summary>Everything a layer needs to draw one frame.</summary>
/// <param name="Wall">Seconds on the app's own clock (keeps running while the song is paused).</param>
/// <param name="Dt">Seconds since the previous frame.</param>
/// <param name="SongTime">Current lyric time in seconds (song position plus the user's offset).</param>
/// <param name="Palette">The lyric colours for this frame (they fade when the song changes).</param>
/// <param name="PixelsPerDip">Screen scale, needed for crisp text.</param>
internal readonly record struct FrameContext(double Wall, double Dt, double SongTime, LyricPalette Palette, double PixelsPerDip);
