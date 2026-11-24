using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using SpotifyLyricsOverlay.Helpers;
using SpotifyLyricsOverlay.Models;
using SpotifyLyricsOverlay.Services;

namespace SpotifyLyricsOverlay
{
    public partial class MainWindow : Window
    {
        private readonly SmtcService _smtcService = new();
        private readonly LyricsService _lyricsService = new();
        private AppSettings _settings = new();

        private DispatcherTimer? _syncTimer;
        private DispatcherTimer? _toastTimer;
        private DispatcherTimer? _scrollResumeTimer;
        private CancellationTokenSource? _lyricsCts;

        private List<LyricLine> _currentLyrics = new();
        private int _lastActiveIndex = -2;
        private const int HOTKEY_ID = 9102;

        private double _targetScrollOffset = 0;
        private double _currentScrollOffset = 0;
        private bool _userIsScrolling = false;
        private bool _isTemporarilyClickable = false;
        private int _topmostTick = 0;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_SIZE_BOTTOMRIGHT = 0xF008;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _settings = SettingsService.Load();
            ApplySettings();

            // Set up Hwnd Hook and Register Hotkey (Ctrl + Shift + L)
            var helper = new WindowInteropHelper(this);
            var source = HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(HwndHook);

            WindowHelper.RegisterHotKey(
                helper.Handle,
                HOTKEY_ID,
                WindowHelper.MOD_CONTROL | WindowHelper.MOD_SHIFT | WindowHelper.MOD_NOREPEAT,
                WindowHelper.VK_L
            );

            // High-precision sync & smooth-scroll timer (approx 30fps)
            _syncTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _syncTimer.Tick += SyncTimer_Tick;
            _syncTimer.Start();

            // Toast hide timer
            _toastTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            _toastTimer.Tick += (s, ev) =>
            {
                _toastTimer.Stop();
                ToastBorder.Visibility = Visibility.Collapsed;
            };

            // User scroll resume timer (resume auto-scroll after 3s)
            _scrollResumeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _scrollResumeTimer.Tick += (s, ev) =>
            {
                _scrollResumeTimer.Stop();
                _userIsScrolling = false;
            };

            // Wire up SMTC events
            _smtcService.SongChanged += OnSongChanged;
            _smtcService.PlaybackStateChanged += OnPlaybackStateChanged;

            WindowHelper.ForceTopMost(this);
            await _smtcService.InitializeAsync();
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WindowHelper.WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleLock();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void ApplySettings()
        {
            // Position and Size
            if (_settings.WindowWidth >= MinWidth) Width = _settings.WindowWidth;
            if (_settings.WindowHeight >= MinHeight) Height = _settings.WindowHeight;

            var virtualWidth = SystemParameters.VirtualScreenWidth;
            var virtualHeight = SystemParameters.VirtualScreenHeight;
            if (_settings.WindowLeft >= 0 && _settings.WindowLeft < virtualWidth - 100)
                Left = _settings.WindowLeft;
            if (_settings.WindowTop >= 0 && _settings.WindowTop < virtualHeight - 100)
                Top = _settings.WindowTop;

            // Font size slider & label
            SliderFontSize.Value = _settings.FontSize;
            TxtFontSizePreview.Text = $"{(int)_settings.FontSize} pt";

            // Colors
            TxtHexColor.Text = _settings.ActiveColor;
            SliderOpacity.Value = _settings.InactiveOpacity;
            TxtOpacityPreview.Text = $"{(int)(_settings.InactiveOpacity * 100)}%";

            ApplyColorsAndSizes();
            ApplyGlassStyle(_settings.GlassBackground);
            ApplyLock(_settings.IsLocked, showToast: false);
        }

        private void ApplyColorsAndSizes()
        {
            var activeColor = ParseColor(_settings.ActiveColor, Colors.White);
            var activeBrush = new SolidColorBrush(activeColor);

            ProgressBarFill.Background = activeBrush;
            TxtPlayingDot.Foreground = activeBrush;

            if (_lastActiveIndex >= 0)
            {
                UpdateLyricLineStyles(_lastActiveIndex);
            }
        }

