using System;

namespace SpotifyLyricsOverlay.Models
{
    public class LyricLine
    {
        public TimeSpan Time { get; set; }
        public string Text { get; set; } = string.Empty;

        public LyricLine(TimeSpan time, string text)
        {
            Time = time;
            Text = text;
        }

        public override string ToString() => $"[{Time:mm\\:ss\\.ff}] {Text}";
    }
}
