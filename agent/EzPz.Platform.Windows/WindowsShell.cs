using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using EzPz.Imaging;
using Microsoft.Win32;

namespace EzPz.Platform.Windows;

/// <summary>
/// The Win32 side of the agent's own window: a thread of its own that runs the message loop the window needs, for as
/// long as the agent runs, and the calls that give the borderless Photino window the look the app had under Tauri.
/// The webview (WebView2) wants a single-threaded apartment, which the thread is.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class WindowsShell
{
    private const uint WmApp = 0x8000 + 2;
    private const uint WmNcLButtonDown = 0x00A1;
    private const nint HtCaption = 2;
    private static readonly nint MessageOnly = -3; // HWND_MESSAGE
    private static readonly nint PerMonitorAwareV2 = -4;

    private const int ExStyleIndex = -20; // GWL_EXSTYLE
    private const nint ExLayered = 0x00080000;
    private const int BackgroundIndex = -10; // GCLP_HBRBACKGROUND
    private const int BlackBrush = 4;
    private const int ShowMinimized = 6, ShowRestore = 9;
    private const uint NoZOrder = 0x4, NoActivate = 0x10;
    private const uint MonitorNearest = 2;
    private const uint RenderFullContent = 2; // PW_RENDERFULLCONTENT, which includes the webview's composition

    // DwmSetWindowAttribute
    private const uint ImmersiveDarkMode = 20, CornerPreference = 33, SystemBackdrop = 38;
    private const int RoundCorners = 2, AcrylicBackdrop = 3;

    private static readonly ConcurrentQueue<Action> s_work = new();
    private static readonly object s_lock = new();
    private static readonly ManualResetEventSlim s_ready = new();
    private static Thread? s_thread;
    private static nint s_dispatcher;

    /// <summary>Runs <paramref name="work"/> on the window thread, later, starting the thread the first time.</summary>
    public static void Post(Action work)
    {
        lock (s_lock)
        {
            if (s_thread is null)
            {
                s_thread = new Thread(Run) { IsBackground = true, Name = "Window" };
                s_thread.SetApartmentState(ApartmentState.STA);
                s_thread.Start();
            }
        }
        s_ready.Wait();
        s_work.Enqueue(work);
        PostMessageW(s_dispatcher, WmApp, 0, 0);
    }

    private static void Run()
    {
        // Pixels on every monitor; the window is sized from its monitor's DPI in Style.
        SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        nint module = GetModuleHandleW(null);
        fixed (char* name = "EzPzWindowThread")
        {
            var windowClass = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                WindowProc = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&DispatcherProc,
                Instance = module,
                ClassName = (nint)name,
            };
            RegisterClassExW(&windowClass);
            // A message-only window, never shown: it only receives Post's wake-ups.
            s_dispatcher = CreateWindowExW(0, name, name, 0, 0, 0, 0, 0, MessageOnly, 0, module, 0);
        }
        s_ready.Set();
        while (GetMessageW(out var message, 0, 0, 0) > 0)
        {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }

    [UnmanagedCallersOnly]
    private static nint DispatcherProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message != WmApp)
            return DefWindowProcW(hwnd, message, wParam, lParam);
        while (s_work.TryDequeue(out var work))
        {
            try
            {
                work();
            }
            catch (Exception error)
            {
                // An exception can't cross into Windows.
                Console.Error.WriteLine($"Window-thread work failed: {error}");
            }
        }
        return 0;
    }

    /// <summary>
    /// Sizes the window for its monitor, centered, and draws it as the Tauri build did: rounded, with the system's
    /// acrylic behind a see-through page where Windows has it, in the system's light or dark. The window thread.
    /// </summary>
    public static void Style(nint hwnd, int width, int height)
    {
        // Photino makes a see-through window layered, which DWM won't draw a backdrop behind; the webview's own
        // transparency is all the page needs.
        SetWindowLongPtrW(hwnd, ExStyleIndex, GetWindowLongPtrW(hwnd, ExStyleIndex) & ~ExLayered);

        uint dpi = GetDpiForWindow(hwnd);
        int w = (int)Math.Round(width * dpi / 96.0), h = (int)Math.Round(height * dpi / 96.0);
        var monitor = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
        GetMonitorInfoW(MonitorFromWindow(hwnd, MonitorNearest), &monitor);
        var work = monitor.Work;
        SetWindowPos(hwnd, 0, work.Left + (work.Right - work.Left - w) / 2, work.Top + (work.Bottom - work.Top - h) / 2,
            w, h, NoZOrder | NoActivate);

        int dark = AppsUseDarkTheme() ? 1 : 0;
        DwmSetWindowAttribute(hwnd, ImmersiveDarkMode, &dark, sizeof(int));
        int corners = RoundCorners;
        DwmSetWindowAttribute(hwnd, CornerPreference, &corners, sizeof(int));
        int backdrop = AcrylicBackdrop;
        // Windows 11 22H2 and later; elsewhere the page shows over the window's plain background.
        if (DwmSetWindowAttribute(hwnd, SystemBackdrop, &backdrop, sizeof(int)) == 0)
        {
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, &margins);
            // Black is where DWM draws the backdrop; Photino's class paints a solid grey.
            SetClassLongPtrW(hwnd, BackgroundIndex, GetStockObject(BlackBrush));
            InvalidateRect(hwnd, null, 1);
        }
        else
        {
            // Keeps the shadow a borderless window would otherwise lose.
            var margins = new Margins { Top = 1 };
            DwmExtendFrameIntoClientArea(hwnd, &margins);
        }
    }

    private static bool AppsUseDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    /// <summary>
    /// Moves the window with the mouse, from a press the page saw where it has no control, as a press on a title bar
    /// would. The window thread.
    /// </summary>
    public static void StartDrag(nint hwnd)
    {
        if (!GetCursorPos(out var point))
            return;
        // The webview holds the capture from the press; Windows moves the window only once it's let go.
        ReleaseCapture();
        PostMessageW(hwnd, WmNcLButtonDown, HtCaption, ((point.Y & 0xFFFF) << 16) | (point.X & 0xFFFF));
    }

    public static void Minimize(nint hwnd) => ShowWindow(hwnd, ShowMinimized);

    /// <summary>
    /// Brings the window forward. Windows lets a background process do that only when the one in front allows it,
    /// as a second launch of the app does before asking the agent.
    /// </summary>
    public static void Focus(nint hwnd)
    {
        if (IsIconic(hwnd))
            ShowWindow(hwnd, ShowRestore);
        SetForegroundWindow(hwnd);
    }

    /// <summary>Lets the agent bring its window forward, from the process the user just started. Any process.</summary>
    public static void AllowForeground() => AllowSetForegroundWindow(-1); // ASFW_ANY

    /// <summary>The window's client area as it's drawn now, webview included, as a PNG. The window thread.</summary>
    public static byte[]? Snapshot(nint hwnd)
    {
        if (hwnd == 0 || !GetClientRect(hwnd, out var client))
            return null;
        int width = client.Right - client.Left, height = client.Bottom - client.Top;
        if (width <= 0 || height <= 0)
            return null;
        nint screen = GetDC(hwnd);
        nint memory = CreateCompatibleDC(screen);
        // Top-down, so rows come out in reading order.
        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader), Width = width, Height = -height, Planes = 1, BitCount = 32,
        };
        nint bits;
        nint bitmap = CreateDIBSection(screen, &header, 0, &bits, 0, 0);
        nint previous = SelectObject(memory, bitmap);
        try
        {
            if (!PrintWindow(hwnd, memory, RenderFullContent))
                return null;
            var pixels = new Span<byte>((void*)bits, width * height * 4).ToArray();
            // The backdrop isn't part of the window's own drawing, and where it shows the alpha means nothing.
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
            return Png.FromBgra(pixels, width, height);
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(hwnd, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size, Style;
        public nint WindowProc;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background, MenuName, ClassName, SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Hwnd;
        public uint Id;
        public nint WParam, LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }

    [LibraryImport("user32.dll")] private static partial nint SetThreadDpiAwarenessContext(nint context);
    [LibraryImport("user32.dll")] private static partial ushort RegisterClassExW(WindowClass* windowClass);
    [LibraryImport("user32.dll")]
    private static partial nint CreateWindowExW(uint exStyle, char* className, char* name, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);
    [LibraryImport("user32.dll")] private static partial nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);
    [LibraryImport("user32.dll")] private static partial int GetMessageW(out Message message, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool TranslateMessage(Message* message);
    [LibraryImport("user32.dll")] private static partial nint DispatchMessageW(Message* message);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [LibraryImport("user32.dll")] private static partial nint GetWindowLongPtrW(nint hwnd, int index);
    [LibraryImport("user32.dll")] private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [LibraryImport("user32.dll")] private static partial nint SetClassLongPtrW(nint hwnd, int index, nint value);
    [LibraryImport("user32.dll")] private static partial uint GetDpiForWindow(nint hwnd);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetMonitorInfoW(nint monitor, MonitorInfo* info);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool InvalidateRect(nint hwnd, Rect* rect, int erase);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetCursorPos(out Point point);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ReleaseCapture();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ShowWindow(nint hwnd, int command);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsIconic(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetForegroundWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool AllowSetForegroundWindow(int processId);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetClientRect(nint hwnd, out Rect rect);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool PrintWindow(nint hwnd, nint dc, uint flags);
    [LibraryImport("user32.dll")] private static partial nint GetDC(nint hwnd);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint hwnd, nint dc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateDIBSection(nint dc, BitmapInfoHeader* info, uint usage, nint* bits, nint section, uint offset);
    [LibraryImport("gdi32.dll")] private static partial nint SelectObject(nint dc, nint item);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteObject(nint item);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteDC(nint dc);
    [LibraryImport("gdi32.dll")] private static partial nint GetStockObject(int index);
    [LibraryImport("dwmapi.dll")] private static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, void* value, int size);
    [LibraryImport("dwmapi.dll")] private static partial int DwmExtendFrameIntoClientArea(nint hwnd, Margins* margins);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint GetModuleHandleW(string? name);
}
