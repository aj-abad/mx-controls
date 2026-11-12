using System.Text.Json.Serialization;
using MxControls.Platform;

namespace MxControls.Actions;

/// <summary>
/// One option of a <see cref="RoseMenu"/>. Its action runs once, when the button comes up with it highlighted. A
/// folder runs nothing: pushing on to the rose's rim while it's highlighted opens its own ring of options, which
/// can hold folders in turn.
/// </summary>
/// <param name="Label">What the rose shows; the agent falls back to a short name for the action.</param>
/// <param name="Icon">A Phosphor icon name, for the app to show.</param>
/// <param name="IconPath">
/// The icon's SVG path data on a 256-unit square, which the overlay draws; the agent has no icon font of its own.
/// </param>
/// <param name="PassThrough">
/// In the second layer: shows and runs the main layer's option in the same place instead of <see cref="Action"/>.
/// Inside a folder, the place is in the main layer's folder in the same place.
/// </param>
/// <param name="Items">For a folder, its options; null for an option that runs <see cref="Action"/>.</param>
public sealed record RoseItem(
    ButtonAction Action, string? Label = null, string? Icon = null, string? IconPath = null, bool PassThrough = false,
    IReadOnlyList<RoseItem>? Items = null)
{
    public const int MaxLabel = 24;
    public const int MaxIconPath = 16_000;

    public static readonly RoseItem Empty = new(ButtonAction.Nothing);

    [JsonIgnore]
    public bool IsFolder => Items is not null;

    /// <summary>Whether it's the main layer's option showing through a second-layer rose.</summary>
    [JsonIgnore]
    public bool FromMain { get; private init; }

    /// <summary>Whether reaching it means something: it runs an action or opens a folder.</summary>
    [JsonIgnore]
    public bool IsSet => IsFolder || Action.IsSet;

    /// <summary>Every action it runs, its folder's included; a pass-through option runs the main layer's.</summary>
    [JsonIgnore]
    public IEnumerable<ButtonAction> AllActions =>
        PassThrough ? [] : Items is { } items ? items.SelectMany(i => i.AllActions) : [Action];

    public string? Problem(IPlatform platform)
    {
        if (Label is { Length: > MaxLabel })
            return $"A rose option's label is at most {MaxLabel} characters.";
        if (IconPath is { } path && (path.Length > MaxIconPath || !Svg.SvgPath.TryParse(path, out _)))
            return "A rose option's icon isn't a path the agent can draw.";
        if (PassThrough)
            return IsFolder ? "An option that shows the main layer's can't be a folder of its own." : null;
        if (Items is { } items)
            return Action.IsSet ? "A folder opens its options, so it runs no action of its own." : RoseMenu.ItemsProblem(items, platform);
        if (Action.Type == ActionTypes.Hold)
            return "A rose option runs when you let go, so it taps keys rather than holding them.";
        if (Action.HoldsLayer)
            return "A rose option runs when you let go, so it can toggle the second layer but not hold it.";
        if (Action.Repeat)
            return "A rose option runs once, so it can't repeat.";
        return Action.Problem(platform);
    }

    /// <summary>
    /// This second-layer option over <paramref name="below"/>, the main layer's in the same place: a pass-through
    /// option becomes it, or nothing where there's none, and a folder passes its own options through to the main
    /// layer's folder there.
    /// </summary>
    public RoseItem Over(RoseItem? below) =>
        PassThrough ? below is { PassThrough: false } shown ? shown.Marked() : Empty with { FromMain = true }
        : Items is { } items ? this with { Items = RoseMenu.Over(items, below?.Items) }
        : this;

    private RoseItem Marked() => this with { FromMain = true, Items = Items?.Select(i => i.Marked()).ToArray() };
}

/// <summary>
/// A ring of options around the pointer while a button is held: moving towards one highlights it, and letting go
/// runs it. Back in the middle, letting go runs nothing. The first option is at the top, the rest go clockwise.
/// Menus live in a library that buttons show by ID, so editing one changes every button showing it.
/// </summary>
/// <param name="Id">The menu's ID in the library; null for a rose carried whole on a binding from before it.</param>
/// <param name="Name">What the library calls it.</param>
/// <param name="Icon">A Phosphor icon name, as profiles have, or null.</param>
public sealed record RoseMenu(IReadOnlyList<RoseItem> Items, string? Id = null, string? Name = null, string? Icon = null)
{
    public const int MinItems = 2;
    public const int MaxItems = 8;

    public string? Problem(IPlatform platform) => ItemsProblem(Items, platform);

    public static string? ItemsProblem(IReadOnlyList<RoseItem>? items, IPlatform platform)
    {
        if (items is not { Count: >= MinItems and <= MaxItems })
            return $"A rose and each of its folders have {MinItems} to {MaxItems} options.";
        return items.Select(item => item.Problem(platform)).FirstOrDefault(problem => problem is not null);
    }

    /// <summary>Every action its options and folders run.</summary>
    [JsonIgnore]
    public IEnumerable<ButtonAction> AllActions => Items.SelectMany(i => i.AllActions);

    /// <summary>
    /// This second-layer rose with each pass-through option replaced by the main layer's option in the same place,
    /// or by nothing where the main layer has no rose or no option there.
    /// </summary>
    public RoseMenu Over(RoseMenu? main) => this with { Items = Over(Items, main?.Items) };

    public static IReadOnlyList<RoseItem> Over(IReadOnlyList<RoseItem> items, IReadOnlyList<RoseItem>? below) =>
        [.. items.Select((item, i) => item.Over(below?.ElementAtOrDefault(i)))];

    /// <summary>The options of the folder at the end of <paramref name="path"/>, by index from the top; null where it breaks off.</summary>
    public static IReadOnlyList<RoseItem>? At(IReadOnlyList<RoseItem> items, IEnumerable<int> path)
    {
        IReadOnlyList<RoseItem>? level = items;
        foreach (int index in path)
            level = level?.ElementAtOrDefault(index)?.Items;
        return level;
    }
}
