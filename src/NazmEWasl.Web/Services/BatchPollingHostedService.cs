using Microsoft.EntityFrameworkCore;
using NazmEWasl.Web.Data;
using NazmEWasl.Web.Models.Domain;

namespace NazmEWasl.Web.Services;

/// <summary>Background hosted service that polls pending TranslationBatch records every 60s.</summary>
public class BatchPollingHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BatchPollingHostedService> _logger;

    public BatchPollingHostedService(IServiceScopeFactory scopeFactory, ILogger<BatchPollingHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollPendingBatchesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during batch polling cycle.");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

    private async Task PollPendingBatchesAsync(CancellationToken ct)
    {
        using var scope   = _scopeFactory.CreateScope();
        var db            = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var factory       = scope.ServiceProvider.GetRequiredService<BatchTranslationServiceFactory>();

        var pending = await db.TranslationBatches
            .Where(b => b.Status == BatchStatus.Submitted || b.Status == BatchStatus.Processing)
            .ToListAsync(ct);

        foreach (var batch in pending)
        {
            try
            {
                var svc      = factory.Create(batch.Provider);
                var complete = await svc.PollAndApplyResultsAsync(batch);

                if (complete)
                {
                    batch.Status      = BatchStatus.Complete;
                    batch.CompletedAt = DateTime.UtcNow;
                    _logger.LogInformation("Batch {ExternalId} for song {SongId} completed.", batch.ExternalBatchId, batch.SongId);
                }
                else
                {
                    batch.Status = BatchStatus.Processing;
                }
            }
            catch (Exception ex)
            {
                batch.Status       = BatchStatus.Failed;
                batch.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Batch {ExternalId} polling failed.", batch.ExternalBatchId);
            }
        }

        if (pending.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
