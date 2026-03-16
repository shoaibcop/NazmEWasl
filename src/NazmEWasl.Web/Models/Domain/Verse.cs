namespace NazmEWasl.Web.Models.Domain;

public class Verse
{
    public int Id { get; set; }
    public int SongId { get; set; }
    public Song Song { get; set; } = null!;
    public int VerseNumber { get; set; }           // 1-indexed
    public string PersianText { get; set; } = string.Empty;
    public string? RomanUrdu { get; set; }         // Urdu words in Latin/English script
    public string? EnglishText { get; set; }       // English poetic translation
    public string? HindiText { get; set; }         // Hindi translation (Devanagari)
    public int? StartMs { get; set; }
    public int? EndMs { get; set; }
    public bool IsApproved { get; set; }
    public DateTime? LastEditedAt { get; set; }
    /// <summary>JSON array of {word, meaning} keyword glosses extracted by the LLM during translation.</summary>
    public string? KeywordsJson { get; set; }
}
