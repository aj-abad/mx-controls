using System.Text.Json;
using MxControls.Actions;
using MxControls.Agent;
using MxControls.Svg;

namespace MxControls.Tests;

public class RoseTests
{
    private static readonly FakePlatform Platform = new();
    // At 1000 DPI a millimetre is about 39 counts.
    private static int Mm(double mm) => (int)Math.Round(mm / 25.4 * 1000);

    private static ButtonAction Press(params string[] keys) => new(ActionTypes.Press, keys);

    private static RoseMenu Menu(int count) =>
        new([.. Enumerable.Range(0, count).Select(i => new RoseItem(Press("KeyC"), $"Option {i}"))]);

    private static RoseTracker Tracker(int count)
    {
        var tracker = new RoseTracker { Dpi = 1000 };
        tracker.Reset(count);
        // The first report of a press is dropped as stale.
        tracker.Add(0, 0);
        return tracker;
    }

    [Fact]
    public void TheFirstReportIsDropped()
    {
        var tracker = new RoseTracker { Dpi = 1000 };
        tracker.Reset(8);
        Assert.False(tracker.Add(0, -Mm(10)));
        Assert.Null(tracker.Selected);
        Assert.Equal(0, tracker.Y);
    }

    [Theory]
    [InlineData(0, -1, 0)]
    [InlineData(1, 0, 2)]
    [InlineData(0, 1, 4)]
    [InlineData(-1, 0, 6)]
    [InlineData(1, -1, 1)]
    public void OptionsGoClockwiseFromTheTop(int x, int y, int expected)
    {
        var tracker = Tracker(8);
        Assert.True(tracker.Add(x * Mm(8), y * Mm(8)));
        Assert.Equal(expected, tracker.Selected);
    }

    [Fact]
    public void TheMiddleHighlightsNothingAndLeavingItTakesLessToReturn()
    {
        var tracker = Tracker(4);
        tracker.Add(0, -Mm(3));
        Assert.Null(tracker.Selected);
        tracker.Add(0, -Mm(1));
        Assert.Equal(0, tracker.Selected);
        Assert.True(tracker.LeftCenter);
        // Just inside the middle's edge still holds the option, so a resting hand doesn't flicker.
        tracker.Add(0, Mm(0.6));
        Assert.Equal(0, tracker.Selected);
        tracker.Add(0, Mm(3.4));
        Assert.Null(tracker.Selected);
    }

    [Fact]
    public void TheGhostStaysInsideTheRoseSoTurningBackAnswersAtOnce()
    {
        var tracker = Tracker(4);
        tracker.Add(Mm(40), 0);
        Assert.Equal(1, tracker.X, 3);
        Assert.Equal(1, tracker.Selected);
        // Far past the edge, coming back the radius plus a little crosses straight to the other side.
        tracker.Add(-Mm(RoseTracker.RadiusMm + 5), 0);
        Assert.Equal(3, tracker.Selected);
    }

    [Fact]
    public void ABoundaryTakesALittleMoreToCrossBack()
    {
        var tracker = Tracker(4);
        tracker.Add(0, -Mm(8));
        Assert.Equal(0, tracker.Selected);
        // 47 degrees round is past the 45-degree boundary but inside the hysteresis.
        double angle = 47 * Math.PI / 180;
        tracker.Add(Mm(8 * Math.Sin(angle)), Mm(8) - Mm(8 * Math.Cos(angle)));
        Assert.Equal(0, tracker.Selected);
        tracker.Add(Mm(4), Mm(4));
        Assert.Equal(1, tracker.Selected);
    }

    [Fact]
    public void ChangingTheCountPicksAgainFromWhereTheGhostIs()
    {
        var tracker = Tracker(8);
        tracker.Add(Mm(8), 0);
        Assert.Equal(2, tracker.Selected);
        tracker.Count = 4;
        Assert.Equal(1, tracker.Selected);
    }

    [Fact]
    public void ASecondLayerRosePassesOptionsThroughByPlace()
    {
        var main = Menu(3);
        var second = new RoseMenu([
            new RoseItem(Press("KeyV"), "Paste"),
            new RoseItem(ButtonAction.Nothing, PassThrough: true),
            new RoseItem(ButtonAction.Nothing, PassThrough: true),
            new RoseItem(ButtonAction.Nothing, PassThrough: true),
        ]);
        var shown = second.Over(main);
        Assert.Equal("Paste", shown.Items[0].Label);
        Assert.Same(main.Items[1], shown.Items[1]);
        Assert.Same(main.Items[2], shown.Items[2]);
        // The main rose has no fourth option, so that place is empty.
        Assert.False(shown.Items[3].Action.IsSet);
        Assert.All(second.Over(null).Items.Skip(1), item => Assert.False(item.Action.IsSet));
        Assert.Same(main, main.Over(null));
    }

