namespace MxControls.Actions;

/// <summary>
/// Turns pointer movement while a rose is open into a ghost cursor and the option it points at. Movement is in
/// sensor counts at the mouse's DPI, y growing downwards. The ghost stays inside the rose: pushing past the edge
/// drags its reach along, so turning back towards the middle answers at once rather than after the overshoot.
/// </summary>
/// <remarks>
/// Within <see cref="CenterMm"/> of the middle nothing is highlighted, so a press can always be let go of
/// without running anything. Leaving a highlighted option takes a little more than reaching it, at the
/// middle and between neighbours, so a hand resting on a boundary doesn't flicker between the two.
/// </remarks>
public sealed class RoseTracker
{
    /// <summary>Desk travel from the middle to the rose's edge.</summary>
    public const double RadiusMm = 12;

    /// <summary>Desk travel from the middle before an option is highlighted.</summary>
    public const double CenterMm = 3.6;

    /// <summary>Desk travel from the middle at which a highlighted folder opens: just short of the rim, where the ghost stops.</summary>
    public const double FolderMm = RadiusMm - 0.75;

    private const double CenterHysteresisMm = 0.8;
    private const double AngleHysteresis = 6 * Math.PI / 180;

    private double _xMm, _yMm;
    private bool _stale;
    private int _count = RoseMenu.MinItems;

    /// <summary>The sensor's resolution, which distances are converted with.</summary>
    public int Dpi { get; set; } = 1000;

    /// <summary>The highlighted option's index, or null in the middle.</summary>
    public int? Selected { get; private set; }

    /// <summary>Whether the ghost has left the middle at any point in this press.</summary>
    public bool LeftCenter { get; private set; }

    /// <summary>The ghost's position, -1 to 1 across the rose, y growing downwards.</summary>
    public double X => _xMm / RadiusMm;

    public double Y => _yMm / RadiusMm;

    /// <summary>Whether the ghost has been pushed out to the rim, where a highlighted folder opens.</summary>
    public bool AtRim => Math.Sqrt(_xMm * _xMm + _yMm * _yMm) >= FolderMm;

    /// <summary>The middle's share of the rose's radius, where nothing is highlighted.</summary>
    public static double CenterRatio => CenterMm / RadiusMm;

    /// <summary>
    /// How many options the rose has. Changing it, as switching layers can, picks again from where the ghost is.
    /// </summary>
    public int Count
    {
        get => _count;
        set
        {
            if (value == _count)
                return;
            _count = Math.Clamp(value, RoseMenu.MinItems, RoseMenu.MaxItems);
            Selected = null;
            Select();
        }
    }

    public void Reset(int count)
    {
        _xMm = _yMm = 0;
        _count = Math.Clamp(count, RoseMenu.MinItems, RoseMenu.MaxItems);
        Selected = null;
        LeftCenter = false;
        // As with gestures, the first raw report of a press carries movement from before it.
        _stale = true;
    }

    /// <summary>
    /// Starts again in the middle of another ring, as opening or leaving a folder does, keeping the next movement.
    /// </summary>
    /// <param name="leftCenter">Whether the press counts as having left the middle, so letting go isn't a click.</param>
    public void Enter(int count, bool leftCenter = true)
    {
        _xMm = _yMm = 0;
        _count = Math.Clamp(count, RoseMenu.MinItems, RoseMenu.MaxItems);
        Selected = null;
        LeftCenter = leftCenter;
    }

    /// <summary>Moves the ghost; returns whether <see cref="Selected"/> changed.</summary>
    public bool Add(int dx, int dy)
    {
        if (_stale)
        {
            _stale = false;
            return false;
        }
        _xMm += dx * 25.4 / Dpi;
        _yMm += dy * 25.4 / Dpi;
        double distance = Math.Sqrt(_xMm * _xMm + _yMm * _yMm);
        if (distance > RadiusMm)
        {
            _xMm *= RadiusMm / distance;
            _yMm *= RadiusMm / distance;
        }
        var before = Selected;
        Select();
        return Selected != before;
    }

    private void Select()
    {
        double distance = Math.Sqrt(_xMm * _xMm + _yMm * _yMm);
        if (distance < (Selected is null ? CenterMm : CenterMm - CenterHysteresisMm))
        {
            Selected = null;
            return;
        }
        LeftCenter = true;
        double width = 2 * Math.PI / _count;
        // Clockwise from straight up, with the first option centred on it.
        double angle = Math.Atan2(_xMm, -_yMm);
        if (Selected is { } current && Math.Abs(Wrap(angle - current * width)) < width / 2 + AngleHysteresis)
            return;
        double turned = (angle + width / 2) % (2 * Math.PI);
        if (turned < 0)
            turned += 2 * Math.PI;
        Selected = Math.Min(_count - 1, (int)(turned / width));
    }

    /// <summary>An angle brought into -π to π.</summary>
    private static double Wrap(double angle)
    {
        angle %= 2 * Math.PI;
        return angle > Math.PI ? angle - 2 * Math.PI : angle < -Math.PI ? angle + 2 * Math.PI : angle;
    }
}
