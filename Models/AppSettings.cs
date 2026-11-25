using System;

namespace SpotifyLyricsOverlay.Models
{
    public class AppSettings
    {
        public double WindowLeft { get; set; } = 720.0;
        public double WindowTop { get; set; } = 80.0;
        public double WindowWidth { get; set; } = 800.0;
        public double WindowHeight { get; set; } = 440.0;

        public double FontSize { get; set; } = 28.0;
        public string ActiveColor { get; set; } = "#FFFFFF";
        public string InactiveColor { get; set; } = "#FFFFFF";
        public double InactiveOpacity { get; set; } = 0.40;
        public string Theme { get; set; } = "SpotifyWhite";
        public string DisplayMode { get; set; } = "SpotifyScroll"; // "SpotifyScroll" or "MiniMode"

        public bool GlassBackground { get; set; } = false;
        public double BackgroundOpacity { get; set; } = 0.0;
        public double SyncOffsetSeconds { get; set; } = 0.0;
        public bool IsLocked { get; set; } = false;
        public bool ShowHeader { get; set; } = true;
        public string Alignment { get; set; } = "Left";

        // v1.1.0 Upgraded Features
        public bool AdaptiveColor { get; set; } = true;
        public bool ShowVisualizer { get; set; } = false; // Optional, OFF by default per user request
        public bool ShowTranslation { get; set; } = true;
        public string TranslationLanguage { get; set; } = "vi"; // "vi" or "en"
        public bool AutoHideOnPause { get; set; } = false;
        public int AutoHideDelaySeconds { get; set; } = 8;
        public bool StartWithWindows { get; set; } = false;
        public bool SaveOfflineCache { get; set; } = true;
        public string SpotifySpDcCookie { get; set; } = string.Empty;
        public string ActivePreset { get; set; } = "Chill Desktop";
    }
}
