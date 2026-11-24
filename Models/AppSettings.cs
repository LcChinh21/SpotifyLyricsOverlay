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
        public string DisplayMode { get; set; } = "SpotifyScroll";
        
        public bool GlassBackground { get; set; } = false; // Default: 100% transparent background
        public double BackgroundOpacity { get; set; } = 0.0;
        public double SyncOffsetSeconds { get; set; } = 0.0;
        public bool IsLocked { get; set; } = false;
        public bool ShowHeader { get; set; } = true;
        public string Alignment { get; set; } = "Left";
    }
}
