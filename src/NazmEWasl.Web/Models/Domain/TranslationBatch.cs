namespace NazmEWasl.Web.Models.Domain;

public class TranslationBatch
{
    public int Id { get; set; }
    public int SongId { get; set; }
    public Song Song { get; set; } = null!;
    public string Provider { get; set; } = "";
    public string ExternalBatchId { get; set; } = "";
    public BatchStatus Status { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum BatchStatus
{
    Submitted,
    Processing,
    Complete,
    Failed
}
