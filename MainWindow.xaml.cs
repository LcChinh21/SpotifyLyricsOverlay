using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using SpotifyLyricsOverlay.Helpers;
using SpotifyLyricsOverlay.Models;
using SpotifyLyricsOverlay.Services;

namespace SpotifyLyricsOverlay
{
    public partial class MainWindow : Window
    {
        private readonly SmtcService _smtcService = new();
        private readonly LyricsService _lyricsService = new();
        private readonly TranslationService _translationService = new();
        private readonly AudioVisualizerService _visualizerService = new();
        private TrayService? _trayService;
        private AppSettings _settings = new();

        private DispatcherTimer? _syncTimer;
        private DispatcherTimer? _toastTimer;
        private DispatcherTimer? _scrollResumeTimer;
        private CancellationTokenSource? _lyricsCts;

        private List<LyricLine> _currentLyrics = new();
        private readonly List<Border> _visualizerBars = new();
        private int _lastActiveIndex = -2;
        private const int HOTKEY_ID = 9102;

        private double _targetScrollOffset = 0;
        private double _currentScrollOffset = 0;
        private bool _userIsScrolling = false;
        private bool _isTemporarilyClickable = false;
        private bool _isInitializing = true;
        private bool _isExiting = false;
        private int _topmostTick = 0;
        private DateTimeOffset? _pausedSince = null;
        private bool _hiddenByAutoHide = false;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_SIZE_BOTTOMRIGHT = 0xF008;

        public MainWindow()
        {
            InitializeComponent();
            InitializeVisualizerBars();
        }

