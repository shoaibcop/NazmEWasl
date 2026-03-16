using System.Text.Json;

namespace NazmEWasl.Web.Services;

internal static class TranslationHelpers
{
    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    internal static string StripFences(string raw)
    {
        var json = raw.Trim();
        if (json.StartsWith("```"))
            json = string.Join("\n", json.Split('\n').Skip(1).SkipLast(1)).Trim();
        return json;
    }

    internal static string BuildSystemPrompt(IReadOnlyList<string> targetLanguages)
    {
        var langInstructions = string.Join("\n", targetLanguages.Select(l => l switch
        {
            "RomanUrdu" => "- Roman Urdu: Urdu written in Latin/English letters. Hindustani register, accessible to Indian audiences. No heavy literary Urdu.",
            "English"   => "- English: Poetic, not literal. Keep metaphor and imagery alive.",
            "Hindi"     => "- Hindi: Devanagari script. Same poetic register as the original.",
            _           => $"- {l}: Translate naturally."
        }));

        var jsonKeys = string.Join(", ", targetLanguages.Select(l => l switch
        {
            "RomanUrdu" => "\"roman_urdu\": \"...\"",
            "English"   => "\"english_text\": \"...\"",
            "Hindi"     => "\"hindi_text\": \"...\"",
            _           => $"\"{l.ToLowerInvariant()}\": \"...\""
        }));

        return
            "You are a master poet-translator fluent in Persian, Urdu, Hindi, and English.\n\n" +
            "Step 1 — Read the full poem provided to understand its emotional arc, recurring imagery, and soul. Do not translate yet.\n\n" +
            "Step 2 — For each numbered verse, produce translations into the requested languages.\n" +
            "Translate emotion-for-emotion, not word-for-word. Preserve imagery, metaphor, and the breath of the original.\n\n" +
            "Step 3 — For each verse, identify 1–3 words that carry deep cultural, spiritual, or poetic weight (e.g. 'ishq', 'dard', 'yaar'). " +
            "Include their richer meaning as a 'keywords' array.\n\n" +
            "Language instructions:\n" + langInstructions + "\n\n" +
            "Output rules:\n" +
            "- Respond ONLY with a valid JSON array. No markdown, no commentary.\n" +
            $"- Schema: [{{\"verse_number\": N, {jsonKeys}, \"keywords\": [{{\"word\": \"...\", \"meaning\": \"...\"}}]}}, ...]\n" +
            "- keywords array may be empty [] if no standout words exist for a verse.\n" +
            "- Include ONLY keys for the requested languages. Omit all others.";
    }

    internal static string BuildSingleVerseSystemPrompt(IReadOnlyList<string> targetLanguages)
    {
        var langInstructions = string.Join("\n", targetLanguages.Select(l => l switch
        {
            "RomanUrdu" => "- Roman Urdu: Urdu in Latin letters, Hindustani register.",
            "English"   => "- English: Poetic, not literal.",
            "Hindi"     => "- Hindi: Devanagari script.",
            _           => $"- {l}: Translate naturally."
        }));

        var jsonKeys = string.Join(", ", targetLanguages.Select(l => l switch
        {
            "RomanUrdu" => "\"roman_urdu\": \"...\"",
            "English"   => "\"english_text\": \"...\"",
            "Hindi"     => "\"hindi_text\": \"...\"",
            _           => $"\"{l.ToLowerInvariant()}\": \"...\""
        }));

        return
            "You are a master poet-translator. You have the full poem for context.\n" +
            "Re-translate ONLY the specified verse — emotion-for-emotion, not word-for-word.\n" +
            "Also identify 1–3 words that carry deep cultural, spiritual, or poetic weight and add them to 'keywords'.\n\n" +
            "Language instructions:\n" + langInstructions + "\n\n" +
            "Output rules:\n" +
            "- Respond ONLY with a single JSON object. No markdown, no commentary.\n" +
            $"- Schema: {{\"verse_number\": N, {jsonKeys}, \"keywords\": [{{\"word\": \"...\", \"meaning\": \"...\"}}]}}\n" +
            "- Include ONLY keys for the requested languages.";
    }
}
