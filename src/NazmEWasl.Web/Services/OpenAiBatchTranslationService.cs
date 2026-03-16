using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NazmEWasl.Web.Data;
using NazmEWasl.Web.Models.Domain;
using NazmEWasl.Web.Services.Interfaces;

namespace NazmEWasl.Web.Services;

/// <summary>Implements OpenAI's native batch API (JSONL upload + /v1/batches).</summary>
public class OpenAiBatchTranslationService : IBatchTranslationService
{
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenAiBatchTranslationService> _logger;

    public OpenAiBatchTranslationService(
        IConfiguration config,
        AppDbContext db,
        IHttpClientFactory httpClientFactory,
        ILogger<OpenAiBatchTranslationService> logger)
    {
        _config = config;
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> SubmitBatchAsync(Song song, IReadOnlyList<string> targetLanguages)
    {
        var apiKey    = _config["OpenAI:ApiKey"] ?? "";
        var model     = _config["OpenAI:Model"] ?? "gpt-4o";
        var maxTokens = int.Parse(_config["OpenAI:MaxTokens"] ?? "8000");

        var verses = await _db.Verses
            .Where(v => v.Song.SongId == song.SongId)
            .OrderBy(v => v.VerseNumber)
            .ToListAsync();

        var fullText     = string.Join("\n", verses.Select(v => v.PersianText));
        var systemPrompt = TranslationHelpers.BuildSystemPrompt(targetLanguages);

        var lines = verses.Select(v =>
        {
            var userPrompt =
                $"Song: \"{song.Title}\" by {song.Artist}\n\n" +
                $"=== Full Poem (context) ===\n{fullText}\n\n" +
                $"=== Translate Verse {v.VerseNumber} ===\n{v.PersianText}\n\n" +
                $"Translate into: {string.Join(", ", targetLanguages)}\n" +
                $"Return a single JSON object (not array) with verse_number={v.VerseNumber}.";

            return JsonSerializer.Serialize(new
            {
                custom_id = $"verse_{v.VerseNumber}",
                method = "POST",
                url = "/v1/chat/completions",
                body = new
                {
                    model,
                    max_tokens = maxTokens,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user",   content = userPrompt }
                    }
                }
            });
        });

        var jsonl = string.Join("\n", lines);

        var client = _httpClientFactory.CreateClient("OpenAI");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        // Upload JSONL as a file
        using var uploadForm = new MultipartFormDataContent();
        uploadForm.Add(new StringContent("batch"), "purpose");
        uploadForm.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(jsonl))
            { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/jsonl") } },
            "file", "batch_requests.jsonl");

        var uploadResponse = await client.PostAsync("v1/files", uploadForm);
        var uploadBody     = await uploadResponse.Content.ReadAsStringAsync();

        if (!uploadResponse.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI file upload failed {(int)uploadResponse.StatusCode}: {uploadBody}");

        using var uploadDoc = JsonDocument.Parse(uploadBody);
        var fileId = uploadDoc.RootElement.GetProperty("id").GetString()
                     ?? throw new InvalidOperationException("No file id from OpenAI upload.");

        // Create batch
        var batchBody = JsonSerializer.Serialize(new
        {
            input_file_id = fileId,
            endpoint = "/v1/chat/completions",
            completion_window = "24h"
        });

        using var batchReq = new HttpRequestMessage(HttpMethod.Post, "v1/batches");
        batchReq.Content = new StringContent(batchBody, Encoding.UTF8, "application/json");

        var batchResponse = await client.SendAsync(batchReq);
        var batchRespBody = await batchResponse.Content.ReadAsStringAsync();

        if (!batchResponse.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI batch create failed {(int)batchResponse.StatusCode}: {batchRespBody}");

        using var batchDoc = JsonDocument.Parse(batchRespBody);
        return batchDoc.RootElement.GetProperty("id").GetString()
               ?? throw new InvalidOperationException("No batch id in OpenAI response.");
    }

    public async Task<bool> PollAndApplyResultsAsync(TranslationBatch batch)
    {
        var apiKey = _config["OpenAI:ApiKey"] ?? "";

        var client = _httpClientFactory.CreateClient("OpenAI");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        var response = await client.GetAsync($"v1/batches/{batch.ExternalBatchId}");
        var body     = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenAI batch poll failed {Status}: {Body}", (int)response.StatusCode, body);
            return false;
        }

        using var doc    = JsonDocument.Parse(body);
        var status       = doc.RootElement.GetProperty("status").GetString();

        if (status != "completed") return false;

        var outputFileId = doc.RootElement.GetProperty("output_file_id").GetString();
        if (string.IsNullOrEmpty(outputFileId)) return false;

        var fileResponse = await client.GetAsync($"v1/files/{outputFileId}/content");
        var fileContent  = await fileResponse.Content.ReadAsStringAsync();

        var song = await _db.Songs
            .Include(s => s.Verses)
            .FirstOrDefaultAsync(s => s.Id == batch.SongId);

        if (song == null) return false;

        foreach (var line in fileContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var lineDoc = JsonDocument.Parse(line);
                var customId = lineDoc.RootElement.GetProperty("custom_id").GetString() ?? "";
                if (!customId.StartsWith("verse_")) continue;
                if (!int.TryParse(customId["verse_".Length..], out var verseNumber)) continue;

                var messageContent = lineDoc.RootElement
                    .GetProperty("response")
                    .GetProperty("body")
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "";

                var json  = TranslationHelpers.StripFences(messageContent);
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
                _logger.LogWarning(ex, "Error parsing OpenAI batch result line.");
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
