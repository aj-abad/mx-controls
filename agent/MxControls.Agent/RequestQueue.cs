namespace MxControls.Agent;

/// <summary>
/// Runs work one piece at a time. Work queued with a key is replaced by newer work with the same key while it
/// waits for its turn, so a burst of changes to one setting reaches the mouse as at most two writes: the one
/// running and the latest. Everyone who queued it waits for whichever work ran in its place.
/// </summary>
public sealed class RequestQueue
{
    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly Dictionary<string, Waiting> _waiting = [];

    private sealed class Waiting(Func<Task> work)
    {
        public Func<Task> Work = work;
        public readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task RunAsync(Func<Task> work)
    {
        await _turn.WaitAsync();
        try
        {
            await work();
        }
        finally
        {
            _turn.Release();
        }
    }

    public Task RunLatestAsync(string key, Func<Task> work)
    {
        lock (_waiting)
        {
            if (_waiting.TryGetValue(key, out var queued))
            {
                queued.Work = work;
                return queued.Done.Task;
            }
            var waiting = new Waiting(work);
            _waiting[key] = waiting;
            _ = RunWaitingAsync(key, waiting);
            return waiting.Done.Task;
        }
    }

    private async Task RunWaitingAsync(string key, Waiting waiting)
    {
        await _turn.WaitAsync();
        Func<Task> work;
        // From here newer work with this key queues behind this one instead of replacing it.
        lock (_waiting)
        {
            _waiting.Remove(key);
            work = waiting.Work;
        }
        try
        {
            await work();
            waiting.Done.SetResult();
        }
        catch (Exception error)
        {
            waiting.Done.SetException(error);
        }
        finally
        {
            _turn.Release();
        }
    }
}
