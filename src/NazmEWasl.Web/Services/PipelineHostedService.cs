namespace NazmEWasl.Web.Services;

public class PipelineHostedService : BackgroundService
{
    private readonly IBackgroundPipelineQueue _queue;
    private readonly ILogger<PipelineHostedService> _logger;
    private readonly SemaphoreSlim _concurrency = new(3);

    public PipelineHostedService(IBackgroundPipelineQueue queue, ILogger<PipelineHostedService> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var workItem = await _queue.DequeueAsync(stoppingToken);
                await _concurrency.WaitAsync(stoppingToken);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await workItem(stoppingToken);
                    }
                    catch (OperationCanceledException) { /* expected on shutdown */ }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unhandled error executing background pipeline job.");
                    }
                    finally
                    {
                        _concurrency.Release();
                    }
                }, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dequeuing pipeline job.");
            }
        }
    }
}