        private void InitializeVisualizerBars()
        {
            VisualizerPanel.Children.Clear();
            _visualizerBars.Clear();
            for (int i = 0; i < AudioVisualizerService.BarCount; i++)
            {
                var bar = new Border
                {
                    Width = 3,
                    Height = 3,
                    CornerRadius = new CornerRadius(1.5),
                    Margin = new Thickness(1.5, 0, 1.5, 0),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Background = Brushes.White
                };
                _visualizerBars.Add(bar);
                VisualizerPanel.Children.Add(bar);
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            _settings = SettingsService.Load();
            _settings.StartWithWindows = StartupHelper.IsStartupEnabled();

            ApplySettings();
            InitializeTray();

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

            _syncTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _syncTimer.Tick += SyncTimer_Tick;
            _syncTimer.Start();

            _toastTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            _toastTimer.Tick += (s, ev) =>
            {
                _toastTimer.Stop();
                ToastBorder.Visibility = Visibility.Collapsed;
            };

            _scrollResumeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _scrollResumeTimer.Tick += (s, ev) =>
            {
                _scrollResumeTimer.Stop();
                _userIsScrolling = false;
            };

            _smtcService.SongChanged += OnSongChanged;
            _smtcService.PlaybackStateChanged += OnPlaybackStateChanged;
            _visualizerService.SpectrumUpdated += OnSpectrumUpdated;

            _isInitializing = false;

            WindowHelper.ForceTopMost(this);
            await _smtcService.InitializeAsync();
        }

        private void InitializeTray()
        {
            _trayService = new TrayService(_settings.StartWithWindows, _settings.IsLocked);
            _trayService.ToggleVisibilityRequested += () => Dispatcher.Invoke(ToggleOverlayVisibility);
            _trayService.ToggleLockRequested += () => Dispatcher.Invoke(ToggleLock);
            _trayService.OpenSettingsRequested += () => Dispatcher.Invoke(() =>
            {
                ShowOverlayFromTray();
                if (_settings.IsLocked) ApplyLock(false);
                SettingsFlyout.Visibility = Visibility.Visible;
            });
            _trayService.PresetSelected += preset => Dispatcher.Invoke(() => ApplyPreset(preset));
            _trayService.StartupToggled += enabled => Dispatcher.Invoke(() =>
            {
                _settings.StartWithWindows = enabled;
                StartupHelper.SetStartup(enabled);
                ChkStartup.IsChecked = enabled;
                SettingsService.Save(_settings);
            });
            _trayService.ExitRequested += () => Dispatcher.Invoke(ExitApplication);
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
            if (_settings.WindowWidth >= MinWidth) Width = _settings.WindowWidth;
            if (_settings.WindowHeight >= MinHeight) Height = _settings.WindowHeight;

            var virtualWidth = SystemParameters.VirtualScreenWidth;
            var virtualHeight = SystemParameters.VirtualScreenHeight;
            if (_settings.WindowLeft >= 0 && _settings.WindowLeft < virtualWidth - 100)
                Left = _settings.WindowLeft;
            if (_settings.WindowTop >= 0 && _settings.WindowTop < virtualHeight - 100)
                Top = _settings.WindowTop;

            SliderFontSize.Value = _settings.FontSize;
            TxtFontSizePreview.Text = $"{(int)_settings.FontSize} pt";

            TxtHexColor.Text = _settings.ActiveColor;
            SliderOpacity.Value = _settings.InactiveOpacity;
            TxtOpacityPreview.Text = $"{(int)(_settings.InactiveOpacity * 100)}%";

            ChkAdaptiveColor.IsChecked = _settings.AdaptiveColor;
            ChkShowTranslation.IsChecked = _settings.ShowTranslation;
            CmbTargetLang.SelectedIndex = string.Equals(_settings.TranslationLanguage, "en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            ChkMiniMode.IsChecked = _settings.DisplayMode == "MiniMode";
            ChkVisualizer.IsChecked = _settings.ShowVisualizer;
            ChkAutoHide.IsChecked = _settings.AutoHideOnPause;
            ChkStartup.IsChecked = _settings.StartWithWindows;
            ChkOfflineCache.IsChecked = _settings.SaveOfflineCache;
            TxtSpDcCookie.Text = _settings.SpotifySpDcCookie;

            ApplyDisplayMode(_settings.DisplayMode);
            ApplyVisualizerState(_settings.ShowVisualizer);
            ApplyColorsAndSizes();
            ApplyGlassStyle(_settings.GlassBackground);
            ApplyLock(_settings.IsLocked, showToast: false);
        }

        private Color GetEffectiveActiveColor()
        {
            if (_settings.AdaptiveColor && _smtcService.CurrentSong.DominantColor.HasValue)
            {
                return _smtcService.CurrentSong.DominantColor.Value;
            }
            return ParseColor(_settings.ActiveColor, Colors.White);
        }

        private void ApplyColorsAndSizes()
        {
            var activeColor = GetEffectiveActiveColor();
            var activeBrush = new SolidColorBrush(activeColor);

            ProgressBarFill.Background = activeBrush;
            TxtPlayingDot.Foreground = activeBrush;

            foreach (var bar in _visualizerBars)
            {
                bar.Background = activeBrush;
            }

            if (_settings.DisplayMode == "MiniMode")
            {
                UpdateMiniModeDisplay(_lastActiveIndex);
            }
            else
            {
                UpdateLyricLineStyles(_lastActiveIndex);
            }
        }

        private void ApplyDisplayMode(string mode)
        {
            _settings.DisplayMode = mode == "MiniMode" ? "MiniMode" : "SpotifyScroll";
            bool isMini = _settings.DisplayMode == "MiniMode";

            LyricsScrollViewer.Visibility = isMini ? Visibility.Collapsed : Visibility.Visible;
            MiniModePanel.Visibility = isMini ? Visibility.Visible : Visibility.Collapsed;
            TxtModeBtn.Text = isMini ? "Mini" : "Spotify";

            if (_currentLyrics.Count == 0)
            {
                MiniModePanel.Visibility = Visibility.Collapsed;
            }
            else if (isMini)
            {
                UpdateMiniModeDisplay(_lastActiveIndex);
            }
        }

        private void ApplyVisualizerState(bool enabled)
        {
            _settings.ShowVisualizer = enabled;
            if (enabled)
            {
                VisualizerPanel.Visibility = Visibility.Visible;
                _visualizerService.Start();
            }
            else
            {
                VisualizerPanel.Visibility = Visibility.Collapsed;
                _visualizerService.Stop();
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
                ToolbarPanel.Visibility = Visibility.Collapsed;
                SettingsFlyout.Visibility = Visibility.Collapsed;
                ResizeGripBorder.Visibility = Visibility.Collapsed;
                FloatingUnlockBorder.Visibility = Visibility.Collapsed;

                WindowHelper.SetClickThrough(this, true);
                if (showToast) ShowToast("🔒 Đã khóa (Click-through) • Rê chuột vào để hiện nút Mở khóa");
            }
            else
            {
                ToolbarPanel.Visibility = Visibility.Visible;
                FloatingUnlockBorder.Visibility = Visibility.Collapsed;
                ResizeGripBorder.Visibility = Visibility.Visible;

                WindowHelper.SetClickThrough(this, false);
                if (showToast) ShowToast("🔓 Đã mở khóa • Có thể kéo di chuyển & điều chỉnh");
            }

            _trayService?.UpdateStates(IsVisible, _settings.IsLocked, _settings.StartWithWindows);
        }

        private void ApplyPreset(string presetName)
        {
            _isInitializing = true;
            _settings.ActivePreset = presetName;

            switch (presetName)
            {
                case "Gaming / Valorant":
                    _settings.DisplayMode = "MiniMode";
                    _settings.FontSize = 22.0;
                    _settings.InactiveOpacity = 0.35;
                    _settings.GlassBackground = false;
                    _settings.ShowVisualizer = false;
                    _settings.AutoHideOnPause = true;
                    Height = 165;
                    Width = 640;
                    break;

                case "Mini Island":
                    _settings.DisplayMode = "MiniMode";
                    _settings.FontSize = 24.0;
                    _settings.InactiveOpacity = 0.45;
                    _settings.GlassBackground = true;
                    _settings.ShowVisualizer = false;
                    Height = 165;
                    Width = 680;
                    break;

                case "Karaoke":
                    _settings.DisplayMode = "SpotifyScroll";
                    _settings.FontSize = 34.0;
                    _settings.InactiveOpacity = 0.50;
                    _settings.GlassBackground = true;
                    _settings.AdaptiveColor = true;
                    _settings.ShowVisualizer = true;
                    Height = 480;
                    Width = 840;
                    break;

                case "Chill Desktop":
                default:
                    _settings.DisplayMode = "SpotifyScroll";
                    _settings.FontSize = 28.0;
                    _settings.InactiveOpacity = 0.40;
                    _settings.GlassBackground = false;
                    _settings.AdaptiveColor = true;
                    _settings.ShowVisualizer = false;
                    _settings.AutoHideOnPause = false;
                    Height = 440;
                    Width = 800;
                    break;
            }

            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;

            ApplySettings();
            if (presetName == "Gaming / Valorant")
            {
                ApplyLock(true, showToast: false);
            }
            _isInitializing = false;
            SettingsService.Save(_settings);
            ShowToast($"✨ Đã áp dụng Preset: {presetName}");
        }

        private void ToggleLock()
        {
            ApplyLock(!_settings.IsLocked, showToast: true);
            SettingsService.Save(_settings);
        }

        private void ToggleOverlayVisibility()
        {
            if (IsVisible)
            {
                Hide();
                _trayService?.UpdateStates(false, _settings.IsLocked, _settings.StartWithWindows);
            }
            else
            {
                ShowOverlayFromTray();
            }
        }

        private void ShowOverlayFromTray()
        {
            _hiddenByAutoHide = false;
            Show();
            WindowHelper.ForceTopMost(this);
            _trayService?.UpdateStates(true, _settings.IsLocked, _settings.StartWithWindows);
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
                    MiniModePanel.Visibility = Visibility.Collapsed;
                    TxtStatus.Text = "Chờ bài hát từ Spotify...";
                    _currentLyrics.Clear();
                    LyricsListPanel.Children.Clear();
                    _lastActiveIndex = -2;
                    ApplyColorsAndSizes();
                    return;
                }

                TxtSongTitle.Text = song.Title;
                TxtArtist.Text = song.Artist;
                ImgThumbnail.Source = song.Thumbnail;
                TxtStatus.Text = "Đang tải lyrics Spotify...";
                StatusPanel.Visibility = Visibility.Visible;
                MiniModePanel.Visibility = Visibility.Collapsed;

                // Apply adaptive color from album thumbnail immediately
                ApplyColorsAndSizes();

                try
                {
                    var lyrics = await _lyricsService.GetLyricsAsync(
                        song,
                        _settings.SaveOfflineCache,
                        _settings.SpotifySpDcCookie,
                        token);

                    if (token.IsCancellationRequested) return;

                    _currentLyrics = lyrics;
                    _lastActiveIndex = -2;

                    if (_currentLyrics.Count > 0)
                    {
                        StatusPanel.Visibility = Visibility.Collapsed;
                        if (_settings.ShowTranslation)
                        {
                            await _translationService.TranslateLyricsAsync(song.CacheKey, _currentLyrics, _settings.TranslationLanguage, token);
                            if (token.IsCancellationRequested) return;
                        }

                        PopulateLyricsUI();
                        ApplyDisplayMode(_settings.DisplayMode);
                    }
                    else
                    {
                        TxtStatus.Text = "Không tìm thấy lyrics đồng bộ ♪ (Kéo thả file .lrc vào đây)";
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

            var inactiveColor = ParseColor(_settings.InactiveColor, Colors.White);
            var inactiveBrush = new SolidColorBrush(inactiveColor);
            var font = new FontFamily("pack://application:,,,/Fonts/#Spotify Mix UI, Segoe UI, -apple-system, sans-serif");

            for (int i = 0; i < _currentLyrics.Count; i++)
            {
                var line = _currentLyrics[i];
                var container = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(0, 7, 0, 7),
                    Cursor = Cursors.Hand,
                    Tag = line
                };

                var tbMain = new TextBlock
                {
                    Text = line.Text,
                    FontSize = _settings.FontSize * 0.86,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = font,
                    Foreground = inactiveBrush,
                    Opacity = _settings.InactiveOpacity,
                    TextAlignment = TextAlignment.Left,
                    TextWrapping = TextWrapping.Wrap
                };

                tbMain.Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 6,
                    ShadowDepth = 2,
                    Direction = 315,
                    Opacity = 0.95
                };
                container.Children.Add(tbMain);

                if (_settings.ShowTranslation && !string.IsNullOrWhiteSpace(line.TranslatedText))
                {
                    var tbTrans = new TextBlock
                    {
                        Text = line.TranslatedText,
                        FontSize = _settings.FontSize * 0.56,
                        FontStyle = FontStyles.Italic,
                        FontFamily = font,
                        Foreground = inactiveBrush,
                        Opacity = Math.Max(0.25, _settings.InactiveOpacity * 0.85),
                        TextAlignment = TextAlignment.Left,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 2, 0, 0)
                    };
                    tbTrans.Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 5,
                        ShadowDepth = 1,
                        Direction = 315,
                        Opacity = 0.9
                    };
                    container.Children.Add(tbTrans);
                }

                container.MouseLeftButtonDown += async (s, ev) =>
                {
                    if (!_settings.IsLocked)
                    {
                        ev.Handled = true;
                        await _smtcService.SeekToAsync(line.Time);
                    }
                };

                LyricsListPanel.Children.Add(container);
            }

