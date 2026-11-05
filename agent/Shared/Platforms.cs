using MxControls.Platform;
using MxControls.Platform.MacOS;
using MxControls.Platform.Windows;

namespace MxControls;

internal static class Platforms
{
    public static IPlatform Current()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsPlatform();
        if (OperatingSystem.IsMacOS())
            return new MacPlatform();
        throw new PlatformNotSupportedException("MX Studio has Windows and macOS backends so far.");
    }
}
