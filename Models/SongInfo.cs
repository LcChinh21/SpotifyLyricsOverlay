using System;
using System.Windows.Media.Imaging;

namespace SpotifyLyricsOverlay.Models
{
    public class SongInfo
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;
        public TimeSpan Position { get; set; } = TimeSpan.Zero;
        public bool IsPlaying { get; set; }
        public BitmapSource? Thumbnail { get; set; }

        public string CacheKey => $"{Artist} - {Title}".Trim();

        public bool IsEmpty => string.IsNullOrWhiteSpace(Title);

        public override bool Equals(object? obj)
        {
            if (obj is SongInfo other)
            {
                return string.Equals(Title, other.Title, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(Artist, other.Artist, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Title?.ToLowerInvariant(), Artist?.ToLowerInvariant());
        }
    }
}