            if (_lastActiveIndex >= 0)
            {
                UpdateLyricLineStyles(_lastActiveIndex);
            }
        }

        private void UpdateLyricLineStyles(int activeIndex)
        {
            var activeColor = GetEffectiveActiveColor();
            var activeBrush = new SolidColorBrush(activeColor);

            var inactiveColor = ParseColor(_settings.InactiveColor, Colors.White);
            var inactiveBrush = new SolidColorBrush(inactiveColor);

            for (int i = 0; i < LyricsListPanel.Children.Count; i++)
            {
                if (LyricsListPanel.Children[i] is StackPanel container && container.Children.Count > 0)
                {
                    bool isActive = (i == activeIndex);
                    if (container.Children[0] is TextBlock tbMain)
                    {
                        tbMain.Foreground = isActive ? activeBrush : inactiveBrush;
                        tbMain.Opacity = isActive ? 1.0 : _settings.InactiveOpacity;
                        tbMain.FontWeight = isActive ? FontWeights.Bold : FontWeights.SemiBold;
                        tbMain.FontSize = isActive ? _settings.FontSize : _settings.FontSize * 0.86;
                    }

                    if (container.Children.Count > 1 && container.Children[1] is TextBlock tbTrans)
                    {
                        tbTrans.Foreground = isActive ? activeBrush : inactiveBrush;
                        tbTrans.Opacity = isActive ? 0.82 : Math.Max(0.22, _settings.InactiveOpacity * 0.80);
                        tbTrans.FontSize = isActive ? _settings.FontSize * 0.62 : _settings.FontSize * 0.54;
                    }
                }
            }
        }

