using System;

namespace SpotifyLyricsOverlay.Models
{
    public class LyricLine
    {
        public TimeSpan Time { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? TranslatedText { get; set; }

        public LyricLine(TimeSpan time, string text, string? translatedText = null)
        {
            Time = time;
            Text = text;
            TranslatedText = translatedText;
        }

        public override string ToString() => $"[{Time:mm\\:ss\\.ff}] {Text}";
    }
}
