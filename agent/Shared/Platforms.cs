using MxControls.Platform;
using MxControls.Platform.Windows;

namespace MxControls;

internal static class Platforms
{
    public static IPlatform Current() =>
        OperatingSystem.IsWindows()
            ? new WindowsPlatform()
            : throw new PlatformNotSupportedException("MX Controls only has a Windows backend so far.");
}