        private void UpdateMiniModeDisplay(int activeIndex)
        {
            if (_currentLyrics.Count == 0) return;

            var activeColor = GetEffectiveActiveColor();
            var activeBrush = new SolidColorBrush(activeColor);
            TxtMiniActive.Foreground = activeBrush;
            TxtMiniActive.FontSize = _settings.FontSize;
            TxtMiniNext.FontSize = _settings.FontSize * 0.68;
            TxtMiniNext.Opacity = _settings.InactiveOpacity;

            if (activeIndex < 0 || activeIndex >= _currentLyrics.Count)
            {
                TxtMiniActive.Text = "♪ ♫ ♪";
                TxtMiniTranslation.Visibility = Visibility.Collapsed;
                TxtMiniNext.Text = _currentLyrics[0].Text;
                return;
            }

            var current = _currentLyrics[activeIndex];
            TxtMiniActive.Text = string.IsNullOrWhiteSpace(current.Text) ? "♪ ♫ ♪" : current.Text;

            if (_settings.ShowTranslation && !string.IsNullOrWhiteSpace(current.TranslatedText))
            {
                TxtMiniTranslation.Text = current.TranslatedText;
                TxtMiniTranslation.Foreground = activeBrush;
                TxtMiniTranslation.FontSize = _settings.FontSize * 0.60;
                TxtMiniTranslation.Visibility = Visibility.Visible;
            }
            else
            {
                TxtMiniTranslation.Visibility = Visibility.Collapsed;
            }

            TxtMiniNext.Text = (activeIndex + 1 < _currentLyrics.Count) ? _currentLyrics[activeIndex + 1].Text : "";
        }