        private void ApplyGlassStyle(bool glass)
        {
            _settings.GlassBackground = glass;
            if (glass)
            {
                CardBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D0121216"));
                CardBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#35FFFFFF"));
                CardBorder.BorderThickness = new Thickness(1);
                CardBorder.Effect = new DropShadowEffect
                {
                    BlurRadius = 22,
                    Direction = 270,
                    ShadowDepth = 4,
                    Opacity = 0.55,
                    Color = Colors.Black
                };
                TxtGlassIcon.Text = "🪟";
            }
            else
            {
                // Pure transparent background: invisible hit-test brush (#01000000)
                CardBorder.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
                CardBorder.BorderBrush = Brushes.Transparent;
                CardBorder.BorderThickness = new Thickness(0);
                CardBorder.Effect = null;
                TxtGlassIcon.Text = "✨";
            }
        }

        private void ApplyLock(bool locked, bool showToast = true)
        {
            _settings.IsLocked = locked;
            _isTemporarilyClickable = false;

            if (locked)
            {
                // Hide normal toolbar and settings
                ToolbarPanel.Visibility = Visibility.Collapsed;
                SettingsFlyout.Visibility = Visibility.Collapsed;
                ResizeGripBorder.Visibility = Visibility.Collapsed;
                FloatingUnlockBorder.Visibility = Visibility.Collapsed;

                WindowHelper.SetClickThrough(this, true);
                if (showToast) ShowToast("🔒 Đã khóa (Click-through) • Rê chuột vào để hiện nút Mở khóa");
            }
            else
            {
                // Restore toolbar
                ToolbarPanel.Visibility = Visibility.Visible;
                FloatingUnlockBorder.Visibility = Visibility.Collapsed;
                ResizeGripBorder.Visibility = Visibility.Visible;

                WindowHelper.SetClickThrough(this, false);
                if (showToast) ShowToast("🔓 Đã mở khóa • Có thể kéo di chuyển & điều chỉnh");
            }
        }

        private void ToggleLock()
        {
            ApplyLock(!_settings.IsLocked, showToast: true);
            SettingsService.Save(_settings);
        }

        private void ShowToast(string message)
        {
            TxtToast.Text = message;
            ToastBorder.Visibility = Visibility.Visible;
            _toastTimer?.Stop();
            _toastTimer?.Start();
        }

        private void OnSongChanged(SongInfo song)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                _lyricsCts?.Cancel();
                _lyricsCts = new CancellationTokenSource();
                var token = _lyricsCts.Token;

                if (song.IsEmpty)
                {
                    TxtSongTitle.Text = "Đang chờ Spotify...";
                    TxtArtist.Text = "Mở nhạc để hiện lyrics Spotify";
                    ImgThumbnail.Source = null;
                    ProgressBarFill.Width = 0;
                    TxtTime.Text = "00:00 / 00:00";
                    StatusPanel.Visibility = Visibility.Visible;
                    TxtStatus.Text = "Chờ bài hát từ Spotify...";
                    _currentLyrics.Clear();
                    LyricsListPanel.Children.Clear();
                    _lastActiveIndex = -2;
                    return;
                }

                TxtSongTitle.Text = song.Title;
                TxtArtist.Text = song.Artist;
                ImgThumbnail.Source = song.Thumbnail;
                TxtStatus.Text = "Đang tải lyrics Spotify...";
                StatusPanel.Visibility = Visibility.Visible;

