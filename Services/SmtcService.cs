using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using SpotifyLyricsOverlay.Models;

namespace SpotifyLyricsOverlay.Services
{
    public class SmtcService
    {
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;

        private TimeSpan _lastPosition = TimeSpan.Zero;
        private DateTimeOffset _lastUpdatedTime = DateTimeOffset.UtcNow;
        private bool _isPlaying = false;
        private SongInfo _currentSong = new();

        public event Action<SongInfo>? SongChanged;
        public event Action<bool>? PlaybackStateChanged;
        public event Action<TimeSpan>? TimelineUpdated;

        public SongInfo CurrentSong => _currentSong;
        public bool IsPlaying => _isPlaying;

        public async Task InitializeAsync()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_manager != null)
                {
                    _manager.SessionsChanged += OnSessionsChanged;
                    _manager.CurrentSessionChanged += OnCurrentSessionChanged;
                    await UpdateCurrentSessionAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to init SMTC: {ex.Message}");
            }
        }

        private async void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        {
            await UpdateCurrentSessionAsync();
        }

        private async void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            await UpdateCurrentSessionAsync();
        }

        private async Task UpdateCurrentSessionAsync()
        {
            if (_manager == null) return;

            var sessions = _manager.GetSessions();
            // Prioritize Spotify session
            var spotifySession = sessions.FirstOrDefault(s =>
                (s.SourceAppUserModelId ?? "").Contains("Spotify", StringComparison.OrdinalIgnoreCase));

            var targetSession = spotifySession ?? _manager.GetCurrentSession();

            if (_currentSession != targetSession)
            {
                if (_currentSession != null)
                {
                    _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                    _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                }

                _currentSession = targetSession;

                if (_currentSession != null)
                {
                    _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                    _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
                }
            }

            await RefreshMediaPropertiesAsync();
            RefreshPlaybackAndTimeline();
        }

        private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            await RefreshMediaPropertiesAsync();
        }

        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            RefreshPlaybackAndTimeline();
        }

        private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            RefreshPlaybackAndTimeline();
        }

        private async Task RefreshMediaPropertiesAsync()
        {
            if (_currentSession == null)
            {
                _currentSong = new SongInfo();
                SongChanged?.Invoke(_currentSong);
                return;
            }

            try
            {
                var media = await _currentSession.TryGetMediaPropertiesAsync();
                if (media == null) return;

                BitmapSource? thumb = null;
                if (media.Thumbnail != null)
                {
                    try
                    {
                        using var streamRef = await media.Thumbnail.OpenReadAsync();
                        using var netStream = streamRef.AsStreamForRead();
                        using var mem = new MemoryStream();
                        await netStream.CopyToAsync(mem);
                        mem.Position = 0;

                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = mem;
                        bmp.EndInit();
                        bmp.Freeze();
                        thumb = bmp;
                    }
                    catch
                    {
                        // Ignore thumbnail load failure
                    }
                }

                var timeline = _currentSession.GetTimelineProperties();

                _currentSong = new SongInfo
                {
                    Title = media.Title ?? string.Empty,
                    Artist = media.Artist ?? string.Empty,
                    Album = media.AlbumTitle ?? string.Empty,
                    Duration = timeline?.EndTime ?? TimeSpan.Zero,
                    Position = timeline?.Position ?? TimeSpan.Zero,
                    IsPlaying = _isPlaying,
                    Thumbnail = thumb
                };

                SongChanged?.Invoke(_currentSong);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching media properties: {ex.Message}");
            }
        }

        private void RefreshPlaybackAndTimeline()
        {
            if (_currentSession == null) return;

            try
            {
                var playback = _currentSession.GetPlaybackInfo();
                var timeline = _currentSession.GetTimelineProperties();

                _isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                PlaybackStateChanged?.Invoke(_isPlaying);

                if (timeline != null)
                {
                    _lastPosition = timeline.Position;
                    _lastUpdatedTime = timeline.LastUpdatedTime;
                    _currentSong.Duration = timeline.EndTime;
                    TimelineUpdated?.Invoke(GetEstimatedCurrentPosition());
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating timeline: {ex.Message}");
            }
        }

        public TimeSpan GetEstimatedCurrentPosition()
        {
            if (!_isPlaying)
            {
                return _lastPosition;
            }

            var elapsed = DateTimeOffset.UtcNow - _lastUpdatedTime;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            return _lastPosition + elapsed;
        }

        public async Task<bool> TogglePlayPauseAsync()
        {
            if (_currentSession == null) return false;
            return await _currentSession.TryTogglePlayPauseAsync();
        }

        public async Task<bool> SkipNextAsync()
        {
            if (_currentSession == null) return false;
            return await _currentSession.TrySkipNextAsync();
        }

        public async Task<bool> SkipPreviousAsync()
        {
            if (_currentSession == null) return false;
            return await _currentSession.TrySkipPreviousAsync();
        }

        public async Task<bool> SeekToAsync(TimeSpan position)
        {
            if (_currentSession == null) return false;
            try
            {
                return await _currentSession.TryChangePlaybackPositionAsync(position.Ticks);
            }
            catch
            {
                return false;
            }
        }
    }
}