        private void OnPlaybackStateChanged(bool isPlaying)
        {
            Dispatcher.InvokeAsync(() =>
            {
                TxtPlayingDot.Opacity = isPlaying ? 1.0 : 0.3;
                TxtPlayPauseIcon.Text = isPlaying ? "⏸" : "▶";

                if (isPlaying)
                {
                    _pausedSince = null;
                    if (_hiddenByAutoHide)
                    {
                        ShowOverlayFromTray();
                    }
                }
                else
                {
                    _pausedSince ??= DateTimeOffset.UtcNow;
                }
            });
        }

        private void OnSpectrumUpdated(double[] levels)
        {
            if (!_settings.ShowVisualizer) return;

            Dispatcher.InvokeAsync(() =>
            {
                for (int i = 0; i < _visualizerBars.Count && i < levels.Length; i++)
                {
                    _visualizerBars[i].Height = 3.0 + levels[i] * 21.0;
                }
            }, DispatcherPriority.Render);
        }

        private void SyncTimer_Tick(object? sender, EventArgs e)
        {
            if (++_topmostTick % 30 == 0 && IsVisible)
            {
                WindowHelper.ForceTopMost(this);
            }

            // Auto-hide on pause check
            if (_settings.AutoHideOnPause && !_smtcService.IsPlaying && IsVisible)
            {
                _pausedSince ??= DateTimeOffset.UtcNow;
                if ((DateTimeOffset.UtcNow - _pausedSince.Value).TotalSeconds >= _settings.AutoHideDelaySeconds)
                {
                    _hiddenByAutoHide = true;
                    Hide();
                    _trayService?.UpdateStates(false, _settings.IsLocked, _settings.StartWithWindows);
                    return;
                }
            }

            CheckLockedHoverState();

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

            if (_currentLyrics.Count == 0) return;

            int activeIndex = _lyricsService.GetActiveIndex(_currentLyrics, pos, _settings.SyncOffsetSeconds);
            if (activeIndex != _lastActiveIndex)
            {
                _lastActiveIndex = activeIndex;

                if (_settings.DisplayMode == "MiniMode")
                {
                    UpdateMiniModeDisplay(activeIndex);
                }
                else if (LyricsListPanel.Children.Count > 0)
                {
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
            }

            if (_settings.DisplayMode == "SpotifyScroll" && !_userIsScrolling)
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
            if (!_settings.IsLocked || !IsVisible) return;

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

                        if (FloatingUnlockBorder.ActualWidth > 0 && FloatingUnlockBorder.ActualHeight > 0)
                        {
                            var transform = FloatingUnlockBorder.TransformToAncestor(this);
                            var origin = transform.Transform(new Point(0, 0));
                            var unlockRect = new Rect(origin, new Size(FloatingUnlockBorder.ActualWidth, FloatingUnlockBorder.ActualHeight));
                            unlockRect.Inflate(8, 8);

                            if (unlockRect.Contains(winPt))
                            {
                                if (!_isTemporarilyClickable)
                                {
                                    _isTemporarilyClickable = true;
                                    WindowHelper.SetClickThrough(this, false);
                                }
                            }
                            else
                            {
                                if (_isTemporarilyClickable)
                                {
                                    _isTemporarilyClickable = false;
                                    WindowHelper.SetClickThrough(this, true);
                                }
                            }
                        }
                    }
                    else
                    {
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
                catch { }
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

        // Drag & Drop .lrc support
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;

            var file = files[0];
            if (Path.GetExtension(file).Equals(".lrc", StringComparison.OrdinalIgnoreCase))
            {
                await LoadCustomLrcAsync(file);
            }
            else
            {
                ShowToast("Vui lòng chọn file định dạng .lrc");
            }
        }

        private async Task LoadCustomLrcAsync(string filePath)
        {
            var song = _smtcService.CurrentSong;
            if (song.IsEmpty)
            {
                ShowToast("Hãy mở 1 bài hát trên Spotify trước khi nạp .lrc");
                return;
            }

            try
            {
                var lines = await _lyricsService.ImportLrcFileAsync(song, filePath);
                if (lines.Count > 0)
                {
                    _currentLyrics = lines;
                    _lastActiveIndex = -2;
                    StatusPanel.Visibility = Visibility.Collapsed;

                    if (_settings.ShowTranslation)
                    {
                        await _translationService.TranslateLyricsAsync(song.CacheKey, _currentLyrics, _settings.TranslationLanguage);
                    }

                    PopulateLyricsUI();
                    ApplyDisplayMode(_settings.DisplayMode);
                    ShowToast($"✅ Đã nạp {lines.Count} dòng từ {Path.GetFileName(filePath)}");
                }
                else
                {
                    ShowToast("File .lrc không có dòng đồng bộ hợp lệ");
                }
            }
            catch (Exception ex)
            {
                ShowToast("Lỗi đọc file: " + ex.Message);
            }
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
            if (_settings.DisplayMode != "SpotifyScroll") return;
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

        // Toolbar & Settings Handlers
        private async void BtnPlayPause_Click(object sender, RoutedEventArgs e) => await _smtcService.TogglePlayPauseAsync();
        private async void BtnNext_Click(object sender, RoutedEventArgs e) => await _smtcService.SkipNextAsync();

        private void BtnMode_Click(object sender, RoutedEventArgs e)
        {
            var next = _settings.DisplayMode == "MiniMode" ? "SpotifyScroll" : "MiniMode";
            ApplyDisplayMode(next);
            ChkMiniMode.IsChecked = next == "MiniMode";
            SettingsService.Save(_settings);
            ShowToast(next == "MiniMode" ? "📱 Chế độ Mini Mode (1-2 dòng)" : "🎵 Chế độ Spotify Lyrics");
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

        private void BtnPresetProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string preset)
            {
                ApplyPreset(preset);
            }
        }

        private void ChkSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            _settings.AdaptiveColor = ChkAdaptiveColor.IsChecked == true;
            _settings.AutoHideOnPause = ChkAutoHide.IsChecked == true;
            _settings.SaveOfflineCache = ChkOfflineCache.IsChecked == true;
            ApplyColorsAndSizes();
            SettingsService.Save(_settings);
        }

