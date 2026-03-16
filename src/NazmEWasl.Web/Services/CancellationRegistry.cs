using System.Collections.Concurrent;

namespace NazmEWasl.Web.Services;

/// <summary>Singleton registry that maps song GUIDs to cancellation token sources.</summary>
public class CancellationRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _map = new();

    public CancellationToken Register(string songId)
    {
        var cts = new CancellationTokenSource();
        _map[songId] = cts;
        return cts.Token;
    }

    public void Cancel(string songId)
    {
        if (_map.TryGetValue(songId, out var cts))
            cts.Cancel();
    }

    public void Remove(string songId)
    {
        if (_map.TryRemove(songId, out var cts))
            cts.Dispose();
    }
}
