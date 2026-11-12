namespace MxControls.Actions;

public enum GestureDirection { Up, Down, Left, Right }

/// <summary>
/// Turns pointer movement while a gesture button is held into directions. Movement is in sensor counts at the
/// mouse's DPI, with y growing downwards, as the mouse reports it; times are milliseconds on any steady clock.
/// </summary>
/// <remarks>
/// <para>
/// A hold is a run of strokes. A stroke fires its direction the moment it has moved far and straight enough,
/// however soon after the press, so a quick flick fires as readily as a slow drag. The distances are on the
/// desk rather than in counts, so they feel the same at any DPI.
/// </para>
/// <para>
/// Once a stroke has fired, any other direction is measured from where the movement turned: the furthest point
/// reached, which follows the pointer while it keeps going. So moving back fires the opposite direction after
/// the same short distance, and a stroke that curves a little doesn't fire sideways. A repeating direction
/// fires again for every <see cref="DefaultRepeatStepMm"/> it keeps going; any other fires again after a
/// pause, when a new stroke starts.
/// </para>
/// <para>
/// A repeating direction also locks the rest of the press to its axis, so turning the volume up and down
/// never skips a track however much the hand drifts sideways: only movement along the axis counts.
/// </para>
/// <para>
/// The first raw XY report of a press is stale: within 15 ms of the press, an MX Master 4 sends movement
/// from before it, often a centimetre or more and mostly against the swipe, so it's dropped. Losing a real
/// first report instead costs one radio interval of travel.
/// </para>
/// </remarks>
public sealed class GestureTracker(
    double thresholdMm = GestureTracker.DefaultThresholdMm, double repeatStepMm = GestureTracker.DefaultRepeatStepMm)
{
    public const double DefaultThresholdMm = 2;
    public const double DefaultRepeatStepMm = 4;
    public const double HysteresisMm = 1.5;

    /// <summary>How long a stroke goes without moving on before the next movement starts a new one.</summary>
    public const int PauseMs = 150;

    // The other axis may drift up to this share of the travel, about 31 degrees off, before the movement counts
    // as a diagonal that picks no direction yet. Recorded straight swipes reached 29 degrees, diagonals 38.
    private const double CrossRatio = 0.6;

    // Position since the press; where the next direction is measured from; where the last one fired.
    private int _x, _y;
    private int _fromX, _fromY;
    private int _firedX, _firedY;
    private GestureDirection? _stroke;
    private bool? _lockedVertical;
    private long _movedOnAt;
    private bool _stale;
    private Func<GestureDirection, bool> _repeats = _ => false;

    /// <summary>The sensor's resolution, which distances are converted with.</summary>
    public int Dpi { get; set; } = 1000;

    /// <summary>The direction fired last in this press, or null if none has.</summary>
    public GestureDirection? Direction { get; private set; }

    /// <summary>Travel since the press along the longer axis, in millimetres.</summary>
    public double TravelMm => Math.Max(Math.Abs(_x), Math.Abs(_y)) * 25.4 / Dpi;

    /// <summary>
    /// Whether a press that has just come up was a click: it fired nothing and moved less than the threshold.
    /// A long diagonal that never picked a direction is neither.
    /// </summary>
    public bool IsClick => Direction is null && Math.Max(Math.Abs(_x), Math.Abs(_y)) < Counts(thresholdMm);

    /// <param name="repeats">Which directions fire again as the movement keeps going; none if null.</param>
    public void Reset(Func<GestureDirection, bool>? repeats = null)
    {
        _x = _y = _fromX = _fromY = _firedX = _firedY = 0;
        _stroke = null;
        _lockedVertical = null;
        _stale = true;
        _repeats = repeats ?? (_ => false);
        Direction = null;
    }

    /// <summary>Starts a press of this gesture, whose repeating directions fire again as the movement keeps going.</summary>
    public void Reset(GestureActions gesture) => Reset(direction => gesture[direction].Repeat);

    /// <summary>Returns the direction this movement fires, or null.</summary>
    public GestureDirection? Add(int dx, int dy, long now)
    {
        if (_stale)
        {
            _stale = false;
            _movedOnAt = now;
            return null;
        }
        if (_lockedVertical is { } vertical)
            (dx, dy) = vertical ? (0, dy) : (dx, 0);
        if (_stroke is not null && now - _movedOnAt >= PauseMs)
        {
            _stroke = null;
            (_fromX, _fromY) = (_x, _y);
        }
        _x += dx;
        _y += dy;

        if (_stroke is { } stroke)
        {
            var (ux, uy) = Unit(stroke);
            int forward = dx * ux + dy * uy, sideways = Math.Abs(dx * uy - dy * ux);
            if (forward > 0 && forward >= sideways)
            {
                // Mostly onwards: the turning point comes along, drift and all.
                (_fromX, _fromY) = (_x, _y);
                _movedOnAt = now;
            }
            else if ((_x - _fromX) * ux + (_y - _fromY) * uy > 0)
            {
                // Onwards only in part: the turning point keeps the furthest reach, not the sideways part.
                if (ux != 0)
                    _fromX = _x;
                else
                    _fromY = _y;
                _movedOnAt = now;
            }
            if (_repeats(stroke) && (_x - _firedX) * ux + (_y - _firedY) * uy >= Counts(repeatStepMm))
                return Fire(stroke, now);
        }

        int vx = _x - _fromX, vy = _y - _fromY;
        int x = Math.Abs(vx), y = Math.Abs(vy);
        int travel = Math.Max(x, y), cross = Math.Min(x, y);
        if (travel < Counts(thresholdMm) || cross > CrossRatio * travel)
            return null;
        var direction = x >= y
            ? vx > 0 ? GestureDirection.Right : GestureDirection.Left
            : vy > 0 ? GestureDirection.Down : GestureDirection.Up;
        if (direction == _stroke)
            return null;
        return Fire(direction, now);
    }

    private GestureDirection Fire(GestureDirection direction, long now)
    {
        _stroke = direction;
        Direction = direction;
        if (_repeats(direction))
            _lockedVertical ??= direction is GestureDirection.Up or GestureDirection.Down;
        (_fromX, _fromY) = (_firedX, _firedY) = (_x, _y);
        _movedOnAt = now;
        return direction;
    }

    private double Counts(double mm) => mm / 25.4 * Dpi;

    private static (int X, int Y) Unit(GestureDirection direction) => direction switch
    {
        GestureDirection.Up => (0, -1),
        GestureDirection.Down => (0, 1),
        GestureDirection.Left => (-1, 0),
        _ => (1, 0),
    };
}