        private async void ChkTranslation_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            _settings.ShowTranslation = ChkShowTranslation.IsChecked == true;
            SettingsService.Save(_settings);

            var song = _smtcService.CurrentSong;
            if (_settings.ShowTranslation && _currentLyrics.Count > 0 && !song.IsEmpty)
            {
                await _translationService.TranslateLyricsAsync(song.CacheKey, _currentLyrics, _settings.TranslationLanguage);
            }
            PopulateLyricsUI();
            ApplyDisplayMode(_settings.DisplayMode);
        }

        private async void CmbTargetLang_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (CmbTargetLang.SelectedItem is ComboBoxItem item && item.Tag is string lang)
            {
                _settings.TranslationLanguage = lang;
                SettingsService.Save(_settings);

                var song = _smtcService.CurrentSong;
                if (_settings.ShowTranslation && _currentLyrics.Count > 0 && !song.IsEmpty)
                {
                    await _translationService.TranslateLyricsAsync(song.CacheKey, _currentLyrics, lang);
                    PopulateLyricsUI();
                    ApplyDisplayMode(_settings.DisplayMode);
                }
            }
        }

        private void ChkMiniMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ApplyDisplayMode(ChkMiniMode.IsChecked == true ? "MiniMode" : "SpotifyScroll");
            SettingsService.Save(_settings);
        }

        private void ChkVisualizer_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ApplyVisualizerState(ChkVisualizer.IsChecked == true);
            SettingsService.Save(_settings);
        }

        private void ChkStartup_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            bool enabled = ChkStartup.IsChecked == true;
            _settings.StartWithWindows = enabled;
            StartupHelper.SetStartup(enabled);
            _trayService?.UpdateStates(IsVisible, _settings.IsLocked, enabled);
            SettingsService.Save(_settings);
        }

        private void SliderFontSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded || _isInitializing) return;
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
                _settings.AdaptiveColor = false;
                ChkAdaptiveColor.IsChecked = false;
                _settings.ActiveColor = hex;
                TxtHexColor.Text = hex;
                ApplyColorsAndSizes();
                SettingsService.Save(_settings);
            }
        }

        private void BtnApplyHex_Click(object sender, RoutedEventArgs e)
        {
            var hex = TxtHexColor.Text?.Trim() ?? "";
            var color = ParseColor(hex, Colors.Transparent);
            if (color != Colors.Transparent)
            {
                _settings.AdaptiveColor = false;
                ChkAdaptiveColor.IsChecked = false;
                _settings.ActiveColor = hex;
                ApplyColorsAndSizes();
                SettingsService.Save(_settings);
                ShowToast($"Đã áp dụng màu: {hex}");
            }
            else
            {
                ShowToast("Mã màu không hợp lệ!");
            }
        }

        private void SliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded || _isInitializing) return;
            _settings.InactiveOpacity = Math.Clamp(e.NewValue, 0.15, 0.85);
            TxtOpacityPreview.Text = $"{(int)(_settings.InactiveOpacity * 100)}%";
            ApplyColorsAndSizes();
            SettingsService.Save(_settings);
        }

        private async void BtnImportLrc_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "LRC Lyrics (*.lrc)|*.lrc|All Files (*.*)|*.*",
                Title = "Chọn file lời bài hát (.lrc)"
            };
            if (dlg.ShowDialog() == true)
            {
                await LoadCustomLrcAsync(dlg.FileName);
            }
        }

        private void BtnSaveSpDc_Click(object sender, RoutedEventArgs e)
        {
            _settings.SpotifySpDcCookie = TxtSpDcCookie.Text?.Trim() ?? "";
            SettingsService.Save(_settings);
            var song = _smtcService.CurrentSong;
            if (!song.IsEmpty)
            {
                _lyricsService.InvalidateMemoryCache(song);
                OnSongChanged(song);
            }
            ShowToast("Đã lưu Spotify sp_dc Cookie");
        }

        private void BtnFontUp_Click(object sender, RoutedEventArgs e) => SliderFontSize.Value = Math.Min(48, _settings.FontSize + 2);
        private void BtnFontDown_Click(object sender, RoutedEventArgs e) => SliderFontSize.Value = Math.Max(18, _settings.FontSize - 2);

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

        private void BtnLock_Click(object sender, RoutedEventArgs e) => ToggleLock();
        private void BtnFloatingUnlock_Click(object sender, MouseButtonEventArgs e) => ToggleLock();

        private void BtnMinimizeTray_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            _trayService?.UpdateStates(false, _settings.IsLocked, _settings.StartWithWindows);
        }

        private void BtnExitApp_Click(object sender, RoutedEventArgs e) => ExitApplication();

        private void ExitApplication()
        {
            _isExiting = true;
            Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExiting)
            {
                // Keep running in system tray unless explicitly exited
                e.Cancel = true;
                Hide();
                _trayService?.UpdateStates(false, _settings.IsLocked, _settings.StartWithWindows);
                return;
            }

            _syncTimer?.Stop();
            _lyricsCts?.Cancel();
            _scrollResumeTimer?.Stop();
            _toastTimer?.Stop();
            _visualizerService.Dispose();
            _trayService?.Dispose();

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
