namespace NazmEWasl.Web.Models.Domain;

public class PipelineJob
{
    public int Id { get; set; }
    public int SongId { get; set; }
    public Song Song { get; set; } = null!;
    public PipelineStep Step { get; set; }
    public JobStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Output { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public enum PipelineStep
{
    ImageGeneration = 0,   // future: per-verse AI image generation (no-op for now)
    Translation     = 1,
    CardRendering   = 2,
    VideoRendering  = 3
}

public enum JobStatus
{
    Queued,
    Running,
    Complete,
    Failed
}
