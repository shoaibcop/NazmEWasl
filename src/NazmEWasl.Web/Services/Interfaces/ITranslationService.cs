namespace NazmEWasl.Web.Services.Interfaces;

public interface ITranslationService
{
    Task<List<VerseTranslation>> TranslateVersesAsync(
        List<string> persianVerses,
        string songTitle,
        string artist,
        string fullSongText,
        IReadOnlyList<string> targetLanguages);

    Task<VerseTranslation> RetranslateVerseAsync(
        int verseNumber,
        string persianText,
        string songTitle,
        string fullSongText,
        IReadOnlyList<string> targetLanguages);
}

/// <summary>A culturally/poetically significant word and its meaning, extracted by the LLM.</summary>
public record KeywordGloss(string Word, string Meaning);

public record VerseTranslation(
    int VerseNumber,
    string? RomanUrdu,
    string? EnglishText,
    string? HindiText,
    List<KeywordGloss>? Keywords = null);
