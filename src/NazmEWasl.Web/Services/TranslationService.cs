using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using NazmEWasl.Web.Services.Interfaces;

namespace NazmEWasl.Web.Services;

public class TranslationService : ITranslationService
{
    private readonly IConfiguration _config;
    private readonly ILogger<TranslationService> _logger;

    public TranslationService(IConfiguration config, ILogger<TranslationService> logger)
    {
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
        var apiKey   = _config["Anthropic:ApiKey"] ?? "";
        var model    = _config["Anthropic:Model"] ?? "claude-sonnet-4-6";
        var maxTokens = int.Parse(_config["Anthropic:MaxTokens"] ?? "8000");

        var numberedVerses = string.Join("\n",
            persianVerses.Select((v, i) => $"{i + 1}. {v}"));

        var systemPrompt = TranslationHelpers.BuildSystemPrompt(targetLanguages);

        var userPrompt =
            $"Song: \"{songTitle}\" by {artist}\n\n" +
            $"=== Full Poem — Read First ===\n{fullSongText}\n\n" +
            $"=== Translate Verse by Verse ===\n{numberedVerses}\n\n" +
            $"Translate into: {string.Join(", ", targetLanguages)}";

        try
        {
            var client = new AnthropicClient(apiKey);
            var response = await client.Messages.GetClaudeMessageAsync(new MessageParameters
            {
                Model     = model,
                MaxTokens = maxTokens,
                System    = [new SystemMessage(systemPrompt)],
                Messages  = [new Message(RoleType.User, userPrompt)]
            });

            var raw  = response.Message.ToString() ?? "[]";
            var json = TranslationHelpers.StripFences(raw);

            var entries = JsonSerializer.Deserialize<List<TranslationEntry>>(json, TranslationHelpers.JsonOpts);

            if (entries == null || entries.Count == 0)
                throw new InvalidOperationException("Claude returned an empty translation result.");

            return entries.Select(e => new VerseTranslation(
                e.VerseNumber, e.RomanUrdu, e.EnglishText, e.HindiText,
                e.Keywords?.Select(k => new KeywordGloss(k.Word, k.Meaning)).ToList()
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation failed for \"{Title}\"", songTitle);
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
        var apiKey    = _config["Anthropic:ApiKey"] ?? "";
        var model     = _config["Anthropic:Model"] ?? "claude-sonnet-4-6";
        var maxTokens = int.Parse(_config["Anthropic:MaxTokens"] ?? "8000");

        var systemPrompt = TranslationHelpers.BuildSingleVerseSystemPrompt(targetLanguages);

        var userPrompt =
            $"Song: \"{songTitle}\"\n\n" +
            $"=== Full Poem (context only) ===\n{fullSongText}\n\n" +
            $"=== Re-translate Verse {verseNumber} ===\n{persianText}\n\n" +
            $"Translate into: {string.Join(", ", targetLanguages)}";

        try
        {
            var client = new AnthropicClient(apiKey);
            var response = await client.Messages.GetClaudeMessageAsync(new MessageParameters
            {
                Model     = model,
                MaxTokens = maxTokens,
                System    = [new SystemMessage(systemPrompt)],
                Messages  = [new Message(RoleType.User, userPrompt)]
            });

            var raw   = response.Message.ToString() ?? "{}";
            var json  = TranslationHelpers.StripFences(raw);
            var entry = JsonSerializer.Deserialize<TranslationEntry>(json, TranslationHelpers.JsonOpts);

            if (entry == null)
                throw new InvalidOperationException("Claude returned an empty re-translation result.");

            return new VerseTranslation(
                entry.VerseNumber, entry.RomanUrdu, entry.EnglishText, entry.HindiText,
                entry.Keywords?.Select(k => new KeywordGloss(k.Word, k.Meaning)).ToList()
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Re-translation failed for verse {VerseNumber}", verseNumber);
            return new VerseTranslation(
                verseNumber,
                targetLanguages.Contains("RomanUrdu") ? $"[Translation failed — verse {verseNumber}]" : null,
                targetLanguages.Contains("English")   ? $"[Translation failed — verse {verseNumber}]" : null,
                targetLanguages.Contains("Hindi")     ? $"[Translation failed — verse {verseNumber}]" : null
            );
        }
    }

    private record TranslationEntry(
        int VerseNumber,
        string? RomanUrdu,
        string? EnglishText,
        string? HindiText,
        List<KeywordGlossEntry>? Keywords = null);

    private record KeywordGlossEntry(string Word, string Meaning);
}
