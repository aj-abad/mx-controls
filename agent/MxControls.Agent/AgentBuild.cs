using System.Security.Cryptography;

namespace MxControls.Agent;

/// <summary>
/// Identifies this build by its executable's contents, which needs no stamping and tells apart builds of the same
/// commit. The app compares it with the agent beside it, computing the same hash, to spot an agent an update left running.
/// </summary>
public static class AgentBuild
{
    /// <summary>The running executable's hash, or null if it couldn't be read.</summary>
    public static string? Hash { get; } = Environment.ProcessPath is { } path ? TryHashFile(path) : null;

    /// <summary>The lowercase hex SHA-256 of a file.</summary>
    public static string HashFile(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string? TryHashFile(string path)
    {
        try
        {
            return HashFile(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
