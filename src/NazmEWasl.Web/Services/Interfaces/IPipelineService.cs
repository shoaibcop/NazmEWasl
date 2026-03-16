namespace NazmEWasl.Web.Services.Interfaces;

public interface IPipelineService
{
    /// <summary>Future step: generate per-verse images using an AI image model. Currently a no-op.</summary>
    Task RunImageGenerationAsync(string songId);
    Task RunCardRenderingAsync(string songId, CancellationToken ct = default);
    Task RunVideoRenderingAsync(string songId, CancellationToken ct = default);
}
