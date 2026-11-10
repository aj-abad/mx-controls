using MxControls.Platform;

namespace MxControls.Actions;

/// <summary>
/// One option of a <see cref="RoseMenu"/>. Its action runs once, when the button comes up with it highlighted.
/// </summary>
/// <param name="Label">What the rose shows; the agent falls back to a short name for the action.</param>
/// <param name="Icon">A Phosphor icon name, for the app to show.</param>
/// <param name="IconPath">
/// The icon's SVG path data on a 256-unit square, which the overlay draws; the agent has no icon font of its own.
/// </param>
/// <param name="PassThrough">
/// In the second layer: shows and runs the main layer's option in the same place instead of <see cref="Action"/>.
/// </param>
public sealed record RoseItem(
    ButtonAction Action, string? Label = null, string? Icon = null, string? IconPath = null, bool PassThrough = false)
{
    public const int MaxLabel = 24;
    public const int MaxIconPath = 16_000;

    public static readonly RoseItem Empty = new(ButtonAction.Nothing);

    public string? Problem(IPlatform platform)
    {
        if (Label is { Length: > MaxLabel })
            return $"A rose option's label is at most {MaxLabel} characters.";
        if (IconPath is { } path && (path.Length > MaxIconPath || !Svg.SvgPath.TryParse(path, out _)))
            return "A rose option's icon isn't a path the agent can draw.";
        if (PassThrough)
            return null;
        if (Action.Type == ActionTypes.Hold)
            return "A rose option runs when you let go, so it taps keys rather than holding them.";
        if (Action.HoldsLayer)
            return "A rose option runs when you let go, so it can toggle the second layer but not hold it.";
        if (Action.Repeat)
            return "A rose option runs once, so it can't repeat.";
        return Action.Problem(platform);
    }
}

/// <summary>
/// A ring of options around the pointer while a button is held: moving towards one highlights it, and letting go
/// runs it. Back in the middle, letting go runs nothing. The first option is at the top, the rest go clockwise.
/// </summary>
public sealed record RoseMenu(IReadOnlyList<RoseItem> Items)
{
    public const int MinItems = 2;
    public const int MaxItems = 8;

    public string? Problem(IPlatform platform)
    {
        if (Items is not { Count: >= MinItems and <= MaxItems })
            return $"A rose has {MinItems} to {MaxItems} options.";
        return Items.Select(item => item.Problem(platform)).FirstOrDefault(problem => problem is not null);
    }

    /// <summary>Whether any option passes through to the main layer, which only the second layer's rose can do.</summary>
    public bool PassesThrough => Items.Any(item => item.PassThrough);

    /// <summary>
    /// This second-layer rose with each pass-through option replaced by the main layer's option in the same place,
    /// or by nothing where the main layer has no rose or no option there.
    /// </summary>
    public RoseMenu Over(RoseMenu? main) => PassesThrough
        ? new([.. Items.Select((item, i) => item.PassThrough
            ? main?.Items.ElementAtOrDefault(i) is { PassThrough: false } below ? below : RoseItem.Empty
            : item)])
        : this;
}
