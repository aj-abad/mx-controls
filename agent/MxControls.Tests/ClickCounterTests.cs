using MxControls.Actions;

namespace MxControls.Tests;

public class ClickCounterTests
{
    private const int Window = ClickCounter.DefaultWindowMs;

    [Fact]
    public void ASingleClickRunsAtOnceWithNothingBeyondIt()
    {
        var counter = new ClickCounter();
        Assert.Null(counter.Press(0));
        Assert.Equal(1, counter.Release(80, most: 1));
        Assert.Null(counter.Deadline);
        Assert.Null(counter.Press(120));
        Assert.Equal(1, counter.Release(200, most: 1));
    }

    [Fact]
    public void ASingleClickWaitsOutTheWindowWhenADoubleIsSet()
    {
        var counter = new ClickCounter();
        counter.Press(0);
        Assert.Null(counter.Release(80, most: 2));
        Assert.Equal(80 + Window, counter.Deadline);
        Assert.Null(counter.Expire(80 + Window - 1));
        Assert.Equal(1, counter.Expire(80 + Window));
        Assert.Null(counter.Deadline);
    }

    [Fact]
    public void TheHighestCountRunsAsSoonAsItsReached()
    {
        var counter = new ClickCounter();
        counter.Press(0);
        Assert.Null(counter.Release(60, most: 2));
        Assert.Null(counter.Press(150));
        Assert.Null(counter.Deadline);
        Assert.Equal(2, counter.Release(210, most: 2));
    }

    [Fact]
    public void ADoubleWaitsForATripleThenRunsWhenNoneComes()
    {
        var counter = new ClickCounter();
        counter.Press(0);
        counter.Release(60, most: 3);
        counter.Press(150);
        Assert.Null(counter.Release(210, most: 3));
        Assert.Equal(2, counter.Expire(210 + Window));
    }

    [Fact]
    public void ThreeQuickClicksRunTheTriple()
    {
        var counter = new ClickCounter();
        long t = 0;
        for (int i = 1; i <= 2; i++)
        {
            counter.Press(t);
            Assert.Null(counter.Release(t + 60, most: 3));
            t += 150;
        }
        counter.Press(t);
        Assert.Equal(3, counter.Release(t + 60, most: 3));
    }

    [Fact]
    public void APressAfterTheWindowRunsTheOldCountFirst()
    {
        // The timer can run late; the next press still mustn't add to a count whose window has closed.
        var counter = new ClickCounter();
        counter.Press(0);
        counter.Release(60, most: 2);
        Assert.Equal(1, counter.Press(60 + Window + 5));
        Assert.Null(counter.Release(120 + Window, most: 2));
    }

    [Fact]
    public void APressThatMovesRunsTheWaitingClicksFirst()
    {
        var counter = new ClickCounter();
        counter.Press(0);
        counter.Release(60, most: 2);
        counter.Press(150);
        Assert.Equal(1, counter.Interrupt());
        Assert.Null(counter.Interrupt());
        Assert.Null(counter.Deadline);
    }

    [Fact]
    public void ClearDropsTheWaitingClicks()
    {
        var counter = new ClickCounter();
        counter.Press(0);
        counter.Release(60, most: 3);
        counter.Clear();
        Assert.Null(counter.Expire(10_000));
        Assert.Null(counter.Interrupt());
    }

    [Fact]
    public void TheWindowCanChange()
    {
        var counter = new ClickCounter { Window = 150 };
        counter.Press(0);
        counter.Release(50, most: 2);
        Assert.Equal(200, counter.Deadline);
    }
}
