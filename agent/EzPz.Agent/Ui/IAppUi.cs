namespace EzPz.Agent.Ui;

/// <summary>The agent's own window, as clients reach it through the agent.</summary>
public interface IAppUi
{
    /// <summary>Opens the window, or brings it forward.</summary>
    void Show();

    /// <summary>
    /// Runs <paramref name="script"/>, the body of an async function, in the page, and gives what it returns as
    /// JSON. Fails if the window isn't open or the script throws.
    /// </summary>
    Task<string> EvalAsync(string script);

    /// <summary>The page as it's drawn now, as a PNG. The window's backdrop isn't in it.</summary>
    Task<byte[]> SnapshotAsync();
}
