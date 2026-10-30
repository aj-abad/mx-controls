namespace MxControls.Actions;

public enum GestureDirection { Up, Down, Left, Right }

/// <summary>
/// Adds up pointer movement while a gesture button is held and picks a direction once. Movement is in sensor
/// counts at the mouse's DPI, with y growing downwards, as the mouse reports it.
/// </summary>
/// <remarks>
/// A direction commits the moment the movement is long and straight enough, however soon after the press, so
/// a quick flick fires as readily as a slow drag. The threshold is a distance on the desk rather than a count,
/// so it feels the same at any DPI, and long enough that a click which drifts a little stays a click.
/// <para>
/// The first raw XY report of a press is stale: within 15 ms of the press, an MX Master 4 sends movement
/// from before it, often a centimetre or more and mostly against the swipe, so it's dropped. Losing a real
/// first report instead costs one radio interval of travel.
/// </para>
/// </remarks>
public sealed class GestureTracker(double thresholdMm = GestureTracker.DefaultThresholdMm)
{
    public const double DefaultThresholdMm = 2;

    // The other axis may drift up to this share of the travel, about 31 degrees off, before the movement counts
    // as a diagonal that picks no direction yet. Recorded straight swipes reached 29 degrees, diagonals 38.
    private const double CrossRatio = 0.6;

    private int _x;
    private int _y;
    private bool _stale;

    /// <summary>The sensor's resolution, which the threshold is converted with.</summary>
    public int Dpi { get; set; } = 1000;

    public GestureDirection? Direction { get; private set; }

    /// <summary>Travel so far along the longer axis, in millimetres.</summary>
    public double TravelMm => Math.Max(Math.Abs(_x), Math.Abs(_y)) * 25.4 / Dpi;

    private double Threshold => thresholdMm / 25.4 * Dpi;

    public void Reset()
    {
        _x = 0;
        _y = 0;
        _stale = true;
        Direction = null;
    }

    /// <summary>Returns the direction on the move that decides it, and null before and after.</summary>
    public GestureDirection? Add(int dx, int dy)
    {
        if (Direction is not null || _stale)
        {
            _stale = false;
            return null;
        }
        _x += dx;
        _y += dy;
        int x = Math.Abs(_x), y = Math.Abs(_y);
        int travel = Math.Max(x, y), cross = Math.Min(x, y);
        if (travel < Threshold || cross > CrossRatio * travel)
            return null;
        Direction = x >= y
            ? _x > 0 ? GestureDirection.Right : GestureDirection.Left
            : _y > 0 ? GestureDirection.Down : GestureDirection.Up;
        return Direction;
    }

    /// <summary>
    /// Whether a press that has just come up was a click: it picked no direction and moved less than the
    /// threshold. A long diagonal that never picked one is neither.
    /// </summary>
    public bool IsClick => Direction is null && Math.Max(Math.Abs(_x), Math.Abs(_y)) < Threshold;
}
