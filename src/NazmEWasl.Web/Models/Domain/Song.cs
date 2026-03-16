using System.ComponentModel.DataAnnotations.Schema;

namespace NazmEWasl.Web.Models.Domain;

public class Song
{
    public int Id { get; set; }
    public string SongId { get; set; } = string.Empty;   // GUID slug, used as folder name
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string? Year { get; set; }
    public string? Notes { get; set; }
    public SongStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    // Comma-separated: "RomanUrdu", "English", "Hindi" — null means legacy (RomanUrdu only)
    public string? SelectedLanguages { get; set; }
    /// <summary>Which LLM provider was last used for translation: "Claude" or "Gemini".</summary>
    public string? LastTranslationProvider { get; set; }

    [NotMapped]
    public IReadOnlyList<string> ParsedLanguages =>
        string.IsNullOrWhiteSpace(SelectedLanguages)
            ? new[] { "RomanUrdu" }
            : SelectedLanguages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public List<Verse> Verses { get; set; } = new();
    public List<PipelineJob> Jobs { get; set; } = new();
}

public enum SongStatus
{
    Uploaded        = 0,
    Translating     = 3,
    AwaitingReview  = 4,
    Approved        = 5,
    GeneratingAssets = 6,
    Complete        = 7
}
