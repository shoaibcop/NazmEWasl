using System.Threading.Channels;

namespace NazmEWasl.Web.Services;

public interface IBackgroundPipelineQueue
{
    void Enqueue(Func<CancellationToken, Task> workItem);
    Task<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);
}

public class BackgroundPipelineQueue : IBackgroundPipelineQueue
{
    private readonly Channel<Func<CancellationToken, Task>> _queue =
        Channel.CreateUnbounded<Func<CancellationToken, Task>>();

    public void Enqueue(Func<CancellationToken, Task> workItem) =>
        _queue.Writer.TryWrite(workItem);

    public async Task<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken) =>
        await _queue.Reader.ReadAsync(cancellationToken);
}
