using System;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace SpotifyLyricsOverlay.Services
{
    public class TrayService : IDisposable
    {
        private readonly WinForms.NotifyIcon _notifyIcon;
        private readonly WinForms.ToolStripMenuItem _startupItem;
        private readonly WinForms.ToolStripMenuItem _lockItem;
        private readonly WinForms.ToolStripMenuItem _visibilityItem;

        public event Action? ToggleVisibilityRequested;
        public event Action? ToggleLockRequested;
        public event Action? OpenSettingsRequested;
        public event Action<string>? PresetSelected;
        public event Action<bool>? StartupToggled;
        public event Action? ExitRequested;

        public TrayService(bool initialStartup, bool initialLocked)
        {
            _notifyIcon = new WinForms.NotifyIcon
            {
                Icon = CreateSpotifyStyleIcon(),
                Text = "Spotify Lyrics Overlay",
                Visible = true
            };

            var menu = new WinForms.ContextMenuStrip();

            _visibilityItem = new WinForms.ToolStripMenuItem("Ẩn / Hiện Overlay");
            _visibilityItem.Click += (s, e) => ToggleVisibilityRequested?.Invoke();

            _lockItem = new WinForms.ToolStripMenuItem(initialLocked ? "Mở khóa chuột (Ctrl+Shift+L)" : "Khóa xuyên chuột (Ctrl+Shift+L)");
            _lockItem.Click += (s, e) => ToggleLockRequested?.Invoke();

            var settingsItem = new WinForms.ToolStripMenuItem("⚙️ Mở bảng Tùy chỉnh");
            settingsItem.Click += (s, e) => OpenSettingsRequested?.Invoke();

            var presetMenu = new WinForms.ToolStripMenuItem("🎨 Chọn nhanh Preset");
            foreach (var preset in new[] { "Chill Desktop", "Gaming / Valorant", "Mini Island", "Karaoke" })
            {
                var item = new WinForms.ToolStripMenuItem(preset);
                item.Click += (s, e) => PresetSelected?.Invoke(preset);
                presetMenu.DropDownItems.Add(item);
            }

            _startupItem = new WinForms.ToolStripMenuItem("🚀 Khởi động cùng Windows")
            {
                Checked = initialStartup,
                CheckOnClick = true
            };
            _startupItem.CheckedChanged += (s, e) => StartupToggled?.Invoke(_startupItem.Checked);

            var exitItem = new WinForms.ToolStripMenuItem("❌ Thoát");
            exitItem.Click += (s, e) => ExitRequested?.Invoke();

            menu.Items.Add(_visibilityItem);
            menu.Items.Add(_lockItem);
            menu.Items.Add(settingsItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(presetMenu);
            menu.Items.Add(_startupItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (s, e) => ToggleVisibilityRequested?.Invoke();
        }

        public void UpdateStates(bool isVisible, bool isLocked, bool isStartup)
        {
            _visibilityItem.Text = isVisible ? "👁️ Ẩn Overlay" : "👁️ Hiện Overlay";
            _lockItem.Text = isLocked ? "🔓 Mở khóa chuột (Ctrl+Shift+L)" : "🔒 Khóa xuyên chuột (Ctrl+Shift+L)";
            _startupItem.Checked = isStartup;
        }

        private static Drawing.Icon CreateSpotifyStyleIcon()
        {
            using var bmp = new Drawing.Bitmap(32, 32);
            using var g = Drawing.Graphics.FromImage(bmp);
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);

            using var bgBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(29, 185, 84));
            g.FillEllipse(bgBrush, 1, 1, 30, 30);

            using var whitePen = new Drawing.Pen(Drawing.Color.White, 2.8f)
            {
                StartCap = Drawing.Drawing2D.LineCap.Round,
                EndCap = Drawing.Drawing2D.LineCap.Round
            };
            g.DrawArc(whitePen, 7, 10, 18, 12, 200, 140);
            g.DrawArc(whitePen, 9, 15, 14, 10, 200, 140);
            g.DrawArc(whitePen, 11, 20, 10, 8, 200, 140);

            return Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
