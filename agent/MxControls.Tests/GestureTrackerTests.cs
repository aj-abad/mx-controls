using System.Text.Json;
using MxControls.Actions;

namespace MxControls.Tests;

// At the default 1000 DPI the 2 mm threshold is 79 counts.
public class GestureTrackerTests
{
    // Each press opens with a stale report, which the tracker must ignore.
    private static GestureTracker Pressed(int dpi = 1000)
    {
        var tracker = new GestureTracker { Dpi = dpi };
        tracker.Reset();
        Assert.Null(tracker.Add(-400, 300));
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
        Assert.Equal(expected, Pressed().Add(dx, dy));
    }

    [Fact]
    public void IgnoresTheFirstReportOfAPress()
    {
        var tracker = new GestureTracker();
        tracker.Reset();
        Assert.Null(tracker.Add(351, 12));
        Assert.Null(tracker.Add(-40, 0));
        Assert.Equal(GestureDirection.Left, tracker.Add(-40, 0));
    }

    [Fact]
    public void WaitsForTheThresholdAcrossReports()
    {
        var tracker = Pressed();
        Assert.Null(tracker.Add(30, 0));
        Assert.Null(tracker.Add(30, 5));
        Assert.Equal(GestureDirection.Right, tracker.Add(30, 0));
    }

    [Fact]
    public void AQuickFlickFiresAndIsNoClick()
    {
        var tracker = Pressed();
        Assert.Equal(GestureDirection.Left, tracker.Add(-200, 10));
        Assert.False(tracker.IsClick);
    }

    [Fact]
    public void ADiagonalPicksNothingUntilItStraightensOut()
    {
        var tracker = Pressed();
        Assert.Null(tracker.Add(120, 120));
        Assert.Null(tracker.Add(60, 0));
        Assert.Equal(GestureDirection.Right, tracker.Add(80, 0));
    }

    [Fact]
    public void FiresOncePerPress()
    {
        var tracker = Pressed();
        Assert.Equal(GestureDirection.Up, tracker.Add(0, -150));
        Assert.Null(tracker.Add(-500, 0));
        Assert.Equal(GestureDirection.Up, tracker.Direction);

        tracker.Reset();
        Assert.Null(tracker.Direction);
        Assert.Null(tracker.Add(500, 0));
        Assert.Equal(GestureDirection.Left, tracker.Add(-150, 0));
    }

    [Fact]
    public void SmallJitterIsAClick()
    {
        var tracker = Pressed();
        foreach (var (dx, dy) in new[] { (3, -2), (-5, 4), (8, 1), (-2, -6) })
            Assert.Null(tracker.Add(dx, dy));
        Assert.True(tracker.IsClick);
    }

    [Fact]
    public void ADriftShortOfTheThresholdIsAClick()
    {
        var tracker = Pressed();
        Assert.Null(tracker.Add(60, 0));
        Assert.True(tracker.IsClick);
    }

    [Fact]
    public void ALongDiagonalIsNeitherADirectionNorAClick()
    {
        var tracker = Pressed();
        Assert.Null(tracker.Add(150, -150));
        Assert.False(tracker.IsClick);
    }

    [Fact]
    public void TheThresholdIsTheSameDistanceAtAnyDpi()
    {
        var tracker = Pressed(dpi: 4000);
        Assert.Null(tracker.Add(130, 0));
        Assert.True(tracker.IsClick);
        Assert.Equal(GestureDirection.Right, tracker.Add(400, 0));
    }

    /// <summary>
    /// Replays swipes recorded with <c>mx gestures --trace</c> on an MX Master 4. They were made in this order:
    /// slow drags, flicks, diagonals, then clicks, with one last drag up. U, D, L and R are directions, x is a
    /// diagonal that fires nothing, and c a click.
    /// </summary>
    [Theory]
    [InlineData("swipes-1000dpi.jsonl", "UUUUU LLLLL DDDDD RRRR UUUU DDDD LLLL RRRRRRR xxx ccc U")]
    public void RecordedSwipesFireWhatWasMeant(string file, string expected)
    {
        var lines = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Traces", file))
            .Select(line => JsonDocument.Parse(line).RootElement).ToList();
        var tracker = new GestureTracker { Dpi = lines[0].GetProperty("dpi").GetInt32() };
        var results = new List<char>();
        foreach (var report in lines.Skip(1))
        {
            if (report.TryGetProperty("held", out var held))
            {
                if (held.GetBoolean())
                    tracker.Reset();
                else
                    results.Add(tracker.Direction is { } d ? d.ToString()[0] : tracker.IsClick ? 'c' : 'x');
            }
            else
            {
                tracker.Add(report.GetProperty("dx").GetInt32(), report.GetProperty("dy").GetInt32());
            }
        }
        Assert.Equal(expected.Replace(" ", ""), new string([.. results]));
    }
}
