using MxControls.Agent;

namespace MxControls.Tests;

public class RequestQueueTests
{
    [Fact]
    public async Task ANewerChangeReplacesOneStillWaiting()
    {
        var queue = new RequestQueue();
        var ran = new List<string>();
        var release = new TaskCompletionSource();

        var running = queue.RunAsync(async () => { ran.Add("busy"); await release.Task; });
        var first = queue.RunLatestAsync("dpi", () => { ran.Add("800"); return Task.CompletedTask; });
        var second = queue.RunLatestAsync("dpi", () => { ran.Add("1000"); return Task.CompletedTask; });
        var third = queue.RunLatestAsync("dpi", () => { ran.Add("1600"); return Task.CompletedTask; });
        release.SetResult();
        await Task.WhenAll(running, first, second, third);

        Assert.Equal(["busy", "1600"], ran);
    }

    [Fact]
    public async Task ChangesToDifferentSettingsBothRun()
    {
        var queue = new RequestQueue();
        var ran = new List<string>();
        var release = new TaskCompletionSource();

        var running = queue.RunAsync(() => release.Task);
        var dpi = queue.RunLatestAsync("dpi", () => { ran.Add("dpi"); return Task.CompletedTask; });
        var wheel = queue.RunLatestAsync("wheel", () => { ran.Add("wheel"); return Task.CompletedTask; });
        release.SetResult();
        await Task.WhenAll(running, dpi, wheel);

        Assert.Equal(["dpi", "wheel"], ran.Order());
    }

    [Fact]
    public async Task AChangeWhileOneRunsWaitsBehindItInsteadOfReplacingIt()
    {
        var queue = new RequestQueue();
        var ran = new List<string>();
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var first = queue.RunLatestAsync("dpi", async () => { ran.Add("800"); started.SetResult(); await release.Task; });
        await started.Task;
        var second = queue.RunLatestAsync("dpi", () => { ran.Add("1000"); return Task.CompletedTask; });
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(["800", "1000"], ran);
    }

    [Fact]
    public async Task EveryoneWaitingGetsTheFailureOfTheWorkThatRan()
    {
        var queue = new RequestQueue();
        var release = new TaskCompletionSource();

        var running = queue.RunAsync(() => release.Task);
        var first = queue.RunLatestAsync("wheel", () => Task.CompletedTask);
        var second = queue.RunLatestAsync("wheel", () => throw new TimeoutException("no reply"));
        release.SetResult();
        await running;

        Assert.Equal("no reply", (await Assert.ThrowsAsync<TimeoutException>(() => first)).Message);
        await Assert.ThrowsAsync<TimeoutException>(() => second);
    }
}
