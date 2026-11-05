namespace MxControls.Agent;

/// <summary>
/// Held for a process's life to keep it the only one of its kind for this user. A named mutex on Windows. Elsewhere a
/// locked file in the user's temp folder, since a NativeAOT program's named mutexes don't reach other processes there;
/// the system drops the lock when the process ends, however it ends.
/// </summary>
internal static class InstanceLock
{
    /// <summary>The held lock, or null when another process has it.</summary>
    public static IDisposable? TryAcquire(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            var mutex = new Mutex(initiallyOwned: true, name, out bool first);
            if (first)
                return mutex;
            mutex.Dispose();
            return null;
        }
        try
        {
            return new FileStream(PathFor(name), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static bool IsHeld(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!Mutex.TryOpenExisting(name, out var mutex))
                return false;
            mutex.Dispose();
            return true;
        }
        using var held = TryAcquire(name);
        return held is null;
    }

    private static string PathFor(string name) => Path.Combine(Path.GetTempPath(), $"{name}.lock");
}
