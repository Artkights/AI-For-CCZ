using System.Collections.Concurrent;

namespace CCZModStudio.Core;

/// <summary>Runs feature loads off the UI thread and commits only the latest request.</summary>
public sealed class AsyncLoadCoordinator : IDisposable
{
    private static readonly SemaphoreSlim CpuGate = new(4, 4);
    private readonly ConcurrentDictionary<string, RequestState> _requests = new(StringComparer.Ordinal);
    private long _generation;

    public async Task<bool> RunLatestAsync<T>(
        string featureKey,
        string projectKey,
        Func<CancellationToken, Task<T>> modelFactory,
        Func<T, Task> uiCommit,
        CancellationToken cancellationToken = default)
    {
        var commitContext = SynchronizationContext.Current;
        var generation = Interlocked.Increment(ref _generation);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var state = new RequestState(projectKey, generation, linked);
        if (_requests.AddOrUpdate(featureKey, state, (_, prior) =>
            {
                prior.Cancellation.Cancel();
                prior.Cancellation.Dispose();
                return state;
            }) is not { } current || current.Generation != generation)
            return false;

        PerformanceMetrics.Increment("AsyncLoad.Queued");
        try
        {
            await CpuGate.WaitAsync(linked.Token).ConfigureAwait(false);
            T model;
            try
            {
                // ConfigureAwait(false) does not force an asynchronous hop when the
                // semaphore wait completes synchronously. Start the factory on the
                // thread pool so fingerprinting and pre-await work never run on UI.
                model = await Task.Run(() => modelFactory(linked.Token), linked.Token).ConfigureAwait(false);
            }
            finally { CpuGate.Release(); }

            if (!_requests.TryGetValue(featureKey, out var latest) || latest.Generation != generation)
            {
                PerformanceMetrics.Increment("AsyncLoad.StaleResults");
                return false;
            }
            linked.Token.ThrowIfCancellationRequested();
            await CommitOnContextAsync(commitContext, () => uiCommit(model), linked.Token).ConfigureAwait(false);
            PerformanceMetrics.Increment("AsyncLoad.Completed");
            return true;
        }
        catch (OperationCanceledException)
        {
            PerformanceMetrics.Increment("AsyncLoad.Cancelled");
            return false;
        }
        finally
        {
            if (_requests.TryGetValue(featureKey, out var latest) && latest.Generation == generation)
                _requests.TryRemove(featureKey, out _);
            linked.Dispose();
        }
    }

    private static Task CommitOnContextAsync(
        SynchronizationContext? context,
        Func<Task> commit,
        CancellationToken cancellationToken)
    {
        if (context == null || ReferenceEquals(context, SynchronizationContext.Current)) return commit();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(async _ =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await commit();
                completion.TrySetResult();
            }
            catch (OperationCanceledException ex) { completion.TrySetCanceled(ex.CancellationToken); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }, null);
        return completion.Task;
    }

    public void Cancel(string featureKey)
    {
        if (!_requests.TryRemove(featureKey, out var state)) return;
        state.Cancellation.Cancel();
        state.Cancellation.Dispose();
    }

    public void CancelProject(string projectKey)
    {
        foreach (var pair in _requests.Where(pair => pair.Value.ProjectKey.Equals(projectKey, StringComparison.OrdinalIgnoreCase)).ToArray())
            Cancel(pair.Key);
    }

    public void Dispose()
    {
        foreach (var key in _requests.Keys.ToArray()) Cancel(key);
    }

    private sealed record RequestState(string ProjectKey, long Generation, CancellationTokenSource Cancellation);
}
