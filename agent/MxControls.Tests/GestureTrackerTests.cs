using System.Text.Json;
using MxControls.Actions;

namespace MxControls.Tests;

// At the default 1000 DPI the 2 mm threshold is 79 counts and the 4 mm repeat step 157.
public class GestureTrackerTests
{
    private const int ReportMs = 8;
    private long _now;

    private GestureDirection? Move(GestureTracker tracker, int dx, int dy, int afterMs = ReportMs) =>
        tracker.Add(dx, dy, _now += afterMs);

    // Each press opens with a stale report, which the tracker must ignore.
    private GestureTracker Pressed(int dpi = 1000, bool repeat = false)
    {
        var tracker = new GestureTracker { Dpi = dpi };
        tracker.Reset(_ => repeat);
        Assert.Null(Move(tracker, -400, 300));
        return tracker;
    }

    [Theory]
    [InlineData(130, 0, GestureDirection.Right)]
    [InlineData(-130, 0, GestureDirection.Left)]
    [InlineData(0, -130, GestureDirection.Up)]
    [InlineData(0, 130, GestureDirection.Down)]
    [InlineData(130, -60, GestureDirection.Right)]
    [InlineData(-60, 140, GestureDirection.Down)]
    public void PicksTheDominantAxis(int dx, int dy, GestureDirection expected)
    {
        Assert.Equal(expected, Move(Pressed(), dx, dy));
    }

    [Fact]
    public void IgnoresTheFirstReportOfAPress()
    {
        var tracker = new GestureTracker();
        tracker.Reset();
        Assert.Null(Move(tracker, 351, 12));
        Assert.Null(Move(tracker, -40, 0));
        Assert.Equal(GestureDirection.Left, Move(tracker, -40, 0));
    }

    [Fact]
    public void WaitsForTheThresholdAcrossReports()
    {
        var tracker = Pressed();
        Assert.Null(Move(tracker, 30, 0));
        Assert.Null(Move(tracker, 30, 5));
        Assert.Equal(GestureDirection.Right, Move(tracker, 30, 0));
    }

    [Fact]
    public void AQuickFlickFiresAndIsNoClick()
    {
        var tracker = Pressed();
        Assert.Equal(GestureDirection.Left, Move(tracker, -200, 10));
        Assert.False(tracker.IsClick);
    }

    [Fact]
    public void ADiagonalPicksNothingUntilItStraightensOut()
    {
        var tracker = Pressed();
        Assert.Null(Move(tracker, 120, 120));
        Assert.Null(Move(tracker, 60, 0));
        Assert.Equal(GestureDirection.Right, Move(tracker, 80, 0));
    }

    [Fact]
    public void AStrokeFiresOnceWithoutRepeat()
    {
        var tracker = Pressed();
        Assert.Equal(GestureDirection.Up, Move(tracker, 0, -150));
        for (int i = 0; i < 10; i++)
            Assert.Null(Move(tracker, 0, -100));
    }

    [Fact]
    public void ARepeatingDirectionFiresForEveryStep()
    {
        var tracker = Pressed(repeat: true);
        Assert.Equal(GestureDirection.Up, Move(tracker, 0, -100));
        Assert.Null(Move(tracker, 0, -100));
        Assert.Equal(GestureDirection.Up, Move(tracker, 0, -60));
        Assert.Null(Move(tracker, 0, -100));
        Assert.Equal(GestureDirection.Up, Move(tracker, 0, -60));
    }

    [Fact]
    public void MovingBackFiresTheOppositeFromWhereItTurned()
    {
        var tracker = Pressed(repeat: true);
        Assert.Equal(GestureDirection.Up, Move(tracker, 0, -100));
        Assert.Null(Move(tracker, 0, -100));
        Assert.Null(Move(tracker, 0, 40));
        Assert.Equal(GestureDirection.Down, Move(tracker, 0, 40));
        Assert.Equal(GestureDirection.Down, Move(tracker, 0, 160));
        Assert.Equal(GestureDirection.Up, Move(tracker, 0, -80));
    }

    [Fact]
    public void ARepeatingDirectionLocksThePressToItsAxis()
    {
        var tracker = Pressed(repeat: true);
        Assert.Equal(GestureDirection.Up, Move(tracker, 0, -100));
        for (int i = 0; i < 5; i++)
            Assert.Null(Move(tracker, -100, 0));
        Assert.Null(Move(tracker, 300, 0, afterMs: GestureTracker.PauseMs));
        Assert.Null(Move(tracker, -60, 40));
        Assert.Equal(GestureDirection.Down, Move(tracker, -60, 40));
    }

