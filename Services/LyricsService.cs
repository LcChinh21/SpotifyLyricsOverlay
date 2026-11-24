using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SpotifyLyricsOverlay.Models;

namespace SpotifyLyricsOverlay.Services
{
    public class LyricsService
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(6)
        };

        static LyricsService()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SpotifyDesktopLyricsOverlay/1.0 (Windows NT 10.0; Win64; x64)");
        }

        private readonly ConcurrentDictionary<string, List<LyricLine>> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Regex _lrcRegex = new(@"\[(\d{1,2}):(\d{2})(?:\.(\d{1,3}))?\](.*)", RegexOptions.Compiled);
        private readonly Regex _cleanRegex = new(@"(\(feat\..*?\)|\[feat\..*?\]|\(ft\..*?\)|\[ft\..*?\]|\(with.*?\)|\[official.*?\]|\(official.*?\]|\(music video\)|\(lyric video\)|\(audio\)|\(remastered.*?\)|\- remastered.*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public async Task<List<LyricLine>> GetLyricsAsync(SongInfo song, CancellationToken cancellationToken = default)
        {
            if (song.IsEmpty) return new List<LyricLine>();

            var key = song.CacheKey;
            if (_cache.TryGetValue(key, out var cachedLyrics))
            {
                return cachedLyrics;
            }

            // 1. Try exact match from LRCLIB
            var lyrics = await FetchExactLyricsAsync(song, cancellationToken);

            // 2. If not found or empty synced lyrics, try search fallback
            if (lyrics == null || lyrics.Count == 0)
            {
                lyrics = await SearchLyricsAsync(song, cancellationToken);
            }

            // 3. Fallback: if still nothing, try cleaned title
            if (lyrics == null || lyrics.Count == 0)
            {
                var cleanTitle = _cleanRegex.Replace(song.Title, "").Trim();
                if (!string.Equals(cleanTitle, song.Title, StringComparison.OrdinalIgnoreCase))
                {
                    lyrics = await SearchLyricsByQueryAsync($"{cleanTitle} {song.Artist}", cancellationToken);
                }
            }

            lyrics ??= new List<LyricLine>();

            _cache[key] = lyrics;
            return lyrics;
        }

        private async Task<List<LyricLine>?> FetchExactLyricsAsync(SongInfo song, CancellationToken cancellationToken)
        {
            try
            {
                var url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(song.Title)}&artist_name={Uri.EscapeDataString(song.Artist)}";
                if (!string.IsNullOrWhiteSpace(song.Album))
                {
                    url += $"&album_name={Uri.EscapeDataString(song.Album)}";
                }
                if (song.Duration > TimeSpan.Zero)
                {
                    url += $"&duration={Math.Round(song.Duration.TotalSeconds)}";
                }

                using var response = await HttpClient.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("syncedLyrics", out var syncedProp) && syncedProp.ValueKind == JsonValueKind.String)
                    {
                        var rawLyrics = syncedProp.GetString();
                        if (!string.IsNullOrWhiteSpace(rawLyrics))
                        {
                            return ParseLrc(rawLyrics);
                        }
                    }

                    // Fallback to plain lyrics if synced not available
                    if (root.TryGetProperty("plainLyrics", out var plainProp) && plainProp.ValueKind == JsonValueKind.String)
                    {
                        var plainText = plainProp.GetString();
                        if (!string.IsNullOrWhiteSpace(plainText))
                        {
                            return CreateUnsyncedList(plainText);
                        }
                    }
                }
            }
            catch
            {
                // Network or cancellation exception
            }
            return null;
        }

        private async Task<List<LyricLine>?> SearchLyricsAsync(SongInfo song, CancellationToken cancellationToken)
        {
            return await SearchLyricsByQueryAsync($"{song.Title} {song.Artist}".Trim(), cancellationToken);
        }

        private async Task<List<LyricLine>?> SearchLyricsByQueryAsync(string query, CancellationToken cancellationToken)
        {
            try
            {
                var url = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(query)}";
                using var response = await HttpClient.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in doc.RootElement.EnumerateArray())
                        {
                            if (item.TryGetProperty("syncedLyrics", out var syncedProp) && syncedProp.ValueKind == JsonValueKind.String)
                            {
                                var rawLyrics = syncedProp.GetString();
                                if (!string.IsNullOrWhiteSpace(rawLyrics))
                                {
                                    return ParseLrc(rawLyrics);
                                }
                            }
                        }

                        // Plain lyrics fallback from first item if no synced
                        foreach (var item in doc.RootElement.EnumerateArray())
                        {
                            if (item.TryGetProperty("plainLyrics", out var plainProp) && plainProp.ValueKind == JsonValueKind.String)
                            {
                                var plain = plainProp.GetString();
                                if (!string.IsNullOrWhiteSpace(plain))
                                {
                                    return CreateUnsyncedList(plain);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Network error or cancelled
            }
            return null;
        }

        public List<LyricLine> ParseLrc(string lrcContent)
        {
            var lines = new List<LyricLine>();
            using var reader = new StringReader(lrcContent);
            string? line;

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // Match [mm:ss.xx] or [mm:ss]
                var matches = _lrcRegex.Matches(line);
                if (matches.Count > 0)
                {
                    foreach (Match match in matches)
                    {
                        var min = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                        var sec = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                        var ms = 0;
                        if (match.Groups[3].Success)
                        {
                            var msStr = match.Groups[3].Value;
                            if (msStr.Length == 2) ms = int.Parse(msStr, CultureInfo.InvariantCulture) * 10;
                            else if (msStr.Length == 3) ms = int.Parse(msStr, CultureInfo.InvariantCulture);
                            else if (msStr.Length == 1) ms = int.Parse(msStr, CultureInfo.InvariantCulture) * 100;
                        }

                        var time = TimeSpan.FromMinutes(min) + TimeSpan.FromSeconds(sec) + TimeSpan.FromMilliseconds(ms);
                        var text = match.Groups[4].Value.Trim();

                        lines.Add(new LyricLine(time, text));
                    }
                }
            }

            lines.Sort((a, b) => a.Time.CompareTo(b.Time));
            return lines;
        }

        private List<LyricLine> CreateUnsyncedList(string plainLyrics)
        {
            var result = new List<LyricLine>();
            using var reader = new StringReader(plainLyrics);
            string? line;
            var time = TimeSpan.Zero;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (!string.IsNullOrEmpty(line))
                {
                    result.Add(new LyricLine(time, line));
                    time += TimeSpan.FromSeconds(5); // Arbitrary step
                }
            }
            return result;
        }

        public int GetActiveIndex(List<LyricLine> lyrics, TimeSpan position, double offsetSeconds = 0.0)
        {
            if (lyrics == null || lyrics.Count == 0) return -1;

            var target = position + TimeSpan.FromSeconds(offsetSeconds);
            if (target < lyrics[0].Time) return -1;

            // Binary search or linear scan for current line
            int low = 0;
            int high = lyrics.Count - 1;
            int best = 0;

            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (lyrics[mid].Time <= target)
                {
                    best = mid;
                    low = mid + 1; // Try to find later line that still <= target
                }
                else
                {
                    high = mid - 1;
                }
            }

            return best;
        }
    }
}
