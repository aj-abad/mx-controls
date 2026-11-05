using MxControls.Actions;
using MxControls.Features;

namespace MxControls.Tests;

public class ThumbWheelTests
{
    private static readonly FakePlatform Platform = new();

    // Each native count is a notch; the diverted wheel reports eight counts for each.
    private static ThumbWheelMapper Mapper(ThumbWheelSettings settings) => new(settings, nativeResolution: 18, divertedResolution: 144);

    private static ThumbWheelTurn Turn(int rotation, int phase = 2) => new(rotation, phase);

    [Fact]
    public void SpeedScalesTheNotchANativeCountScrolls()
    {
        var mapper = Mapper(new ThumbWheelSettings(Speed: 150));
        Assert.Equal(180, mapper.Turn(Turn(8, phase: 1)));
        Assert.Equal(-180, mapper.Turn(Turn(-8)));
    }

    [Fact]
    public void FractionsCarryOverWithinATurn()
    {
        var mapper = Mapper(new ThumbWheelSettings(Speed: 50));
        // Half speed is 7.5 units a count.
        Assert.Equal(7, mapper.Turn(Turn(1, phase: 1)));
        Assert.Equal(8, mapper.Turn(Turn(1)));
    }

    [Fact]
    public void InvertedFlipsTheDirection()
    {
        Assert.Equal(-120, Mapper(new ThumbWheelSettings(Speed: 200, Inverted: true)).Turn(Turn(4, phase: 1)));
    }

    [Fact]
    public void ActionsStepOncePerNotchAndStartEachTurnFresh()
    {
        var mapper = Mapper(new ThumbWheelSettings(ThumbWheelSettings.ActionsMode));
        Assert.Equal(0, mapper.Turn(Turn(5, phase: 1)));
        Assert.Equal(1, mapper.Turn(Turn(5)));
        Assert.Equal(2, mapper.Turn(Turn(16)));
        // Two counts were left over; a new turn drops them, so seven more still make no step.
        Assert.Equal(0, mapper.Turn(Turn(7, phase: 1)));
        Assert.Equal(0, mapper.Turn(Turn(-9)));
        Assert.Equal(-1, mapper.Turn(Turn(-8)));
    }

    [Fact]
    public void OnlyTheDefaultLeavesTheWheelAlone()
    {
        Assert.True(ThumbWheelSettings.Default.IsDefault);
        Assert.False(new ThumbWheelSettings(Inverted: true).IsDefault);
        Assert.False(new ThumbWheelSettings(Inverted: true).Diverts);
        Assert.True(new ThumbWheelSettings(Speed: 200).Diverts);
        Assert.True(new ThumbWheelSettings(ThumbWheelSettings.ActionsMode).Diverts);
    }

    [Theory]
    [InlineData("scroll", 10, null, "Speed must be")]
    [InlineData("spin", 100, null, "Unknown thumb wheel mode")]
    [InlineData("actions", 100, "F99", "can't send the key")]
    public void ExplainsWhatIsWrong(string mode, int speed, string? leftKey, string expected)
    {
        var left = leftKey is null ? null : new ButtonAction(ActionTypes.Press, [leftKey]);
        Assert.Contains(expected, new ThumbWheelSettings(mode, speed, Left: left).Problem(Platform));
    }

    [Fact]
    public void AcceptsTapsForItsSteps()
    {
        var settings = new ThumbWheelSettings(ThumbWheelSettings.ActionsMode,
            Left: new ButtonAction(ActionTypes.Press, ["MediaPlayPause"]),
            Right: new ButtonAction(ActionTypes.Click, Mouse: MouseButtons.Forward));
        Assert.Null(settings.Problem(Platform));
    }
}
