using NazmEWasl.Web.Models.Domain;

namespace NazmEWasl.Web.Services.Interfaces;

public interface IBatchTranslationService
{
    /// <summary>Submit a batch translation job. Returns the external batch ID.</summary>
    Task<string> SubmitBatchAsync(Song song, IReadOnlyList<string> targetLanguages);

    /// <summary>Poll the external batch and apply results. Returns true when complete.</summary>
    Task<bool> PollAndApplyResultsAsync(TranslationBatch batch);
}
