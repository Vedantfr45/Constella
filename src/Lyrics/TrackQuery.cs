using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Constella.Media;

namespace Constella.Lyrics;

/// <summary>A title/artist pair to search for.</summary>
public sealed record TrackQuery(string Title, string Artist, string Album)
{
    /// <summary>
    /// Search variations, best first. Browser players often report video titles such as
    /// "Artist - Song (Official Video)" with the channel as artist, so we also try cleaned and
    /// split versions of the title.
    /// </summary>
    public static IReadOnlyList<TrackQuery> CandidatesFor(TrackInfo track)
    {
        var list = new List<TrackQuery>();
        void Add(string title, string artist, string album)
        {
            title = title.Trim();
            artist = artist.Trim();
            if (title.Length == 0)
            {
                return;
            }
            var query = new TrackQuery(title, artist, album);
            if (!list.Exists(q => string.Equals(q.Title, title, StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(q.Artist, artist, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(query);
            }
        }

        string title = CleanTitle(track.Title);
        string artist = CleanArtist(track.Artist);

        Add(title, artist, track.Album);
        Add(RemoveFeaturing(title), FirstArtist(artist), track.Album);

        // "Artist - Song" in the title (typical for YouTube).
        var split = Regex.Match(title, @"^(?<left>.+?)\s+[-–—]\s+(?<right>.+)$");
        if (split.Success)
        {
            string left = split.Groups["left"].Value;
            string right = RemoveFeaturing(split.Groups["right"].Value);
            Add(right, left, string.Empty);
            Add(left, right, string.Empty);
        }

        return list;
    }

    private static string CleanTitle(string title)
    {
        // Drop bracketed video/marketing notes: (Official Video), [Lyrics], (Visualizer), (HD) ...
        title = Regex.Replace(title,
            @"\s*[\(\[][^\)\]]*\b(official|video|audio|lyrics?|visuali[sz]er|m/?v|hd|hq|4k|remaster(ed)?|explicit|color coded)\b[^\)\]]*[\)\]]",
            string.Empty, RegexOptions.IgnoreCase);
        // "Song | Album Name" or "Song // Live session".
        title = Regex.Replace(title, @"\s+(\||//).*$", string.Empty);
        return title.Trim();
    }

    private static string CleanArtist(string artist)
    {
        artist = Regex.Replace(artist, @"\s*-\s*Topic$", string.Empty, RegexOptions.IgnoreCase);
        artist = Regex.Replace(artist, @"(VEVO|\s+Official)$", string.Empty, RegexOptions.IgnoreCase);
        return artist.Trim();
    }

    private static string RemoveFeaturing(string title) =>
        Regex.Replace(title, @"\s*[\(\[]?\s*\b(feat\.?|ft\.?|featuring)\s.*$", string.Empty, RegexOptions.IgnoreCase).Trim();

    private static string FirstArtist(string artist) =>
        Regex.Split(artist, @"\s*(,|&|\bx\b|\band\b|;)\s*", RegexOptions.IgnoreCase)[0].Trim();
}
