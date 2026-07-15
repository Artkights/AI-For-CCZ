using System.Collections.Concurrent;

namespace CCZModStudio.Core;

/// <summary>Coalesces rapid UI changes without blocking the UI thread.</summary>
public sealed class DebouncedUiAction : IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new(StringComparer.Ordinal);

    public void Schedule(string key, TimeSpan delay, Action action)
    {
        var cts = new CancellationTokenSource();
        var prior = _pending.AddOrUpdate(key, cts, (_, current) =>
        {
            current.Cancel();
            current.Dispose();
            return cts;
        });
        if (!ReferenceEquals(prior, cts)) return;
        _ = RunAsync(key, delay, action, cts);
    }

    public void Cancel(string key)
    {
        if (!_pending.TryRemove(key, out var cts)) return;
        cts.Cancel();
        cts.Dispose();
    }

    private async Task RunAsync(string key, TimeSpan delay, Action action, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(delay, cts.Token).ConfigureAwait(true);
            if (!cts.IsCancellationRequested) action();
        }
        catch (OperationCanceledException) { }
        finally
        {
            _pending.TryRemove(new KeyValuePair<string, CancellationTokenSource>(key, cts));
            cts.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var key in _pending.Keys.ToArray()) Cancel(key);
    }
}
