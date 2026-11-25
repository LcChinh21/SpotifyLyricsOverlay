using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
            Timeout = TimeSpan.FromSeconds(7)
        };

        static LyricsService()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SpotifyDesktopLyricsOverlay/1.1 (Windows NT 10.0; Win64; x64)");
        }

        private static readonly string CacheFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SpotifyLyricsOverlay",
            "LyricsCache"
        );

        private readonly ConcurrentDictionary<string, List<LyricLine>> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Regex _lrcRegex = new(@"\[(\d{1,2}):(\d{2})(?:\.(\d{1,3}))?\](.*)", RegexOptions.Compiled);
        private readonly Regex _cleanRegex = new(@"(\(feat\..*?\)|\[feat\..*?\]|\(ft\..*?\)|\[ft\..*?\]|\(with.*?\)|\[official.*?\]|\(official.*?\]|\(music video\)|\(lyric video\)|\(audio\)|\(remastered.*?\)|\- remastered.*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private string? _cachedSpotifyToken;
        private DateTimeOffset _spotifyTokenExpiry = DateTimeOffset.MinValue;
        private string? _cachedSpDcUsed;

        public string LastSource { get; private set; } = string.Empty;

        public async Task<List<LyricLine>> GetLyricsAsync(
            SongInfo song,
            bool useOfflineCache = true,
            string? spotifySpDcCookie = null,
            CancellationToken cancellationToken = default)
        {
            if (song.IsEmpty) return new List<LyricLine>();

            var key = song.CacheKey;
            if (_cache.TryGetValue(key, out var cachedLyrics))
            {
                return cachedLyrics;
            }

            List<LyricLine>? lyrics = null;

            // 1. Check local offline .lrc cache
            if (useOfflineCache)
            {
                lyrics = await TryLoadLocalLrcAsync(song, cancellationToken);
                if (lyrics != null && lyrics.Count > 0)
                {
                    LastSource = "Offline Cache (.lrc)";
                    _cache[key] = lyrics;
                    return lyrics;
                }
            }

            // 2. Optional: Spotify internal Musixmatch endpoint via sp_dc cookie
            if (!string.IsNullOrWhiteSpace(spotifySpDcCookie))
            {
                lyrics = await FetchFromSpotifySpDcAsync(song, spotifySpDcCookie.Trim(), cancellationToken);
                if (lyrics != null && lyrics.Count > 0)
                {
                    LastSource = "Spotify / Musixmatch";
                }
            }

            // 3. Primary open source: LRCLIB exact match
            if (lyrics == null || lyrics.Count == 0)
            {
                lyrics = await FetchExactLyricsAsync(song, cancellationToken);
                if (lyrics != null && lyrics.Count > 0) LastSource = "LRCLIB";
            }

            // 4. LRCLIB search fallback
            if (lyrics == null || lyrics.Count == 0)
            {
                lyrics = await SearchLyricsAsync(song, cancellationToken);
                if (lyrics != null && lyrics.Count > 0) LastSource = "LRCLIB";
            }

            // 5. LRCLIB cleaned title fallback
            if (lyrics == null || lyrics.Count == 0)
            {
                var cleanTitle = _cleanRegex.Replace(song.Title, "").Trim();
                if (!string.Equals(cleanTitle, song.Title, StringComparison.OrdinalIgnoreCase))
                {
                    lyrics = await SearchLyricsByQueryAsync($"{cleanTitle} {song.Artist}", cancellationToken);
                    if (lyrics != null && lyrics.Count > 0) LastSource = "LRCLIB";
                }
            }

            // 6. NetEase Cloud Music fallback
            if (lyrics == null || lyrics.Count == 0)
            {
                lyrics = await FetchFromNetEaseAsync(song, cancellationToken);
                if (lyrics != null && lyrics.Count > 0) LastSource = "NetEase";
            }

            lyrics ??= new List<LyricLine>();

            if (lyrics.Count > 0)
            {
                _cache[key] = lyrics;
                if (useOfflineCache)
                {
                    await SaveLocalLrcAsync(song, lyrics);
                }
            }

            return lyrics;
        }

        public async Task<List<LyricLine>> ImportLrcFileAsync(SongInfo song, string filePath)
        {
            var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            var lines = ParseLrc(content);
            if (lines.Count > 0 && !song.IsEmpty)
            {
                _cache[song.CacheKey] = lines;
                LastSource = "Tệp .lrc thủ công";
                await SaveLocalLrcAsync(song, lines);
            }
            return lines;
        }

        public void InvalidateMemoryCache(SongInfo song)
        {
            if (!song.IsEmpty)
            {
                _cache.TryRemove(song.CacheKey, out _);
            }
        }

        private string GetLocalLrcPath(SongInfo song)
        {
            var safeName = song.CacheKey;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(c, '_');
            }
            return Path.Combine(CacheFolder, $"{safeName}.lrc");
        }

        private async Task<List<LyricLine>?> TryLoadLocalLrcAsync(SongInfo song, CancellationToken cancellationToken)
        {
            try
            {
                var path = GetLocalLrcPath(song);
                if (File.Exists(path))
                {
                    var text = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
                    var parsed = ParseLrc(text);
                    if (parsed.Count > 0) return parsed;
                }
            }
            catch { }
            return null;
        }

        private async Task SaveLocalLrcAsync(SongInfo song, List<LyricLine> lines)
        {
            try
            {
                if (!Directory.Exists(CacheFolder))
                {
                    Directory.CreateDirectory(CacheFolder);
                }

                var sb = new StringBuilder();
                foreach (var line in lines)
                {
                    sb.AppendLine(line.ToString());
                }
                await File.WriteAllTextAsync(GetLocalLrcPath(song), sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        private async Task<List<LyricLine>?> FetchFromSpotifySpDcAsync(SongInfo song, string spDc, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrEmpty(_cachedSpotifyToken) || _cachedSpDcUsed != spDc || DateTimeOffset.UtcNow >= _spotifyTokenExpiry)
                {
                    using var tokenReq = new HttpRequestMessage(HttpMethod.Get, "https://open.spotify.com/get_access_token?reason=transport&productType=web_player");
                    tokenReq.Headers.Add("Cookie", $"sp_dc={spDc}");
                    tokenReq.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                    using var tokenResp = await HttpClient.SendAsync(tokenReq, cancellationToken);
                    if (!tokenResp.IsSuccessStatusCode) return null;

                    var tokenJson = await tokenResp.Content.ReadAsStringAsync(cancellationToken);
                    using var tDoc = JsonDocument.Parse(tokenJson);
                    if (!tDoc.RootElement.TryGetProperty("accessToken", out var tokProp)) return null;

                    _cachedSpotifyToken = tokProp.GetString();
                    _cachedSpDcUsed = spDc;
                    if (tDoc.RootElement.TryGetProperty("accessTokenExpirationTimestampMs", out var expProp))
                    {
                        _spotifyTokenExpiry = DateTimeOffset.FromUnixTimeMilliseconds(expProp.GetInt64()).AddMinutes(-1);
                    }
                    else
                    {
                        _spotifyTokenExpiry = DateTimeOffset.UtcNow.AddMinutes(30);
                    }
                }

                if (string.IsNullOrEmpty(_cachedSpotifyToken)) return null;

                // Search Track ID
                var q = Uri.EscapeDataString($"{song.Title} {song.Artist}".Trim());
                using var searchReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.spotify.com/v1/search?q={q}&type=track&limit=1");
                searchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedSpotifyToken);

                using var searchResp = await HttpClient.SendAsync(searchReq, cancellationToken);
                if (!searchResp.IsSuccessStatusCode) return null;

                var searchJson = await searchResp.Content.ReadAsStringAsync(cancellationToken);
                using var sDoc = JsonDocument.Parse(searchJson);
                var items = sDoc.RootElement.GetProperty("tracks").GetProperty("items");
                if (items.GetArrayLength() == 0) return null;

                var trackId = items[0].GetProperty("id").GetString();
                if (string.IsNullOrEmpty(trackId)) return null;

                // Fetch Lyrics
                using var lyrReq = new HttpRequestMessage(HttpMethod.Get, $"https://spclient.wg.spotify.com/color-lyrics/v2/track/{trackId}?format=json&market=from_token");
                lyrReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedSpotifyToken);
                lyrReq.Headers.Add("app-platform", "WebPlayer");

                using var lyrResp = await HttpClient.SendAsync(lyrReq, cancellationToken);
                if (!lyrResp.IsSuccessStatusCode) return null;

                var lyrJson = await lyrResp.Content.ReadAsStringAsync(cancellationToken);
                using var lDoc = JsonDocument.Parse(lyrJson);
                if (!lDoc.RootElement.TryGetProperty("lyrics", out var lyricsObj)) return null;
                if (!lyricsObj.TryGetProperty("lines", out var linesArr)) return null;

                var result = new List<LyricLine>();
                foreach (var item in linesArr.EnumerateArray())
                {
                    var msStr = item.GetProperty("startTimeMs").GetString();
                    var words = item.GetProperty("words").GetString() ?? "";
                    if (long.TryParse(msStr, out var ms))
                    {
                        result.Add(new LyricLine(TimeSpan.FromMilliseconds(ms), words.Trim()));
                    }
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        private async Task<List<LyricLine>?> FetchFromNetEaseAsync(SongInfo song, CancellationToken cancellationToken)
        {
            try
            {
                var query = _cleanRegex.Replace($"{song.Title} {song.Artist}", "").Trim();
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://music.163.com/api/search/get");
                req.Headers.Add("Referer", "https://music.163.com/");
                req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["s"] = query,
                    ["type"] = "1",
                    ["offset"] = "0",
                    ["limit"] = "3"
                });

                using var resp = await HttpClient.SendAsync(req, cancellationToken);
                if (!resp.IsSuccessStatusCode) return null;

                var json = await resp.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("result", out var res) ||
                    !res.TryGetProperty("songs", out var songs) ||
                    songs.GetArrayLength() == 0)
                {
                    return null;
                }

                long songId = songs[0].GetProperty("id").GetInt64();
                using var lyrReq = new HttpRequestMessage(HttpMethod.Get, $"https://music.163.com/api/song/lyric?os=pc&id={songId}&lv=-1&kv=-1&tv=-1");
                lyrReq.Headers.Add("Referer", "https://music.163.com/");

                using var lyrResp = await HttpClient.SendAsync(lyrReq, cancellationToken);
                if (!lyrResp.IsSuccessStatusCode) return null;

                var lyrJson = await lyrResp.Content.ReadAsStringAsync(cancellationToken);
                using var lDoc = JsonDocument.Parse(lyrJson);
                if (lDoc.RootElement.TryGetProperty("lrc", out var lrcObj) &&
                    lrcObj.TryGetProperty("lyric", out var lyricProp) &&
                    lyricProp.ValueKind == JsonValueKind.String)
                {
                    var raw = lyricProp.GetString();
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        var parsed = ParseLrc(raw);
                        if (parsed.Count > 0) return parsed;
                    }
                }
            }
            catch { }
            return null;
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
            catch { }
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
            catch { }
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
                    time += TimeSpan.FromSeconds(5);
                }
            }
            return result;
        }

        public int GetActiveIndex(List<LyricLine> lyrics, TimeSpan position, double offsetSeconds = 0.0)
        {
            if (lyrics == null || lyrics.Count == 0) return -1;

            var target = position + TimeSpan.FromSeconds(offsetSeconds);
            if (target < lyrics[0].Time) return -1;

            int low = 0;
            int high = lyrics.Count - 1;
            int best = 0;

            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (lyrics[mid].Time <= target)
                {
                    best = mid;
                    low = mid + 1;
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
