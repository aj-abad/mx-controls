using MxControls.Platform;

namespace MxControls.Input;

/// <summary>
/// Records a shortcut through the platform's key capture. It stops on its own after a stretch without keys, so
/// a client that crashes or forgets mid-recording can't keep the keyboard from everyone else.
/// </summary>
public sealed class KeyRecorder(IKeyCapture capture, TimeSpan idleTimeout) : IDisposable
{
    private readonly object _lock = new();
    private Timer? _idle;
    private long _deadline = long.MaxValue;

    /// <summary>A key went down or up; the code is null for a key the platform can't send.</summary>
    public event Action<string?, bool>? Key;

    /// <summary>Recording stopped because no key came within the idle timeout.</summary>
    public event Action? TimedOut;

    public void Start()
    {
        capture.Start(OnKey);
        lock (_lock)
        {
            _idle ??= new Timer(_ => Expire());
            Extend();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _deadline = long.MaxValue;
            _idle?.Change(Timeout.Infinite, Timeout.Infinite);
        }
        capture.Stop();
    }

    private void OnKey(string? code, bool down)
    {
        lock (_lock)
            Extend();
        Key?.Invoke(code, down);
    }

    private void Extend()
    {
        _deadline = Environment.TickCount64 + (long)idleTimeout.TotalMilliseconds;
        _idle!.Change(idleTimeout, Timeout.InfiniteTimeSpan);
    }

    private void Expire()
    {
        lock (_lock)
        {
            if (_deadline == long.MaxValue)
                return;
            // A key or a new Start may have moved the deadline after this callback was already on its way, and
            // the timer can fire a tick before the clock reaches it.
            long left = _deadline - Environment.TickCount64;
            if (left > 0)
            {
                _idle!.Change(left, Timeout.Infinite);
                return;
            }
            _deadline = long.MaxValue;
        }
        capture.Stop();
        TimedOut?.Invoke();
    }

    public void Dispose()
    {
        Stop();
        lock (_lock)
            _idle?.Dispose();
    }
}