    [Fact]
    public void RoseBindingsAreChecked()
    {
        ButtonBinding Rose(RoseMenu? menu, ButtonAction? click = null) => new("gesture", ButtonBinding.RoseMode, click, Rose: menu);
        Assert.Null(Rose(Menu(2)).Problem(Platform));
        Assert.Null(Rose(Menu(8), Press("KeyV")).Problem(Platform));
        Assert.Contains("needs its options", Rose(null).Problem(Platform));
        Assert.Contains("2 to 8", Rose(Menu(1)).Problem(Platform));
        Assert.Contains("2 to 8", Rose(Menu(9)).Problem(Platform));
        Assert.Contains("taps keys", Rose(new([new(new(ActionTypes.Hold, ["KeyC"])), RoseItem.Empty])).Problem(Platform));
        Assert.Contains("can't repeat", Rose(new([new(Press("KeyC") with { Repeat = true }), RoseItem.Empty])).Problem(Platform));
        Assert.Contains("not hold it",
            Rose(new([new(new ButtonAction(ActionTypes.Layer, Layer: LayerModes.Momentary)), RoseItem.Empty])).Problem(Platform));
        Assert.Null(Rose(new([new(new ButtonAction(ActionTypes.Layer, Layer: LayerModes.Toggle)), RoseItem.Empty])).Problem(Platform));
        Assert.Contains("at most", Rose(new([new(Press("KeyC"), new string('x', 25)), RoseItem.Empty])).Problem(Platform));
        Assert.Contains("icon", Rose(new([new(Press("KeyC"), IconPath: "M 1"), RoseItem.Empty])).Problem(Platform));
        Assert.Contains("can't hold keys",
            Rose(Menu(2), new ButtonAction(ActionTypes.Hold, ["KeyC"])).Problem(Platform));
        Assert.Contains("double or triple",
            (Rose(Menu(2)) with { DoubleClick = Press("KeyC") }).Problem(Platform));
    }

    [Fact]
    public void RoseActionsCountAsTheBindingsActionsExceptPassThroughs()
    {
        var binding = new ButtonBinding("gesture", ButtonBinding.RoseMode, Rose: new([
            new RoseItem(new ButtonAction(ActionTypes.Macro, Macro: "m1")),
            new RoseItem(new ButtonAction(ActionTypes.Macro, Macro: "m2"), PassThrough: true),
        ]));
        Assert.Equal(["m1"], binding.AllActions.Select(a => a.Macro).OfType<string>());
        Assert.True(binding.NeedsRawXY);
        Assert.False(binding.Clicks.Single.IsSet);
    }

    [Fact]
    public void ARoseBindingRoundTripsThroughTheWireFormat()
    {
        var json = """
            [{"button":"gesture","mode":"rose","rose":{"items":[
              {"action":{"type":"press","keys":["KeyC"]},"label":"Copy","icon":"copy","iconPath":"M0,0H10V10Z"},
              {"action":{"type":"none"},"passThrough":true}]}}]
            """;
        var bindings = JsonSerializer.Deserialize<ButtonBinding[]>(json, Protocol.Json)!;
        var rose = bindings[0].Rose!;
        Assert.Equal("Copy", rose.Items[0].Label);
        Assert.True(rose.Items[1].PassThrough);
        Assert.Contains("\"passThrough\":true", JsonSerializer.Serialize(bindings, Protocol.Json));
    }

    [Fact]
    public void PathDataBecomesCubics()
    {
        var figures = SvgPath.Parse("M10,10 h20 v20 H10 z m40,0 l5-5 5,5");
        Assert.Equal(2, figures.Count);
        Assert.True(figures[0].Closed);
        Assert.Equal(new PathPoint(10, 10), figures[0].Start);
        Assert.Equal([new(30, 10), new(30, 30), new(10, 30)], figures[0].Segments.Select(s => s.End));
        // A move after a close starts from where the figure began; its extra pairs are lines.
        Assert.Equal(new PathPoint(50, 10), figures[1].Start);
        Assert.Equal([new(55, 5), new(60, 10)], figures[1].Segments.Select(s => s.End));
        Assert.False(figures[1].Closed);
    }

    [Fact]
    public void ArcsEndWhereTheySayAndStayOnTheirCircle()
    {
        // A full circle of radius 104 about (128, 128) in two halves, with flags written without separators.
        var figure = SvgPath.Parse("M128,24a104,104,0,1,0,0,208A104,104 0 1 0 128,24Z").Single();
        Assert.Equal(new PathPoint(128, 24), figure.Segments[^1].End);
        foreach (var segment in figure.Segments)
        {
            double r = Math.Sqrt(Math.Pow(segment.End.X - 128, 2) + Math.Pow(segment.End.Y - 128, 2));
            Assert.Equal(104, r, 6);
        }
        Assert.True(SvgPath.TryParse("M0 0a5 5 0 105 5", out var compact));
        Assert.Equal(new PathPoint(5, 5), compact.Single().Segments[^1].End);
    }

    [Fact]
    public void BrokenPathDataIsRefused()
    {
        Assert.False(SvgPath.TryParse("10,10 L 5", out _));
        Assert.False(SvgPath.TryParse("M10", out _));
        Assert.False(SvgPath.TryParse("M0,0 A5,5,0,2,0,5,5", out _));
    }
}
