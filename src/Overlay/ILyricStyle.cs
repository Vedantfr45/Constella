using System.Collections.Generic;
using System.Windows;
using Constella.Lyrics;
using Constella.Overlay.Constellation;
using Constella.Overlay.Lines;

namespace Constella.Overlay;

/// <summary>
/// A way of drawing lyrics. Each style owns its own visual and draws every frame from the song
/// time, so seeking and pausing work automatically. To add a style: implement this interface and
/// register it in <see cref="LyricStyles"/>; it then appears in the settings window.
/// </summary>
internal interface ILyricStyle
{
    /// <summary>The element the overlay places on screen (full size, never hit-testable).</summary>
    UIElement Visual { get; }

    void SetLook(LyricLook look);

    /// <summary>New lyrics (or none). The style fades out whatever it was showing.</summary>
    void SetSheet(LyricSheet? sheet, double wall);

    /// <summary>Advances animation; returns true while something is on screen.</summary>
    bool Update(in FrameContext frame);
}

/// <summary>The registered lyric styles.</summary>
internal static class LyricStyles
{
    public static IReadOnlyList<(string Id, string Name)> All { get; } = new[]
    {
        ("constellation", "Constellation"),
        ("stack", "Stack"),
        ("spotlight", "Spotlight"),
        ("karaoke", "Karaoke"),
        ("wipe", "Light wipe"),
        ("type", "Type-on"),
        ("underline", "Underline"),
    };

    public static ILyricStyle Create(string id) => id switch
    {
        "stack" => new StackStyle(),
        "spotlight" => new SpotlightStyle(),
        "karaoke" => new KaraokeStyle(),
        "wipe" => new WipeStyle(),
        "type" => new TypeStyle(),
        "underline" => new TracerStyle(),
        _ => new ConstellationStyle(),
    };
}
