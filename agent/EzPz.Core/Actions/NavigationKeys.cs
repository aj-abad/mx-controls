namespace EzPz.Actions;

/// <summary>
/// The keys a platform sends for the back and forward buttons in programs that ignore those buttons, such as
/// Finder on a Mac. Each is <c>KeyboardEvent.code</c> names, modifiers first.
/// </summary>
public sealed record NavigationKeys(IReadOnlyList<string> Back, IReadOnlyList<string> Forward)
{
    /// <summary>Cmd+[ and Cmd+], which a Mac's Go menus show for Back and Forward.</summary>
    public static readonly NavigationKeys Default = new(["MetaLeft", "BracketLeft"], ["MetaLeft", "BracketRight"]);

    public const int MaxKeys = 6;

    public IReadOnlyList<string> For(string button) => button == MouseButtons.Back ? Back : Forward;

    public NavigationKeys With(string button, IReadOnlyList<string> keys) =>
        button == MouseButtons.Back ? this with { Back = keys } : this with { Forward = keys };
}
