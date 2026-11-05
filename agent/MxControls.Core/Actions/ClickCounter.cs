namespace MxControls.Actions;

/// <summary>
/// Counts quick successive clicks of one control, to tell a single click from a double or triple one. Times are
/// milliseconds on any steady clock.
/// </summary>
/// <remarks>
/// A click only waits for another when a higher count could still come: with nothing set beyond a single click,
/// every click runs as soon as it's released, and the last count set runs as soon as it's reached. Otherwise
/// the clicks so far wait until the next press starts or <see cref="Window"/> runs out after the release.
/// </remarks>
public sealed class ClickCounter
{
    public const int DefaultWindowMs = 300;

    private int _count;

    /// <summary>How long after a release the next press may start and still add to the count.</summary>
    public int Window { get; set; } = DefaultWindowMs;

    /// <summary>When the clicks waiting run unless another press starts; null when nothing waits or a press is down.</summary>
    public long? Deadline { get; private set; }

    /// <summary>
    /// A press starts. Returns the count to run first if the window had already run out, which a late timer
    /// can leave behind; otherwise null.
    /// </summary>
    public int? Press(long now)
    {
        var expired = Expire(now);
        Deadline = null;
        return expired;
    }

    /// <summary>A press came up as a click. Returns the count to run now, or null while it waits for another.</summary>
    /// <param name="most">The highest count with an action, 1 to 3.</param>
    public int? Release(long now, int most)
    {
        _count++;
        if (_count < most)
        {
            Deadline = now + Window;
            return null;
        }
        return Take();
    }

    /// <summary>Returns the count waiting once its window has run out, or null.</summary>
    public int? Expire(long now) => Deadline is { } deadline && now >= deadline ? Take() : null;

    /// <summary>
    /// The press turned into something other than a click, such as a swipe. Returns the count that was waiting,
    /// so it runs before whatever the press does, or null.
    /// </summary>
    public int? Interrupt() => _count > 0 ? Take() : null;

    /// <summary>Forgets the clicks waiting without running them.</summary>
    public void Clear()
    {
        _count = 0;
        Deadline = null;
    }

    private int Take()
    {
        int count = _count;
        Clear();
        return count;
    }
}
