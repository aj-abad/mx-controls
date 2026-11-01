using MxControls.Actions;

namespace MxControls.Tests;

public class ActionRunnerTests
{
    private const ushort Button = 0x00C3;

    private static (ActionRunner Runner, FakePlatform Platform, object Gate) Create()
    {
        var platform = new FakePlatform();
        var gate = new object();
        return (new ActionRunner(platform, gate), platform, gate);
    }

    [Fact]
    public void HoldKeepsKeysDownUntilEndThenReleasesInReverse()
    {
        var (runner, platform, _) = Create();
        runner.Begin(Button, new ButtonAction(ActionTypes.Hold, ["ControlLeft", "ShiftLeft"]));
        Assert.Equal(["down ControlLeft", "down ShiftLeft"], platform.Log);

        runner.End(Button);
        Assert.Equal(["down ControlLeft", "down ShiftLeft", "up ShiftLeft", "up ControlLeft"], platform.Log);
    }

    [Fact]
    public void PressTapsTheChordOnce()
    {
        var (runner, platform, _) = Create();
        runner.Begin(Button, new ButtonAction(ActionTypes.Press, ["ControlLeft", "KeyC"]));
        runner.End(Button);
        Assert.Equal(["down ControlLeft", "down KeyC", "up KeyC", "up ControlLeft"], platform.Log);
    }

    [Fact]
    public void SystemRunsThroughThePlatform()
    {
        var (runner, platform, _) = Create();
        runner.Run(new ButtonAction(ActionTypes.System, System: SystemActions.TaskView));
        Assert.Equal(["system taskView"], platform.Log);
    }

    [Fact]
    public void ClickHoldsTheMouseButtonUntilEnd()
    {
        var (runner, platform, _) = Create();
        runner.Begin(Button, new ButtonAction(ActionTypes.Click, Mouse: MouseButtons.Back));
        Assert.Equal(["mouse down back"], platform.Log);

        runner.End(Button);
        Assert.Equal(["mouse down back", "mouse up back"], platform.Log);
    }

    [Fact]
    public void ClickTapsWhenRunOnce()
    {
        var (runner, platform, _) = Create();
        runner.Run(new ButtonAction(ActionTypes.Click, Mouse: MouseButtons.Forward));
        Assert.Equal(["mouse down forward", "mouse up forward"], platform.Log);
    }

    [Fact]
    public void NothingDoesNothing()
    {
        var (runner, platform, _) = Create();
        runner.Begin(Button, ButtonAction.Nothing);
        runner.End(Button);
        Assert.Empty(platform.Log);
    }

    [Fact]
    public async Task RepeatFiresWhileHeldAndStopsAtEnd()
    {
        var (runner, platform, gate) = Create();
        lock (gate)
            runner.Begin(Button, new ButtonAction(ActionTypes.Press, ["MediaPlayPause"], Repeat: true));

        // Once at once, then after 300 ms and every 200 ms: three taps by 650 ms.
        await Task.Delay(650);
        lock (gate)
            runner.End(Button);
        int taps = platform.Log.Count(line => line == "down MediaPlayPause");
        Assert.InRange(taps, 2, 4);

        await Task.Delay(450);
        Assert.Equal(taps, platform.Log.Count(line => line == "down MediaPlayPause"));
    }

    [Fact]
    public void BeginningAgainEndsThePreviousHold()
    {
        var (runner, platform, _) = Create();
        runner.Begin(Button, new ButtonAction(ActionTypes.Hold, ["ShiftLeft"]));
        runner.Begin(Button, new ButtonAction(ActionTypes.Hold, ["ControlLeft"]));
        runner.EndAll();
        Assert.Equal(["down ShiftLeft", "up ShiftLeft", "down ControlLeft", "up ControlLeft"], platform.Log);
    }

    [Fact]
    public async Task EndAllLetsGoOfEveryControlAndStopsRepeats()
    {
        var (runner, platform, gate) = Create();
        lock (gate)
        {
            runner.Begin(Button, new ButtonAction(ActionTypes.Hold, ["ShiftLeft"]));
            runner.Begin(0x0053, new ButtonAction(ActionTypes.Click, Mouse: MouseButtons.Back));
            runner.Begin(0x0056, new ButtonAction(ActionTypes.Press, ["MediaPlayPause"], Repeat: true));
            runner.EndAll();
        }
        Assert.Contains("up ShiftLeft", platform.Log);
        Assert.Contains("mouse up back", platform.Log);

        await Task.Delay(450);
        Assert.Equal(1, platform.Log.Count(line => line == "down MediaPlayPause"));
    }
}