    [Fact]
    public void OnlyARepeatingDirectionLocks()
    {
        var tracker = new GestureTracker();
        tracker.Reset(direction => direction is GestureDirection.Up or GestureDirection.Down);
        Assert.Null(Move(tracker, -400, 300));
        Assert.Equal(GestureDirection.Left, Move(tracker, -150, 0));
        Assert.Equal(GestureDirection.Up, Move(tracker, 0, -150));
        Assert.Null(Move(tracker, 150, 0));
        Assert.Null(Move(tracker, 150, 0));
    }

    [Fact]
    public void TurningFiresTheNewDirection()
    {
        var tracker = Pressed();
        Assert.Equal(GestureDirection.Up, Move(tracker, 0, -150));
        Assert.Null(Move(tracker, -50, 0));
        Assert.Equal(GestureDirection.Left, Move(tracker, -50, 0));
        Assert.Equal(GestureDirection.Left, tracker.Direction);
    }

    [Fact]
    public void AStrokeThatCurvesDoesNotFireSideways()
    {
        var tracker = Pressed();
        Assert.Null(Move(tracker, 12, -40));
        Assert.Equal(GestureDirection.Up, Move(tracker, 12, -40));
        for (int i = 0; i < 12; i++)
            Assert.Null(Move(tracker, 12, -40));
    }

    [Fact]
    public void APauseStartsANewStroke()
    {
        var tracker = Pressed();
        Assert.Equal(GestureDirection.Right, Move(tracker, 150, 0));
        Assert.Null(Move(tracker, 100, 0));
        Assert.Null(Move(tracker, 40, 0, afterMs: GestureTracker.PauseMs));
        Assert.Equal(GestureDirection.Right, Move(tracker, 40, 0));
    }

    [Fact]
    public void FiresAfreshEachPress()
    {
        var tracker = Pressed();
        Assert.Equal(GestureDirection.Up, Move(tracker, 0, -150));

        tracker.Reset();
        Assert.Null(tracker.Direction);
        Assert.Null(Move(tracker, 500, 0));
        Assert.Equal(GestureDirection.Left, Move(tracker, -150, 0));
    }

    [Fact]
    public void SmallJitterIsAClick()
    {
        var tracker = Pressed();
        foreach (var (dx, dy) in new[] { (3, -2), (-5, 4), (8, 1), (-2, -6) })
            Assert.Null(Move(tracker, dx, dy));
        Assert.True(tracker.IsClick);
    }

    [Fact]
    public void ADriftShortOfTheThresholdIsAClick()
    {
        var tracker = Pressed();
        Assert.Null(Move(tracker, 60, 0));
        Assert.True(tracker.IsClick);
    }

    [Fact]
    public void ALongDiagonalIsNeitherADirectionNorAClick()
    {
        var tracker = Pressed();
        Assert.Null(Move(tracker, 150, -150));
        Assert.False(tracker.IsClick);
    }

    [Fact]
    public void TheThresholdIsTheSameDistanceAtAnyDpi()
    {
        var tracker = Pressed(dpi: 4000);
        Assert.Null(Move(tracker, 130, 0));
        Assert.True(tracker.IsClick);
        Assert.Equal(GestureDirection.Right, Move(tracker, 400, 0));
    }

    // Right leads on: one more step right, which turns up or down. Up and left are leaves, as today.
    private static readonly GestureActions Paths = new(
        ButtonAction.Nothing, new GestureStep(ActionTypes.Press, ["KeyU"]), GestureStep.Nothing, new GestureStep(ActionTypes.Press, ["KeyL"]),
        new GestureStep(ActionTypes.None, Right: new GestureStep(ActionTypes.None,
            Up: new GestureStep(ActionTypes.Press, ["KeyT"]), Down: new GestureStep(ActionTypes.Press, ["KeyB"]))));

    // At 1000 DPI and 6 mm spacing, the first step is at 79 counts and the second at 315.
    private GestureTracker OnPaths()
    {
        var tracker = new GestureTracker();
        tracker.Reset(Paths);
        Assert.Null(Move(tracker, -400, 300));
        return tracker;
    }

    private static string Keys(GestureTracker tracker) => string.Join("+", tracker.Step?.Keys ?? []);

    [Fact]
    public void ADirectionThatLeadsOnStartsAPathInsteadOfFiring()
    {
        var tracker = OnPaths();
        Assert.Null(Move(tracker, 100, 0));
        Assert.Equal(PathChange.Reached, tracker.Change);
        Assert.Equal([GestureDirection.Right], tracker.Path);
        Assert.Null(tracker.Direction);
        Assert.False(tracker.IsClick);
    }

