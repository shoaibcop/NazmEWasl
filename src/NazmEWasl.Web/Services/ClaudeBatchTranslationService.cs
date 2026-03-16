using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NazmEWasl.Web.Data;
using NazmEWasl.Web.Models.Domain;
using NazmEWasl.Web.Services.Interfaces;

namespace NazmEWasl.Web.Services;

/// <summary>Implements Anthropic's native batch API (/v1/messages/batches).</summary>
public class ClaudeBatchTranslationService : IBatchTranslationService
{
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClaudeBatchTranslationService> _logger;

    public ClaudeBatchTranslationService(
        IConfiguration config,
        AppDbContext db,
        IHttpClientFactory httpClientFactory,
        ILogger<ClaudeBatchTranslationService> logger)
    {
        _config = config;
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> SubmitBatchAsync(Song song, IReadOnlyList<string> targetLanguages)
    {
        var apiKey    = _config["Anthropic:ApiKey"] ?? "";
        var model     = _config["Anthropic:Model"] ?? "claude-sonnet-4-6";
        var maxTokens = int.Parse(_config["Anthropic:MaxTokens"] ?? "8000");

        var verses = await _db.Verses
            .Where(v => v.Song.SongId == song.SongId)
            .OrderBy(v => v.VerseNumber)
            .ToListAsync();

        var persianVerses = verses.Select(v => v.PersianText).ToList();
        var fullText      = string.Join("\n", persianVerses);
        var systemPrompt  = TranslationHelpers.BuildSystemPrompt(targetLanguages);

        // One request per verse in the batch
        var requests = verses.Select(v =>
        {
            var userPrompt =
                $"Song: \"{song.Title}\" by {song.Artist}\n\n" +
                $"=== Full Poem (context) ===\n{fullText}\n\n" +
                $"=== Translate Verse {v.VerseNumber} ===\n{v.PersianText}\n\n" +
                $"Translate into: {string.Join(", ", targetLanguages)}\n" +
                $"Return a single JSON object (not array) with verse_number={v.VerseNumber}.";

            return new
            {
                custom_id = $"verse_{v.VerseNumber}",
                @params = new
                {
                    model,
                    max_tokens = maxTokens,
                    system = systemPrompt,
                    messages = new[] { new { role = "user", content = userPrompt } }
                }
            };
        }).ToList();

        var body = JsonSerializer.Serialize(new { requests });

        var client = _httpClientFactory.CreateClient("Anthropic");
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/messages/batches");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        req.Headers.Add("x-api-key", apiKey);

        var response = await client.SendAsync(req);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Anthropic batch submit failed {(int)response.StatusCode}: {responseBody}");

        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("id").GetString()
               ?? throw new InvalidOperationException("No batch id in Anthropic response.");
    }

    public async Task<bool> PollAndApplyResultsAsync(TranslationBatch batch)
    {
        var apiKey = _config["Anthropic:ApiKey"] ?? "";

        var client = _httpClientFactory.CreateClient("Anthropic");
        using var pollReq = new HttpRequestMessage(HttpMethod.Get, $"v1/messages/batches/{batch.ExternalBatchId}");
        pollReq.Headers.Add("x-api-key", apiKey);
        var response = await client.SendAsync(pollReq);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Anthropic batch poll failed {Status}: {Body}", (int)response.StatusCode, body);
            return false;
        }

        using var doc = JsonDocument.Parse(body);
        var processingStatus = doc.RootElement.GetProperty("processing_status").GetString();

        if (processingStatus != "ended")
            return false;

        // Download results stream
        var resultsUrl = doc.RootElement.GetProperty("results_url").GetString();
        if (string.IsNullOrEmpty(resultsUrl)) return false;

        var resultsResponse = await client.GetAsync(resultsUrl);
        var resultsText = await resultsResponse.Content.ReadAsStringAsync();

        var song = await _db.Songs
            .Include(s => s.Verses)
            .FirstOrDefaultAsync(s => s.Id == batch.SongId);

        if (song == null) return false;

        // Results are JSONL — one JSON object per line
        foreach (var line in resultsText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var lineDoc = JsonDocument.Parse(line);
                var customId = lineDoc.RootElement.GetProperty("custom_id").GetString() ?? "";
                if (!customId.StartsWith("verse_")) continue;

                if (!int.TryParse(customId["verse_".Length..], out var verseNumber)) continue;

                var resultType = lineDoc.RootElement.GetProperty("result").GetProperty("type").GetString();
                if (resultType != "succeeded") continue;

                var messageContent = lineDoc.RootElement
                    .GetProperty("result")
                    .GetProperty("message")
                    .GetProperty("content")[0]
                    .GetProperty("text")
                    .GetString() ?? "";

                var json = TranslationHelpers.StripFences(messageContent);
                var entry = JsonSerializer.Deserialize<TranslationEntry>(json, TranslationHelpers.JsonOpts);
                if (entry == null) continue;

                var verse = song.Verses.FirstOrDefault(v => v.VerseNumber == verseNumber);
                if (verse == null) continue;

                if (entry.RomanUrdu   != null) verse.RomanUrdu   = entry.RomanUrdu;
                if (entry.EnglishText != null) verse.EnglishText = entry.EnglishText;
                if (entry.HindiText   != null) verse.HindiText   = entry.HindiText;
                if (entry.Keywords    != null && entry.Keywords.Count > 0)
                    verse.KeywordsJson = JsonSerializer.Serialize(
                        entry.Keywords.Select(k => new { word = k.Word, meaning = k.Meaning }));
                verse.LastEditedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing batch result line.");
            }
        }

        song.Status = SongStatus.AwaitingReview;
        await _db.SaveChangesAsync();
        return true;
    }

    private record TranslationEntry(
        int VerseNumber,
        string? RomanUrdu,
        string? EnglishText,
        string? HindiText,
        List<KeywordGlossEntry>? Keywords = null);

    private record KeywordGlossEntry(string Word, string Meaning);
}
