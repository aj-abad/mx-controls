using EzPz.Platform;
using EzPz.Platform.MacOS;
using EzPz.Platform.Windows;

namespace EzPz;

internal static class Platforms
{
    public static IPlatform Current()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsPlatform();
        if (OperatingSystem.IsMacOS())
            return new MacPlatform();
        throw new PlatformNotSupportedException("ezPz has Windows and macOS backends so far.");
    }
}