    [Fact]
    public void EachStepOfARunIsOneSpacingFurther()
    {
        var tracker = OnPaths();
        Move(tracker, 100, 0);
        Assert.Null(Move(tracker, 200, 0));
        Assert.Equal(PathChange.None, tracker.Change);
        Move(tracker, 30, 0);
        Assert.Equal(PathChange.ReachedBranch, tracker.Change);
        Assert.Equal([GestureDirection.Right, GestureDirection.Right], tracker.Path);
    }

    [Fact]
    public void ABranchTurnsAtRightAngles()
    {
        var tracker = OnPaths();
        Move(tracker, 100, 0);
        Move(tracker, 230, 0);
        Move(tracker, 0, -40);
        Assert.Equal(2, tracker.Path.Count);
        Move(tracker, 0, -40);
        Assert.Equal([GestureDirection.Right, GestureDirection.Right, GestureDirection.Up], tracker.Path);
        Assert.Equal("KeyT", Keys(tracker));
    }

    [Fact]
    public void MovingOnwardsAtABranchDoesNotCountAgainstTheTurn()
    {
        var tracker = OnPaths();
        Move(tracker, 100, 0);
        Move(tracker, 230, 0);
        Move(tracker, 300, 0);
        Assert.Equal(2, tracker.Path.Count);
        Move(tracker, 0, 90);
        Assert.Equal("KeyB", Keys(tracker));
    }

    [Fact]
    public void ADiagonalAtABranchTurnsNowhere()
    {
        var tracker = OnPaths();
        Move(tracker, 100, 0);
        Move(tracker, 230, 0);
        Move(tracker, -50, -80);
        Assert.Equal(2, tracker.Path.Count);
    }

    [Fact]
    public void MovementAcrossARunIsDropped()
    {
        var tracker = OnPaths();
        Move(tracker, 100, 0);
        Move(tracker, 0, -200);
        Move(tracker, 0, 400);
        Assert.Equal([GestureDirection.Right], tracker.Path);
    }

    [Fact]
    public void BackingOffReturnsToTheStepBeforeAfterTheHysteresis()
    {
        var tracker = OnPaths();
        Move(tracker, 100, 0);
        Move(tracker, 230, 0);
        Move(tracker, -40, 0);
        Assert.Equal(2, tracker.Path.Count);
        Move(tracker, -40, 0);
        Assert.Equal(PathChange.BackedOff, tracker.Change);
        Assert.Equal([GestureDirection.Right], tracker.Path);
    }

    [Fact]
    public void BackingOutOfAPathCancelsIt()
    {
        var tracker = OnPaths();
        Move(tracker, 100, 0);
        Move(tracker, -70, 0);
        Assert.Empty(tracker.Path);
        Assert.Null(tracker.Step);
        Assert.False(tracker.IsClick);
    }

    [Fact]
    public void AfterBackingOutAnotherDirectionCanBePicked()
    {
        var tracker = OnPaths();
        Move(tracker, 100, 0);
        Move(tracker, -70, 0);
        Assert.Null(Move(tracker, -70, 0));
        Assert.Equal(GestureDirection.Left, Move(tracker, -50, 0));
    }

    [Fact]
    public void LeavesBesidePathsStillFireAsTheyAreReached()
    {
        var tracker = OnPaths();
        Assert.Equal(GestureDirection.Up, Move(tracker, 0, -100));
        Assert.Empty(tracker.Path);
    }

    [Fact]
    public void AStrokeCanTurnIntoAPath()
    {
        var tracker = OnPaths();
        Assert.Equal(GestureDirection.Up, Move(tracker, 0, -100));
        Assert.Null(Move(tracker, 100, 0));
        Assert.Equal([GestureDirection.Right], tracker.Path);
        Move(tracker, 230, 0);
        Assert.Equal(2, tracker.Path.Count);
    }

    [Fact]
    public void AJunctionHasNoKeys()
    {
        var tracker = OnPaths();
        Move(tracker, 100, 0);
        Assert.False(tracker.Step!.Action.IsSet);
    }

    // Right, right repeats; up leads to a step that repeats and turns right.
    private static readonly GestureActions Repeats = new(
        ButtonAction.Nothing,
        new GestureStep(ActionTypes.None, Up: new GestureStep(ActionTypes.Press, ["KeyU"], Repeat: true, Right: new GestureStep(ActionTypes.Press, ["KeyT"]))),
        GestureStep.Nothing, GestureStep.Nothing,
        new GestureStep(ActionTypes.None, Right: new GestureStep(ActionTypes.Press, ["KeyR"], Repeat: true)));

