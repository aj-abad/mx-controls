using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MxControls.Svg;

namespace MxControls.Platform.Windows;

/// <summary>
/// The rose as a layered, click-through, topmost window on a thread of its own, drawn with GDI+ into a
/// premultiplied bitmap. The thread starts with the first rose. Calls only store the latest view and wake the
/// thread, so a burst of movement draws once.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WindowsRoseOverlay : IRoseOverlay
{
    private const float SizeDip = 420;
    private const float RadiusDip = 186;
    private const int FadeInMs = 90;

    private const uint WmApp = 0x8000 + 1;
    private const uint WmTimer = 0x0113;
    private const nint FadeTimer = 1;

    // The window procedure is a static function pointer, so it finds the overlay here; the agent has just one.
    private static WindowsRoseOverlay? s_overlay;

    private readonly object _lock = new();
    private RoseView? _pending;
    private bool _posted;
    private Thread? _thread;
    private nint _hwnd;

    // The thread's own.
    private bool _visible;
    private long _shownAt;
    private int _x, _y, _size;
    private float _scale = 1;
    private nint _memoryDc, _dib, _bits;
    private nint _bitmap, _graphics;
    private nint _font, _smallFont, _format;
    private readonly Dictionary<string, IReadOnlyList<PathFigure>?> _icons = [];

    public void Show(RoseView view) => Post(view);

    public void Hide() => Post(null);

    private void Post(RoseView? view)
    {
        lock (_lock)
        {
            _pending = view;
            if (_thread is null)
            {
                if (view is null)
                    return;
                _thread = new Thread(Run) { IsBackground = true, Name = "Rose overlay" };
                _thread.Start();
                return;
            }
            if (_hwnd == 0 || _posted)
                return;
            _posted = true;
        }
        Native.PostMessageW(_hwnd, WmApp, 0, 0);
    }

    private void Run()
    {
        // Pixels, not the 96 DPI the agent's process is scaled to, so the rose is sharp on every monitor.
        Native.SetThreadDpiAwarenessContext(Native.PerMonitorAwareV2);
        s_overlay = this;
        var input = new Gdip.StartupInput { Version = 1 };
        if (Gdip.GdiplusStartup(out _, &input, 0) != 0)
            return;
        CreateTextResources();

        nint module = Native.GetModuleHandleW(null);
        fixed (char* name = "MxStudioRose")
        {
            var windowClass = new Native.WindowClass
            {
                Size = (uint)sizeof(Native.WindowClass),
                WindowProc = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WindowProc,
                Instance = module,
                ClassName = (nint)name,
            };
            Native.RegisterClassExW(&windowClass);
            nint hwnd = Native.CreateWindowExW(
                Native.ExLayered | Native.ExTransparent | Native.ExTopmost | Native.ExToolWindow | Native.ExNoActivate,
                name, name, Native.Popup, 0, 0, 1, 1, 0, 0, module, 0);
            lock (_lock)
            {
                _hwnd = hwnd;
                _posted = true;
            }
            if (hwnd == 0)
                return;
        }
        Native.PostMessageW(_hwnd, WmApp, 0, 0);
        while (Native.GetMessageW(out var message, 0, 0, 0) > 0)
            Native.DispatchMessageW(&message);
    }

    [UnmanagedCallersOnly]
    private static nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if ((message == WmApp || (message == WmTimer && wParam == FadeTimer)) && s_overlay is { } overlay)
        {
            try
            {
                overlay.Render();
            }
            catch
            {
                // An exception can't cross into Windows; the next view draws again.
            }
            return 0;
        }
        return Native.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void Render()
    {
        RoseView? view;
        lock (_lock)
        {
            view = _pending;
            _posted = false;
        }
        if (view is null)
        {
            if (_visible)
            {
                Native.KillTimer(_hwnd, FadeTimer);
                Native.ShowWindow(_hwnd, Native.Hidden);
                _visible = false;
            }
            return;
        }
        if (!_visible)
            Place();

        Gdip.GdipGraphicsClear(_graphics, 0);
        Draw(view);

        long elapsed = Environment.TickCount64 - _shownAt;
        byte alpha = (byte)Math.Clamp(elapsed * 255 / FadeInMs, 0, 255);
        if (alpha < 255)
            Native.SetTimer(_hwnd, FadeTimer, 10, 0);
        else
            Native.KillTimer(_hwnd, FadeTimer);

        nint screen = Native.GetDC(0);
        var position = new Native.Point(_x, _y);
        var size = new Native.Size(_size, _size);
        var origin = new Native.Point(0, 0);
        var blend = new Native.Blend { Operation = 0, ConstantAlpha = alpha, AlphaFormat = 1 };
        Native.UpdateLayeredWindow(_hwnd, screen, &position, &size, _memoryDc, &origin, 0, &blend, Native.UpdateAlpha);
        Native.ReleaseDC(0, screen);

        if (!_visible)
        {
            Native.ShowWindow(_hwnd, Native.ShowNoActivate);
            Native.SetWindowPos(_hwnd, Native.Topmost, 0, 0, 0, 0, Native.NoMove | Native.NoSize | Native.NoActivate);
            _visible = true;
        }
    }

    /// <summary>Centres the rose on the pointer, kept on its monitor, and sizes it for that monitor's DPI.</summary>
    private void Place()
    {
        Native.GetCursorPos(out var cursor);
        nint monitor = Native.MonitorFromPoint(cursor, Native.MonitorNearest);
        float scale = Native.GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 ? dpi / 96f : 1;
        var info = new Native.MonitorInfo { Size = (uint)sizeof(Native.MonitorInfo) };
        Native.GetMonitorInfoW(monitor, &info);
        int size = (int)MathF.Ceiling(SizeDip * scale);
        if (size != _size || scale != _scale)
        {
            _scale = scale;
            Allocate(size);
        }
        var area = info.Monitor;
        _x = Math.Clamp(cursor.X - size / 2, area.Left, Math.Max(area.Left, area.Right - size));
        _y = Math.Clamp(cursor.Y - size / 2, area.Top, Math.Max(area.Top, area.Bottom - size));
        _shownAt = Environment.TickCount64;
    }

    private void Allocate(int size)
    {
        if (_graphics != 0)
            Gdip.GdipDeleteGraphics(_graphics);
        if (_bitmap != 0)
            Gdip.GdipDisposeImage(_bitmap);
        if (_dib != 0)
            Native.DeleteObject(_dib);
        if (_memoryDc == 0)
            _memoryDc = Native.CreateCompatibleDC(0);

        var header = new Native.BitmapInfoHeader
        {
            Size = (uint)sizeof(Native.BitmapInfoHeader),
            Width = size,
            // Negative for rows top to bottom, the order GDI+ writes them in.
            Height = -size,
            Planes = 1,
            BitCount = 32,
        };
        nint bits;
        _dib = Native.CreateDIBSection(_memoryDc, &header, 0, &bits, 0, 0);
        _bits = bits;
        Native.SelectObject(_memoryDc, _dib);
        Gdip.GdipCreateBitmapFromScan0(size, size, size * 4, Gdip.PixelFormat32bppPArgb, (byte*)bits, out _bitmap);
        Gdip.GdipGetImageGraphicsContext(_bitmap, out _graphics);
        Gdip.GdipSetSmoothingMode(_graphics, Gdip.SmoothingAntiAlias);
        Gdip.GdipSetPixelOffsetMode(_graphics, Gdip.PixelOffsetHighQuality);
        Gdip.GdipSetTextRenderingHint(_graphics, Gdip.TextAntiAliasGridFit);
        _size = size;
        RecreateFonts();
    }

    private void CreateTextResources()
    {
        Gdip.GdipCreateStringFormat(0, 0, out _format);
        Gdip.GdipSetStringFormatAlign(_format, 1);
        Gdip.GdipSetStringFormatLineAlign(_format, 1);
    }

    private void RecreateFonts()
    {
        if (_font != 0)
            Gdip.GdipDeleteFont(_font);
        if (_smallFont != 0)
            Gdip.GdipDeleteFont(_smallFont);
        nint family;
        if (Gdip.GdipCreateFontFamilyFromName("Segoe UI Semibold", 0, out family) != 0)
            Gdip.GdipCreateFontFamilyFromName("Segoe UI", 0, out family);
        Gdip.GdipCreateFont(family, 12.5f * _scale, 0, Gdip.UnitPixel, out _font);
        Gdip.GdipCreateFont(family, 9.5f * _scale, 0, Gdip.UnitPixel, out _smallFont);
        Gdip.GdipDeleteFontFamily(family);
    }

    private void Draw(RoseView view)
    {
        float s = _scale, c = _size / 2f, outer = RadiusDip * s;
        float inner = MathF.Max(outer * (float)view.CenterRatio, 44 * s);
        int count = view.Slots.Count;
        float width = 360f / count;

        // A soft shadow under the glass, so the rose reads on light and dark windows alike.
        for (int i = 0; i < 4; i++)
        {
            float spread = (4 - i) * 3 * s;
            Fill(Argb(14, 0, 0, 0), path => Gdip.GdipAddPathEllipse(path, c - outer - spread, c - outer - spread + 2 * s,
                2 * (outer + spread), 2 * (outer + spread)));
        }
        Fill(Argb(226, 22, 23, 28), path => Gdip.GdipAddPathEllipse(path, c - outer, c - outer, 2 * outer, 2 * outer));

        if (view.Selected is { } selected && selected < count)
        {
            float start = -90 - width / 2 + selected * width;
            bool empty = view.Slots[selected].Empty;
            Fill(empty ? Argb(22, 255, 255, 255) : Argb(46, 255, 255, 255), path => Sector(path, c, inner, outer, start, width));
            if (!empty)
            {
                float rim = outer - 2.5f * s;
                Stroke(Argb(235, 255, 255, 255), 3.5f * s,
                    path => Gdip.GdipAddPathArc(path, c - rim, c - rim, 2 * rim, 2 * rim, start + 1.5f, width - 3));
            }
        }

        for (int i = 0; i < count; i++)
        {
            double angle = (-90 - width / 2 + i * width) * Math.PI / 180;
            float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
            Line(Argb(52, 255, 255, 255), 1 * s, c + cos * (inner + 3 * s), c + sin * (inner + 3 * s), c + cos * (outer - 2 * s), c + sin * (outer - 2 * s));
        }
        Stroke(Argb(120, 255, 255, 255), 1.5f * s, path => Gdip.GdipAddPathEllipse(path, c - outer, c - outer, 2 * outer, 2 * outer));

        // The middle: letting go here runs nothing, so it lights up while the ghost is in it.
        bool home = view.Selected is null;
        Fill(home ? Argb(70, 255, 255, 255) : Argb(110, 10, 10, 14), path => Gdip.GdipAddPathEllipse(path, c - inner, c - inner, 2 * inner, 2 * inner));
        Stroke(Argb(home ? 200 : 110, 255, 255, 255), 1.5f * s, path => Gdip.GdipAddPathEllipse(path, c - inner, c - inner, 2 * inner, 2 * inner));
        float mark = 14 * s, cross = 5 * s, markY = view.SecondLayer ? c - 6 * s : c;
        Stroke(Argb(home ? 255 : 170, 255, 255, 255), 1.5f * s, path => Gdip.GdipAddPathEllipse(path, c - mark, markY - mark, 2 * mark, 2 * mark));
        Line(Argb(home ? 255 : 170, 255, 255, 255), 1.6f * s, c - cross, markY - cross, c + cross, markY + cross);
        Line(Argb(home ? 255 : 170, 255, 255, 255), 1.6f * s, c + cross, markY - cross, c - cross, markY + cross);
        if (view.SecondLayer)
            Text("LAYER 2", _smallFont, Argb(230, 120, 190, 255), c - inner, markY + mark + 1 * s, 2 * inner, 14 * s);

        float ring = inner + (outer - inner) * 0.54f;
        float chord = MathF.Min(118 * s, 2 * ring * MathF.Sin(MathF.PI / count) - 10 * s);
        for (int i = 0; i < count; i++)
        {
            var slot = view.Slots[i];
            double angle = (-90 + i * width) * Math.PI / 180;
            float x = c + (float)Math.Cos(angle) * ring, y = c + (float)Math.Sin(angle) * ring;
            bool lit = view.Selected == i;
            int alpha = slot.Empty ? 80 : slot.Inherited && !lit ? 170 : lit ? 255 : 225;
            var icon = slot.IconPath is { } data ? Icon(data) : null;
            string label = slot.Empty && slot.Label.Length == 0 ? "—" : slot.Label.ToUpperInvariant();
            float iconSize = 26 * s, labelHeight = 32 * s;
            if (icon is not null)
            {
                Text(label, _font, Argb(alpha, 255, 255, 255), x - chord / 2, y - labelHeight - 2 * s, chord, labelHeight, bottom: true);
                Fill(Argb(alpha, 255, 255, 255), path => AddIcon(path, icon, x - iconSize / 2, y + 2 * s, iconSize), winding: true);
            }
            else
                Text(label, _font, Argb(alpha, 255, 255, 255), x - chord / 2, y - labelHeight / 2, chord, labelHeight);
            if (slot.Inherited)
            {
                float dot = 2.5f * s, dotY = icon is not null ? y + iconSize + 9 * s : y + labelHeight / 2 + 4 * s;
                Fill(Argb(alpha, 120, 190, 255), path => Gdip.GdipAddPathEllipse(path, x - dot, dotY - dot, 2 * dot, 2 * dot));
            }
        }

        DrawGhost(c + (float)view.GhostX * (outer - 6 * s), c + (float)view.GhostY * (outer - 6 * s), s);
    }

    /// <summary>A translucent arrow where the pointer would be, since the real one stays still while the rose is open.</summary>
    private void DrawGhost(float x, float y, float s)
    {
        float[] arrow = Arrow;
        float k = 1.05f * s;
        void Shape(nint path)
        {
            Gdip.GdipStartPathFigure(path);
            for (int i = 2; i < arrow.Length; i += 2)
                Gdip.GdipAddPathLine(path, x + arrow[i - 2] * k, y + arrow[i - 1] * k, x + arrow[i] * k, y + arrow[i + 1] * k);
            Gdip.GdipClosePathFigure(path);
        }
        Fill(Argb(215, 255, 255, 255), Shape);
        Stroke(Argb(150, 0, 0, 0), 1.1f * s, Shape);
    }

    private static readonly float[] Arrow = [0, 0, 0, 17, 4.2f, 13, 7.2f, 19.5f, 10, 18.3f, 7.1f, 12, 12.5f, 12];

    private IReadOnlyList<PathFigure>? Icon(string data)
    {
        if (!_icons.TryGetValue(data, out var figures))
        {
            if (_icons.Count > 64)
                _icons.Clear();
            figures = SvgPath.TryParse(data, out var parsed) ? parsed : null;
            _icons[data] = figures;
        }
        return figures;
    }

    private static void AddIcon(nint path, IReadOnlyList<PathFigure> figures, float left, float top, float size)
    {
        float k = size / 256;
        foreach (var figure in figures)
        {
            Gdip.GdipStartPathFigure(path);
            var from = figure.Start;
            foreach (var segment in figure.Segments)
            {
                Gdip.GdipAddPathBezier(path,
                    left + (float)from.X * k, top + (float)from.Y * k,
                    left + (float)segment.Control1.X * k, top + (float)segment.Control1.Y * k,
                    left + (float)segment.Control2.X * k, top + (float)segment.Control2.Y * k,
                    left + (float)segment.End.X * k, top + (float)segment.End.Y * k);
                from = segment.End;
            }
            if (figure.Closed)
                Gdip.GdipClosePathFigure(path);
        }
    }

    private static void Sector(nint path, float c, float inner, float outer, float start, float sweep)
    {
        Gdip.GdipAddPathArc(path, c - outer, c - outer, 2 * outer, 2 * outer, start, sweep);
        Gdip.GdipAddPathArc(path, c - inner, c - inner, 2 * inner, 2 * inner, start + sweep, -sweep);
        Gdip.GdipClosePathFigure(path);
    }

    private void Fill(uint color, Action<nint> shape, bool winding = false)
    {
        Gdip.GdipCreatePath(winding ? 1 : 0, out var path);
        shape(path);
        Gdip.GdipCreateSolidFill(color, out var brush);
        Gdip.GdipFillPath(_graphics, brush, path);
        Gdip.GdipDeleteBrush(brush);
        Gdip.GdipDeletePath(path);
    }

    private void Stroke(uint color, float width, Action<nint> shape)
    {
        Gdip.GdipCreatePath(0, out var path);
        shape(path);
        Gdip.GdipCreatePen1(color, width, Gdip.UnitWorld, out var pen);
        Gdip.GdipDrawPath(_graphics, pen, path);
        Gdip.GdipDeletePen(pen);
        Gdip.GdipDeletePath(path);
    }

    private void Line(uint color, float width, float x1, float y1, float x2, float y2)
    {
        Gdip.GdipCreatePen1(color, width, Gdip.UnitWorld, out var pen);
        Gdip.GdipDrawLine(_graphics, pen, x1, y1, x2, y2);
        Gdip.GdipDeletePen(pen);
    }

    private void Text(string text, nint font, uint color, float x, float y, float width, float height, bool bottom = false)
    {
        if (text.Length == 0)
            return;
        Gdip.GdipSetStringFormatLineAlign(_format, bottom ? 2 : 1);
        Gdip.GdipCreateSolidFill(color, out var brush);
        var rect = new Gdip.RectF(x, y, width, height);
        Gdip.GdipDrawString(_graphics, text, text.Length, font, &rect, _format, brush);
        Gdip.GdipDeleteBrush(brush);
    }

    private static uint Argb(int a, int r, int g, int b) => (uint)(a << 24 | r << 16 | g << 8 | b);

    private static partial class Native
    {
        public const uint ExLayered = 0x00080000, ExTransparent = 0x00000020, ExTopmost = 0x00000008;
        public const uint ExToolWindow = 0x00000080, ExNoActivate = 0x08000000;
        public const uint Popup = 0x80000000;
        public const int Hidden = 0, ShowNoActivate = 4;
        public const uint NoSize = 0x1, NoMove = 0x2, NoActivate = 0x10;
        public const uint UpdateAlpha = 2;
        public const uint MonitorNearest = 2;
        public static readonly nint Topmost = -1;
        public static readonly nint PerMonitorAwareV2 = -4;

        [StructLayout(LayoutKind.Sequential)]
        public struct WindowClass
        {
            public uint Size, Style;
            public nint WindowProc;
            public int ClassExtra, WindowExtra;
            public nint Instance, Icon, Cursor, Background, MenuName, ClassName, SmallIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        public record struct Point(int X, int Y);

        [StructLayout(LayoutKind.Sequential)]
        public record struct Size(int Width, int Height);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MonitorInfo
        {
            public uint Size;
            public Rect Monitor, Work;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Blend
        {
            public byte Operation, Flags, ConstantAlpha, AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapInfoHeader
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, SizeImage;
            public int XPelsPerMeter, YPelsPerMeter;
            public uint ColorsUsed, ColorsImportant;
        }

        [LibraryImport("user32.dll")] public static partial nint SetThreadDpiAwarenessContext(nint context);
        [LibraryImport("user32.dll")] public static partial ushort RegisterClassExW(WindowClass* windowClass);
        [LibraryImport("user32.dll")]
        public static partial nint CreateWindowExW(uint exStyle, char* className, char* name, uint style, int x, int y, int width, int height,
            nint parent, nint menu, nint instance, nint param);
        [LibraryImport("user32.dll")] public static partial nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);
        [LibraryImport("user32.dll")] public static partial int GetMessageW(out WindowsKeyCapture.Msg message, nint hwnd, uint min, uint max);
        [LibraryImport("user32.dll")] public static partial nint DispatchMessageW(WindowsKeyCapture.Msg* message);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool ShowWindow(nint hwnd, int command);
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool UpdateLayeredWindow(nint hwnd, nint screen, Point* position, Size* size, nint source, Point* origin,
            uint key, Blend* blend, uint flags);
        [LibraryImport("user32.dll")] public static partial nint SetTimer(nint hwnd, nint id, uint ms, nint callback);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool KillTimer(nint hwnd, nint id);
        [LibraryImport("user32.dll")] public static partial nint GetDC(nint hwnd);
        [LibraryImport("user32.dll")] public static partial int ReleaseDC(nint hwnd, nint dc);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetCursorPos(out Point point);
        [LibraryImport("user32.dll")] public static partial nint MonitorFromPoint(Point point, uint flags);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetMonitorInfoW(nint monitor, MonitorInfo* info);
        [LibraryImport("shcore.dll")] public static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial nint GetModuleHandleW(string? name);
        [LibraryImport("gdi32.dll")] public static partial nint CreateCompatibleDC(nint dc);
        [LibraryImport("gdi32.dll")] public static partial nint CreateDIBSection(nint dc, BitmapInfoHeader* info, uint usage, nint* bits, nint section, uint offset);
        [LibraryImport("gdi32.dll")] public static partial nint SelectObject(nint dc, nint item);
        [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DeleteObject(nint item);
    }

    private static partial class Gdip
    {
        private const string Library = "gdiplus.dll";
        public const int PixelFormat32bppPArgb = 0x000E200B;
        public const int SmoothingAntiAlias = 4;
        public const int PixelOffsetHighQuality = 2;
        public const int TextAntiAliasGridFit = 3;
        public const int UnitWorld = 0;
        public const int UnitPixel = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct StartupInput
        {
            public uint Version;
            public nint DebugEventCallback;
            public int SuppressBackgroundThread, SuppressExternalCodecs;
        }

        [StructLayout(LayoutKind.Sequential)]
        public record struct RectF(float X, float Y, float Width, float Height);

        [LibraryImport(Library)] public static partial int GdiplusStartup(out nuint token, StartupInput* input, nint output);
        [LibraryImport(Library)] public static partial int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, byte* scan0, out nint bitmap);
        [LibraryImport(Library)] public static partial int GdipGetImageGraphicsContext(nint image, out nint graphics);
        [LibraryImport(Library)] public static partial int GdipDisposeImage(nint image);
        [LibraryImport(Library)] public static partial int GdipDeleteGraphics(nint graphics);
        [LibraryImport(Library)] public static partial int GdipSetSmoothingMode(nint graphics, int mode);
        [LibraryImport(Library)] public static partial int GdipSetPixelOffsetMode(nint graphics, int mode);
        [LibraryImport(Library)] public static partial int GdipSetTextRenderingHint(nint graphics, int hint);
        [LibraryImport(Library)] public static partial int GdipGraphicsClear(nint graphics, uint color);
        [LibraryImport(Library)] public static partial int GdipCreateSolidFill(uint color, out nint brush);
        [LibraryImport(Library)] public static partial int GdipDeleteBrush(nint brush);
        [LibraryImport(Library)] public static partial int GdipCreatePen1(uint color, float width, int unit, out nint pen);
        [LibraryImport(Library)] public static partial int GdipDeletePen(nint pen);
        [LibraryImport(Library)] public static partial int GdipCreatePath(int fillMode, out nint path);
        [LibraryImport(Library)] public static partial int GdipDeletePath(nint path);
        [LibraryImport(Library)] public static partial int GdipStartPathFigure(nint path);
        [LibraryImport(Library)] public static partial int GdipClosePathFigure(nint path);
        [LibraryImport(Library)] public static partial int GdipAddPathLine(nint path, float x1, float y1, float x2, float y2);
        [LibraryImport(Library)] public static partial int GdipAddPathArc(nint path, float x, float y, float width, float height, float start, float sweep);
        [LibraryImport(Library)] public static partial int GdipAddPathEllipse(nint path, float x, float y, float width, float height);
        [LibraryImport(Library)]
        public static partial int GdipAddPathBezier(nint path, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4);
        [LibraryImport(Library)] public static partial int GdipFillPath(nint graphics, nint brush, nint path);
        [LibraryImport(Library)] public static partial int GdipDrawPath(nint graphics, nint pen, nint path);
        [LibraryImport(Library)] public static partial int GdipDrawLine(nint graphics, nint pen, float x1, float y1, float x2, float y2);
        [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf16)]
        public static partial int GdipCreateFontFamilyFromName(string name, nint collection, out nint family);
        [LibraryImport(Library)] public static partial int GdipDeleteFontFamily(nint family);
        [LibraryImport(Library)] public static partial int GdipCreateFont(nint family, float size, int style, int unit, out nint font);
        [LibraryImport(Library)] public static partial int GdipDeleteFont(nint font);
        [LibraryImport(Library)] public static partial int GdipCreateStringFormat(int flags, ushort language, out nint format);
        [LibraryImport(Library)] public static partial int GdipSetStringFormatAlign(nint format, int align);
        [LibraryImport(Library)] public static partial int GdipSetStringFormatLineAlign(nint format, int align);
        [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf16)]
        public static partial int GdipDrawString(nint graphics, string text, int length, nint font, RectF* layout, nint format, nint brush);
    }
}
