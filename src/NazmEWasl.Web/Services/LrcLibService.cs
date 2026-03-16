using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using NazmEWasl.Web.Services.Interfaces;

namespace NazmEWasl.Web.Services;

public class LrcLibService : ILrcLibService
{
    private readonly HttpClient _http;
    private readonly ILogger<LrcLibService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public LrcLibService(HttpClient http, ILogger<LrcLibService> logger)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://lrclib.net");
        _http.Timeout = TimeSpan.FromSeconds(10);
        _logger = logger;
    }

    public async Task<LrcLibResult?> GetLyricsAsync(string title, string artist)
    {
        // Try direct lookup first (exact match)
        var getUrl = $"/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        try
        {
            var res = await _http.GetAsync(getUrl);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadFromJsonAsync<LrcLibResponse>(JsonOpts);
                if (body != null && (!string.IsNullOrWhiteSpace(body.SyncedLyrics) || !string.IsNullOrWhiteSpace(body.PlainLyrics)))
                    return new LrcLibResult(body.TrackName, body.ArtistName, body.SyncedLyrics, body.PlainLyrics);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "lrclib direct GET failed for \"{Title}\" / \"{Artist}\"", title, artist);
        }

        // Fall back to search
        var searchUrl = $"/api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        try
        {
            var res = await _http.GetAsync(searchUrl);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("lrclib search returned {StatusCode} for \"{Title}\" / \"{Artist}\"", res.StatusCode, title, artist);
                return null;
            }

            var results = await res.Content.ReadFromJsonAsync<List<LrcLibResponse>>(JsonOpts);
            if (results == null || results.Count == 0) return null;

            // Prefer results with synced lyrics, fall back to plain-only
            var best = results.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.SyncedLyrics))
                    ?? results.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.PlainLyrics));

            if (best == null) return null;

            return new LrcLibResult(best.TrackName, best.ArtistName, best.SyncedLyrics, best.PlainLyrics);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "lrclib search failed for \"{Title}\" / \"{Artist}\"", title, artist);
            return null;
        }
    }

    public List<ParsedVerse> ParseLyrics(LrcLibResult result)
    {
        if (result.HasSyncedLyrics)
            return ParseSynced(result.SyncedLyrics!);

        return ParsePlain(result.PlainLyrics ?? string.Empty);
    }

    private static List<ParsedVerse> ParseSynced(string syncedLyrics)
    {
        // Parse optional [offset:N] header tag (N in milliseconds)
        // LRC convention: positive = lyrics are late, add N to startMs to correct
        var offsetMs = 0;
        var offsetMatch = Regex.Match(syncedLyrics, @"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase);
        if (offsetMatch.Success && int.TryParse(offsetMatch.Groups[1].Value, out var rawOffset))
            offsetMs = rawOffset;

        var pattern = new Regex(@"^\[(\d+):(\d+\.\d+)\]\s*(.*)$", RegexOptions.Multiline);
        var raw = new List<(int startMs, string text)>();

        foreach (Match m in pattern.Matches(syncedLyrics))
        {
            var minutes = int.Parse(m.Groups[1].Value);
            var seconds = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            var text = m.Groups[3].Value.Trim();
            if (string.IsNullOrWhiteSpace(text)) continue; // skip instrumental/empty markers
            raw.Add(((int)((minutes * 60 + seconds) * 1000) + offsetMs, text));
        }

        var verses = new List<ParsedVerse>();
        for (int i = 0; i < raw.Count; i++)
        {
            var endMs = i + 1 < raw.Count ? raw[i + 1].startMs : raw[i].startMs + 5000;
            verses.Add(new ParsedVerse(i + 1, raw[i].text, raw[i].startMs, endMs));
        }
        return verses;
    }

    private static List<ParsedVerse> ParsePlain(string plainLyrics)
    {
        var normalised = plainLyrics.Replace("\r\n", "\n").Replace("\r", "\n");
        var separator = normalised.Contains("\n\n") ? "\n\n" : "\n";
        var lines = normalised
            .Split(separator, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        const int gapMs = 8000;
        var verses = new List<ParsedVerse>();
        for (int i = 0; i < lines.Count; i++)
            verses.Add(new ParsedVerse(i + 1, lines[i], i * gapMs, (i + 1) * gapMs - 500));
        return verses;
    }

    private record LrcLibResponse(
        string TrackName,
        string ArtistName,
        string? AlbumName,
        string? SyncedLyrics,
        string? PlainLyrics);
}