                try
                {
                    var lyrics = await _lyricsService.GetLyricsAsync(song, token);
                    if (token.IsCancellationRequested) return;

                    _currentLyrics = lyrics;
                    _lastActiveIndex = -2;

                    if (_currentLyrics.Count > 0)
                    {
                        StatusPanel.Visibility = Visibility.Collapsed;
                        PopulateLyricsUI();
                    }
                    else
                    {
                        TxtStatus.Text = "Không tìm thấy lyrics đồng bộ cho bài hát này ♪";
                        StatusPanel.Visibility = Visibility.Visible;
                        LyricsListPanel.Children.Clear();
                    }
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        TxtStatus.Text = "Không thể tải lyrics: " + ex.Message;
                        StatusPanel.Visibility = Visibility.Visible;
                    }
                }
            });
        }

        private void PopulateLyricsUI()
        {
            LyricsListPanel.Children.Clear();
            _currentScrollOffset = 0;
            _targetScrollOffset = 0;
            LyricsScrollViewer.ScrollToVerticalOffset(0);

            var activeColor = ParseColor(_settings.ActiveColor, Colors.White);
            var inactiveColor = ParseColor(_settings.InactiveColor, Colors.White);

            var font = new FontFamily("pack://application:,,,/Fonts/#Spotify Mix UI, Segoe UI, -apple-system, sans-serif");

            for (int i = 0; i < _currentLyrics.Count; i++)
            {
                var line = _currentLyrics[i];
                var tb = new TextBlock
                {
                    Text = line.Text,
                    FontSize = _settings.FontSize * 0.86,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = font,
                    Foreground = new SolidColorBrush(inactiveColor),
                    Opacity = _settings.InactiveOpacity,
                    TextAlignment = TextAlignment.Left,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 7, 0, 7),
                    Cursor = Cursors.Hand,
                    Tag = line
                };

                tb.Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 6,
                    ShadowDepth = 2,
                    Direction = 315,
                    Opacity = 0.95
                };

                tb.MouseLeftButtonDown += async (s, ev) =>
                {
                    if (!_settings.IsLocked)
                    {
                        ev.Handled = true;
                        await _smtcService.SeekToAsync(line.Time);
                    }
                };

                LyricsListPanel.Children.Add(tb);
            }
        }

        private void UpdateLyricLineStyles(int activeIndex)
        {
            var activeColor = ParseColor(_settings.ActiveColor, Colors.White);
            var activeBrush = new SolidColorBrush(activeColor);

            var inactiveColor = ParseColor(_settings.InactiveColor, Colors.White);
            var inactiveBrush = new SolidColorBrush(inactiveColor);

            for (int i = 0; i < LyricsListPanel.Children.Count; i++)
            {
                if (LyricsListPanel.Children[i] is TextBlock tb)
                {
                    if (i == activeIndex)
                    {
                        tb.Foreground = activeBrush;
                        tb.Opacity = 1.0;
                        tb.FontWeight = FontWeights.Bold;
                        tb.FontSize = _settings.FontSize;
                    }
                    else
                    {
                        tb.Foreground = inactiveBrush;
                        tb.Opacity = _settings.InactiveOpacity;
                        tb.FontWeight = FontWeights.SemiBold;
                        tb.FontSize = _settings.FontSize * 0.86;
                    }
                }
            }
        }

        private void OnPlaybackStateChanged(bool isPlaying)
        {
            Dispatcher.InvokeAsync(() =>
            {
                TxtPlayingDot.Opacity = isPlaying ? 1.0 : 0.3;
                TxtPlayPauseIcon.Text = isPlaying ? "⏸" : "▶";
            });
        }

        private void SyncTimer_Tick(object? sender, EventArgs e)
        {
            // 0. Periodic topmost refresh for games like Valorant
            if (++_topmostTick % 30 == 0)
            {
                WindowHelper.ForceTopMost(this);
            }

            // 1. Check mouse hover when locked to show Unlock button & handle click-through
            CheckLockedHoverState();

            // 2. Playback & timeline
            var song = _smtcService.CurrentSong;
            if (song.IsEmpty) return;

            var pos = _smtcService.GetEstimatedCurrentPosition();
            var dur = song.Duration;

            TxtTime.Text = $"{FormatTime(pos)} / {FormatTime(dur)}";

            var maxBarWidth = ProgressBarContainer.ActualWidth;
            if (maxBarWidth > 0 && dur > TimeSpan.Zero)
            {
                var progress = Math.Clamp(pos.TotalMilliseconds / dur.TotalMilliseconds, 0.0, 1.0);
                ProgressBarFill.Width = progress * maxBarWidth;
            }

            // 3. Lyrics synchronization
            if (_currentLyrics.Count == 0 || LyricsListPanel.Children.Count == 0) return;

            int activeIndex = _lyricsService.GetActiveIndex(_currentLyrics, pos, _settings.SyncOffsetSeconds);
            if (activeIndex != _lastActiveIndex)
            {
                _lastActiveIndex = activeIndex;
                UpdateLyricLineStyles(activeIndex);

                if (activeIndex >= 0 && activeIndex < LyricsListPanel.Children.Count)
                {
                    if (LyricsListPanel.Children[activeIndex] is FrameworkElement activeElem)
                    {
                        try
                        {
                            var transform = activeElem.TransformToAncestor(LyricsListPanel);
                            var point = transform.Transform(new Point(0, 0));
                            double target = point.Y - (LyricsScrollViewer.ActualHeight * 0.30);
                            if (target < 0) target = 0;
                            _targetScrollOffset = target;
                        }
                        catch { }
                    }
                }
            }

            // Smooth spring lerp scroll
            if (!_userIsScrolling)
            {
                double diff = _targetScrollOffset - _currentScrollOffset;
                if (Math.Abs(diff) > 0.5)
                {
                    _currentScrollOffset += diff * 0.14;
                    LyricsScrollViewer.ScrollToVerticalOffset(_currentScrollOffset);
                }
            }
        }

        private void CheckLockedHoverState()
        {
            if (!_settings.IsLocked) return;

            if (WindowHelper.GetCursorPos(out var pt))
            {
                try
                {
                    var winPt = PointFromScreen(new Point(pt.X, pt.Y));
                    bool isInsideWindow = winPt.X >= 0 && winPt.X <= ActualWidth && winPt.Y >= 0 && winPt.Y <= ActualHeight;

                    if (isInsideWindow)
                    {
                        if (FloatingUnlockBorder.Visibility != Visibility.Visible)
                        {
                            FloatingUnlockBorder.Visibility = Visibility.Visible;
                        }

                        // Check if cursor is over FloatingUnlockBorder
                        if (FloatingUnlockBorder.ActualWidth > 0 && FloatingUnlockBorder.ActualHeight > 0)
                        {
                            var transform = FloatingUnlockBorder.TransformToAncestor(this);
                            var origin = transform.Transform(new Point(0, 0));
                            var unlockRect = new Rect(origin, new Size(FloatingUnlockBorder.ActualWidth, FloatingUnlockBorder.ActualHeight));
                            unlockRect.Inflate(8, 8); // Comfortable hit target

                            if (unlockRect.Contains(winPt))
                            {
                                if (!_isTemporarilyClickable)
                                {
                                    _isTemporarilyClickable = true;
                                    WindowHelper.SetClickThrough(this, false); // Enable mouse click on button!
                                }
                            }
                            else
                            {
                                if (_isTemporarilyClickable)
                                {
                                    _isTemporarilyClickable = false;
                                    WindowHelper.SetClickThrough(this, true); // Keep click-through for desktop!
                                }
                            }
                        }
                    }
                    else
                    {
                        // Outside window
                        if (FloatingUnlockBorder.Visibility != Visibility.Collapsed)
                        {
                            FloatingUnlockBorder.Visibility = Visibility.Collapsed;
                        }
                        if (_isTemporarilyClickable)
                        {
                            _isTemporarilyClickable = false;
                            WindowHelper.SetClickThrough(this, true);
                        }
                    }
                }
                catch
                {
                    // Ignore transient point conversion errors
                }
            }
        }

        private static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return fallback;
                if (!hex.StartsWith("#")) hex = "#" + hex;
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return fallback;
            }
        }

        private static string FormatTime(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
            return ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"mm\:ss");
        }

        // Window Events
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_settings.IsLocked && e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                _settings.WindowLeft = Left;
                _settings.WindowTop = Top;
                SettingsService.Save(_settings);
            }
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            _userIsScrolling = true;
            _currentScrollOffset = LyricsScrollViewer.VerticalOffset - (e.Delta * 0.6);
            if (_currentScrollOffset < 0) _currentScrollOffset = 0;
            LyricsScrollViewer.ScrollToVerticalOffset(_currentScrollOffset);

            _scrollResumeTimer?.Stop();
            _scrollResumeTimer?.Start();
        }

        private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_settings.IsLocked && e.ButtonState == MouseButtonState.Pressed)
            {
                var helper = new WindowInteropHelper(this);
                SendMessage(helper.Handle, WM_SYSCOMMAND, (IntPtr)SC_SIZE_BOTTOMRIGHT, IntPtr.Zero);
                _settings.WindowWidth = Width;
                _settings.WindowHeight = Height;
                SettingsService.Save(_settings);
            }
        }

        private async void ProgressBarContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_settings.IsLocked)
            {
                var pos = e.GetPosition(ProgressBarContainer);
                var song = _smtcService.CurrentSong;
                if (ProgressBarContainer.ActualWidth > 0 && song.Duration > TimeSpan.Zero)
                {
                    var ratio = Math.Clamp(pos.X / ProgressBarContainer.ActualWidth, 0.0, 1.0);
                    var targetTime = TimeSpan.FromMilliseconds(ratio * song.Duration.TotalMilliseconds);
                    await _smtcService.SeekToAsync(targetTime);
                }
            }
        }

        // Toolbar Buttons
        private async void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            await _smtcService.TogglePlayPauseAsync();
        }

        private async void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            await _smtcService.SkipNextAsync();
        }

        private void BtnCustomize_Click(object sender, RoutedEventArgs e)
        {
            SettingsFlyout.Visibility = SettingsFlyout.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsFlyout.Visibility = Visibility.Collapsed;
        }

        private void SliderFontSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            _settings.FontSize = Math.Clamp(e.NewValue, 18.0, 48.0);
            TxtFontSizePreview.Text = $"{(int)_settings.FontSize} pt";
            ApplyColorsAndSizes();
            SettingsService.Save(_settings);
        }

        private void BtnPresetFont_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && double.TryParse(tagStr, out var size))
            {
                SliderFontSize.Value = size;
            }
        }

        private void BtnSwatchActive_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
            {
                _settings.ActiveColor = hex;
                TxtHexColor.Text = hex;
                ApplyColorsAndSizes();
                SettingsService.Save(_settings);
            }
        }

        private void BtnApplyHex_Click(object sender, RoutedEventArgs e)
        {
            var hex = TxtHexColor.Text?.Trim() ?? "";
            try
            {
                var color = ParseColor(hex, Colors.Transparent);
                if (color != Colors.Transparent)
                {
                    _settings.ActiveColor = hex;
                    ApplyColorsAndSizes();
                    SettingsService.Save(_settings);
                    ShowToast($"Đã áp dụng màu: {hex}");
                }
            }
            catch
            {
                ShowToast("Mã màu không hợp lệ!");
            }
        }

        private void SliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            _settings.InactiveOpacity = Math.Clamp(e.NewValue, 0.15, 0.85);
            TxtOpacityPreview.Text = $"{(int)(_settings.InactiveOpacity * 100)}%";
            ApplyColorsAndSizes();
            SettingsService.Save(_settings);
        }

        private void BtnFontUp_Click(object sender, RoutedEventArgs e)
        {
            SliderFontSize.Value = Math.Min(48, _settings.FontSize + 2);
        }

        private void BtnFontDown_Click(object sender, RoutedEventArgs e)
        {
            SliderFontSize.Value = Math.Max(18, _settings.FontSize - 2);
        }

        private void BtnSyncMinus_Click(object sender, RoutedEventArgs e)
        {
            _settings.SyncOffsetSeconds -= 0.5;
            ShowToast($"Độ lệch: {_settings.SyncOffsetSeconds:+0.0;-0.0;0.0}s");
            _lastActiveIndex = -2;
            SettingsService.Save(_settings);
        }

        private void BtnSyncPlus_Click(object sender, RoutedEventArgs e)
        {
            _settings.SyncOffsetSeconds += 0.5;
            ShowToast($"Độ lệch: {_settings.SyncOffsetSeconds:+0.0;-0.0;0.0}s");
            _lastActiveIndex = -2;
            SettingsService.Save(_settings);
        }

        private void BtnGlass_Click(object sender, RoutedEventArgs e)
        {
            ApplyGlassStyle(!_settings.GlassBackground);
            ShowToast(_settings.GlassBackground ? "🪟 Nền kính mờ" : "✨ Nền trong suốt");
            SettingsService.Save(_settings);
        }

        private void BtnLock_Click(object sender, RoutedEventArgs e)
        {
            ToggleLock();
        }

        private void BtnFloatingUnlock_Click(object sender, MouseButtonEventArgs e)
        {
            ToggleLock();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _syncTimer?.Stop();
            _lyricsCts?.Cancel();
            _scrollResumeTimer?.Stop();
            _toastTimer?.Stop();

            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
            SettingsService.Save(_settings);

            var helper = new WindowInteropHelper(this);
            WindowHelper.UnregisterHotKey(helper.Handle, HOTKEY_ID);
        }
    }
}
