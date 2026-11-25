using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpotifyLyricsOverlay.Models;

namespace SpotifyLyricsOverlay.Services
{
    public class TranslationService
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        static TranslationService()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        }

        private readonly ConcurrentDictionary<string, List<string?>> _translationCache = new(StringComparer.OrdinalIgnoreCase);

        public async Task TranslateLyricsAsync(
            string songKey,
            List<LyricLine> lines,
            string targetLang,
            CancellationToken cancellationToken = default)
        {
            if (lines == null || lines.Count == 0 || string.IsNullOrWhiteSpace(targetLang))
                return;

            string cacheKey = $"{songKey}::{targetLang}";
            if (_translationCache.TryGetValue(cacheKey, out var cached))
            {
                if (cached.Count == lines.Count)
                {
                    for (int i = 0; i < lines.Count; i++)
                    {
                        lines[i].TranslatedText = cached[i];
                    }
                    return;
                }
            }

            // Collect non-empty indices to translate
            var indices = new List<int>();
            var texts = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                var txt = lines[i].Text?.Trim();
                if (!string.IsNullOrEmpty(txt) && txt != "♪" && txt != "♪ ♫ ♪")
                {
                    indices.Add(i);
                    texts.Add(txt.Replace("\r", " ").Replace("\n", " "));
                }
            }

            if (texts.Count == 0) return;

            var translatedByIndex = new Dictionary<int, string>();
            const int chunkSize = 20;

            for (int start = 0; start < texts.Count; start += chunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int count = Math.Min(chunkSize, texts.Count - start);
                var chunk = texts.GetRange(start, count);
                var (detectedLang, translatedChunk) = await TranslateBatchAsync(chunk, targetLang, cancellationToken);

                // Skip translation if song is already in the target language
                if (string.Equals(detectedLang, targetLang, StringComparison.OrdinalIgnoreCase))
                {
                    var emptyList = Enumerable.Repeat<string?>(null, lines.Count).ToList();
                    _translationCache[cacheKey] = emptyList;
                    return;
                }

                if (translatedChunk != null && translatedChunk.Count == chunk.Count)
                {
                    for (int j = 0; j < count; j++)
                    {
                        int origIdx = indices[start + j];
                        var tr = translatedChunk[j].Trim();
                        if (!string.Equals(tr, chunk[j], StringComparison.OrdinalIgnoreCase))
                        {
                            translatedByIndex[origIdx] = tr;
                        }
                    }
                }
            }

            var cachedResult = new List<string?>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                translatedByIndex.TryGetValue(i, out var tr);
                lines[i].TranslatedText = tr;
                cachedResult.Add(tr);
            }

            _translationCache[cacheKey] = cachedResult;
        }

        private async Task<(string? detectedLang, List<string>? lines)> TranslateBatchAsync(
            List<string> lines,
            string targetLang,
            CancellationToken cancellationToken)
        {
            try
            {
                string joined = string.Join("\n", lines);
                string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={Uri.EscapeDataString(targetLang)}&dt=t&q={Uri.EscapeDataString(joined)}";

                using var resp = await HttpClient.GetAsync(url, cancellationToken);
                if (!resp.IsSuccessStatusCode) return (null, null);

                string json = await resp.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 1)
                    return (null, null);

                string? detectedLang = null;
                if (root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String)
                {
                    detectedLang = root[2].GetString();
                }

                var sb = new StringBuilder();
                if (root[0].ValueKind == JsonValueKind.Array)
                {
                    foreach (var seg in root[0].EnumerateArray())
                    {
                        if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0 && seg[0].ValueKind == JsonValueKind.String)
                        {
                            sb.Append(seg[0].GetString());
                        }
                    }
                }

                var resultLines = sb.ToString()
                    .Split('\n')
                    .Select(l => l.TrimEnd('\r'))
                    .ToList();

                return (detectedLang, resultLines);
            }
            catch
            {
                return (null, null);
            }
        }
    }
}
