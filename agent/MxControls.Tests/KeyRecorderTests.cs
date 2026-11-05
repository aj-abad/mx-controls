using MxControls.Input;
using MxControls.Platform;

namespace MxControls.Tests;

public class KeyRecorderTests
{
    private sealed class FakeCapture : IKeyCapture
    {
        public Action<string?, bool>? OnKey { get; private set; }

        public void Start(Action<string?, bool> onKey) => OnKey = onKey;
        public void Stop() => OnKey = null;
    }

    [Fact]
    public void ForwardsKeysUntilStopped()
    {
        var capture = new FakeCapture();
        using var recorder = new KeyRecorder(capture, TimeSpan.FromMinutes(1));
        var keys = new List<string>();
        recorder.Key += (code, down) => keys.Add($"{(down ? "down" : "up")} {code}");

        recorder.Start();
        capture.OnKey!("MetaLeft", true);
        capture.OnKey!("MetaLeft", false);
        recorder.Stop();

        Assert.Equal(["down MetaLeft", "up MetaLeft"], keys);
        Assert.Null(capture.OnKey);
    }

    [Fact]
    public async Task HandsTheKeyboardBackAfterTheIdleTimeout()
    {
        var capture = new FakeCapture();
        using var recorder = new KeyRecorder(capture, TimeSpan.FromMilliseconds(100));
        var timedOut = new TaskCompletionSource();
        recorder.TimedOut += () => timedOut.TrySetResult();

        recorder.Start();
        await timedOut.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(capture.OnKey);
    }

    [Fact]
    public async Task KeysPushTheTimeoutBack()
    {
        var capture = new FakeCapture();
        using var recorder = new KeyRecorder(capture, TimeSpan.FromMilliseconds(300));
        bool timedOut = false;
        recorder.TimedOut += () => timedOut = true;

        recorder.Start();
        for (int i = 0; i < 4; i++)
        {
            await Task.Delay(150);
            capture.OnKey!("ShiftLeft", i % 2 == 0);
        }

        Assert.False(timedOut);
        Assert.NotNull(capture.OnKey);
    }

    [Fact]
    public async Task StoppingCancelsTheTimeout()
    {
        var capture = new FakeCapture();
        using var recorder = new KeyRecorder(capture, TimeSpan.FromMilliseconds(100));
        bool timedOut = false;
        recorder.TimedOut += () => timedOut = true;

        recorder.Start();
        recorder.Stop();
        await Task.Delay(300);

        Assert.False(timedOut);
    }
}