    private GestureTracker OnRepeats()
    {
        var tracker = new GestureTracker();
        tracker.Reset(Repeats);
        Assert.Null(Move(tracker, -400, 300));
        return tracker;
    }

    [Fact]
    public void ARepeatingStepRunsAsItsReachedAndAgainAsTheMovementGoesOn()
    {
        var tracker = OnRepeats();
        Move(tracker, 100, 0);
        Assert.Equal(PathChange.Reached, tracker.Change);
        Move(tracker, 230, 0);
        Assert.Equal(PathChange.Ran, tracker.Change);
        Assert.Equal("KeyR", Keys(tracker));
        Move(tracker, 100, 0);
        Assert.Equal(PathChange.None, tracker.Change);
        Move(tracker, 60, 0);
        Assert.Equal(PathChange.Ran, tracker.Change);
        Move(tracker, 160, 0);
        Assert.Equal(PathChange.Ran, tracker.Change);
        Assert.Equal(2, tracker.Path.Count);
        // It has run, so letting go runs nothing more.
        Assert.Null(tracker.ReleaseStep);
    }

    [Fact]
    public void BackingOffARepeatingStepStopsIt()
    {
        var tracker = OnRepeats();
        Move(tracker, 100, 0);
        Move(tracker, 230, 0);
        Move(tracker, -80, 0);
        Assert.Equal(PathChange.BackedOff, tracker.Change);
        Assert.Equal([GestureDirection.Right], tracker.Path);
        Move(tracker, 80, 0);
        Assert.Equal(PathChange.Ran, tracker.Change);
    }

    [Fact]
    public void ARepeatingStepCanStillTurn()
    {
        var tracker = OnRepeats();
        Move(tracker, 0, -100);
        Move(tracker, 0, -230);
        Assert.Equal(PathChange.Ran, tracker.Change);
        Move(tracker, 90, 0);
        Assert.Equal([GestureDirection.Up, GestureDirection.Up, GestureDirection.Right], tracker.Path);
        Assert.Equal("KeyT", tracker.ReleaseStep!.Keys![0]);
    }

    [Fact]
    public void ARunStopsAtItsLastStep()
    {
        var tracker = OnPaths();
        Move(tracker, 100, 0);
        Move(tracker, 230, 0);
        Move(tracker, 600, 0);
        Assert.Equal(2, tracker.Path.Count);
    }

    // Swipes recorded with `mx gestures --trace` on an MX Master 4, in this order: slow drags, flicks, diagonals,
    // then clicks, with one last drag up. U, D, L and R are directions, x a diagonal that fires nothing, c a click.
    private const string RecordedSwipes = "swipes-1000dpi.jsonl";
    private const string MeantSwipes = "UUUUU LLLLL DDDDD RRRR UUUU DDDD LLLL RRRRRRR xxx ccc U";

    [Fact]
    public void RecordedSwipesFireWhatWasMeantOnce()
    {
        var presses = Replay(RecordedSwipes, repeat: false);
        Assert.Equal(MeantSwipes.Replace(" ", ""), string.Concat(presses));
    }

    [Fact]
    public void RecordedSwipesOnlyRepeatWhatWasMeant()
    {
        var presses = Replay(RecordedSwipes, repeat: true);
        var meant = MeantSwipes.Replace(" ", "");
        Assert.Equal(meant.Length, presses.Count);
        for (int i = 0; i < meant.Length; i++)
            Assert.Matches(meant[i] is 'x' or 'c' ? $"^{meant[i]}$" : $"^{meant[i]}+$", presses[i]);
    }

    /// <summary>What each press of a trace fires, as one letter per direction, or x or c when it fires none.</summary>
    private static List<string> Replay(string file, bool repeat)
    {
        var lines = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Traces", file))
            .Select(line => JsonDocument.Parse(line).RootElement).ToList();
        var tracker = new GestureTracker { Dpi = lines[0].GetProperty("dpi").GetInt32() };
        var presses = new List<string>();
        var fired = "";
        foreach (var report in lines.Skip(1))
        {
            long now = (long)report.GetProperty("t").GetDouble();
            if (report.TryGetProperty("held", out var held))
            {
                if (held.GetBoolean())
                {
                    tracker.Reset(_ => repeat);
                    fired = "";
                }
                else
                {
                    presses.Add(fired is not "" ? fired : tracker.IsClick ? "c" : "x");
                }
            }
            else if (tracker.Add(report.GetProperty("dx").GetInt32(), report.GetProperty("dy").GetInt32(), now) is { } d)
            {
                fired += d.ToString()[0];
            }
        }
        return presses;
    }
}
