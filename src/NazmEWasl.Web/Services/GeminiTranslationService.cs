using System.Text.Json;
using NazmEWasl.Web.Services.Interfaces;

namespace NazmEWasl.Web.Services;

public class GeminiTranslationService : ITranslationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<GeminiTranslationService> _logger;

    public GeminiTranslationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<GeminiTranslationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<List<VerseTranslation>> TranslateVersesAsync(
        List<string> persianVerses,
        string songTitle,
        string artist,
        string fullSongText,
        IReadOnlyList<string> targetLanguages)
    {
        var numberedVerses = string.Join("\n",
            persianVerses.Select((v, i) => $"{i + 1}. {v}"));

        var fullPrompt =
            TranslationHelpers.BuildSystemPrompt(targetLanguages) + "\n\n" +
            $"Song: \"{songTitle}\" by {artist}\n\n" +
            $"=== Full Poem — Read First ===\n{fullSongText}\n\n" +
            $"=== Translate Verse by Verse ===\n{numberedVerses}\n\n" +
            $"Translate into: {string.Join(", ", targetLanguages)}";

        try
        {
            var raw = await CallGeminiAsync(fullPrompt);
            var json = TranslationHelpers.StripFences(raw);

            var entries = JsonSerializer.Deserialize<List<TranslationEntry>>(json, TranslationHelpers.JsonOpts);

            if (entries == null || entries.Count == 0)
                throw new InvalidOperationException("Gemini returned an empty translation result.");

            return entries.Select(e => new VerseTranslation(
                e.VerseNumber, e.RomanUrdu, e.EnglishText, e.HindiText,
                e.Keywords?.Select(k => new KeywordGloss(k.Word, k.Meaning)).ToList()
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini translation failed for \"{Title}\"", songTitle);
            return persianVerses.Select((_, i) => new VerseTranslation(
                i + 1,
                targetLanguages.Contains("RomanUrdu") ? $"[Translation failed — verse {i + 1}]" : null,
                targetLanguages.Contains("English")   ? $"[Translation failed — verse {i + 1}]" : null,
                targetLanguages.Contains("Hindi")     ? $"[Translation failed — verse {i + 1}]" : null
            )).ToList();
        }
    }

    public async Task<VerseTranslation> RetranslateVerseAsync(
        int verseNumber,
        string persianText,
        string songTitle,
        string fullSongText,
        IReadOnlyList<string> targetLanguages)
    {
        var fullPrompt =
            TranslationHelpers.BuildSingleVerseSystemPrompt(targetLanguages) + "\n\n" +
            $"Song: \"{songTitle}\"\n\n" +
            $"=== Full Poem (context only) ===\n{fullSongText}\n\n" +
            $"=== Re-translate Verse {verseNumber} ===\n{persianText}\n\n" +
            $"Translate into: {string.Join(", ", targetLanguages)}";

        try
        {
            var raw   = await CallGeminiAsync(fullPrompt);
            var json  = TranslationHelpers.StripFences(raw);
            var entry = JsonSerializer.Deserialize<TranslationEntry>(json, TranslationHelpers.JsonOpts);

            if (entry == null)
                throw new InvalidOperationException("Gemini returned an empty re-translation result.");

            return new VerseTranslation(
                entry.VerseNumber, entry.RomanUrdu, entry.EnglishText, entry.HindiText,
                entry.Keywords?.Select(k => new KeywordGloss(k.Word, k.Meaning)).ToList()
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini re-translation failed for verse {VerseNumber}", verseNumber);
            return new VerseTranslation(
                verseNumber,
                targetLanguages.Contains("RomanUrdu") ? $"[Translation failed — verse {verseNumber}]" : null,
                targetLanguages.Contains("English")   ? $"[Translation failed — verse {verseNumber}]" : null,
                targetLanguages.Contains("Hindi")     ? $"[Translation failed — verse {verseNumber}]" : null
            );
        }
    }

    private async Task<string> CallGeminiAsync(string prompt)
    {
        var apiKey    = _config["Gemini:ApiKey"] ?? "";
        var model     = _config["Gemini:Model"] ?? "gemini-2.0-flash";
        var maxTokens = int.Parse(_config["Gemini:MaxTokens"] ?? "8000");

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new { maxOutputTokens = maxTokens }
        };

        var client = _httpClientFactory.CreateClient("Gemini");
        var url    = $"v1beta/models/{model}:generateContent?key={apiKey}";

        var response = await client.PostAsJsonAsync(url, requestBody);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Gemini API error {(int)response.StatusCode}: {body}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc    = JsonDocument.Parse(responseJson);

        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? throw new InvalidOperationException("Gemini response contained no text.");

        return text;
    }

    private record TranslationEntry(
        int VerseNumber,
        string? RomanUrdu,
        string? EnglishText,
        string? HindiText,
        List<KeywordGlossEntry>? Keywords = null);

    private record KeywordGlossEntry(string Word, string Meaning);
}
