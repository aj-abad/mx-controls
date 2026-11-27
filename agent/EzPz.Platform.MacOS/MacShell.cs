using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EzPz.Platform.MacOS;

/// <summary>
/// The AppKit side of the agent's own window: runs the application on the main thread, which AppKit requires, and
/// gives the webview window the look the app had under Tauri. The window itself comes from Photino; this patches
/// what Photino leaves out on macOS (transparency, a title bar over the content) and what it decides for a
/// one-window app (quitting with the last window), since here the agent outlives its window.
/// </summary>
[SupportedOSPlatform("macos")]
public static unsafe partial class MacShell
{
    private const string Objc = "/usr/lib/libobjc.A.dylib";
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";

    private const nint ActivationRegular = 0;
    private const nint ActivationAccessory = 1;
    private const nuint TerminateNow = 1;
    private const nuint TerminateLater = 2;
    private const nuint FullSizeContentView = 1 << 15;
    private const nint TitleHidden = 1;
    private const nint ZoomButton = 2;
    private const nint BelowWindow = -1; // NSWindowBelow
    private const nuint WidthHeightSizable = 2 | 16;
    private const nint SidebarMaterial = 7;
    private const nint BehindWindow = 0;
    private const nint StateActive = 1;

    private static readonly nint AppKit = NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
    private static readonly nint MainQueue = NativeLibrary.GetExport(NativeLibrary.Load(LibSystem), "_dispatch_main_q");
    private static readonly nint MsgSend = NativeLibrary.GetExport(NativeLibrary.Load(Objc), "objc_msgSend");

    [LibraryImport(Objc, StringMarshalling = StringMarshalling.Utf8)] private static partial nint objc_getClass(string name);
    [LibraryImport(Objc, StringMarshalling = StringMarshalling.Utf8)] private static partial nint sel_registerName(string name);
    [LibraryImport(Objc, StringMarshalling = StringMarshalling.Utf8)] private static partial byte class_addMethod(nint cls, nint sel, nint imp, string types);
    [LibraryImport(Objc, StringMarshalling = StringMarshalling.Utf8)] private static partial nint class_replaceMethod(nint cls, nint sel, nint imp, string types);
    [LibraryImport(Objc)] private static partial nint objc_autoreleasePoolPush();
    [LibraryImport(Objc)] private static partial void objc_autoreleasePoolPop(nint pool);
    [LibraryImport(LibSystem)] private static partial void dispatch_async_f(nint queue, nint context, delegate* unmanaged<nint, void> work);

    private static nint Sel(string name) => sel_registerName(name);
    private static nint Send(nint receiver, string selector) => ((delegate* unmanaged<nint, nint, nint>)MsgSend)(receiver, Sel(selector));
    private static nint Send(nint receiver, string selector, nint a) => ((delegate* unmanaged<nint, nint, nint, nint>)MsgSend)(receiver, Sel(selector), a);
    private static nint Send(nint receiver, string selector, nint a, nint b) =>
        ((delegate* unmanaged<nint, nint, nint, nint, nint>)MsgSend)(receiver, Sel(selector), a, b);
    private static nint Send(nint receiver, string selector, nint a, nint b, nint c) =>
        ((delegate* unmanaged<nint, nint, nint, nint, nint, nint>)MsgSend)(receiver, Sel(selector), a, b, c);
    private static void SendBool(nint receiver, string selector, bool value) =>
        ((delegate* unmanaged<nint, nint, byte, void>)MsgSend)(receiver, Sel(selector), value ? (byte)1 : (byte)0);
    private static bool GetBool(nint receiver, string selector, nint a) =>
        ((delegate* unmanaged<nint, nint, nint, byte>)MsgSend)(receiver, Sel(selector), a) != 0;
    private static nuint GetUInt(nint receiver, string selector) => ((delegate* unmanaged<nint, nint, nuint>)MsgSend)(receiver, Sel(selector));
    private static void SetUInt(nint receiver, string selector, nuint value) => ((delegate* unmanaged<nint, nint, nuint, void>)MsgSend)(receiver, Sel(selector), value);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect(double x, double y, double width, double height)
    {
        public double X = x, Y = y, Width = width, Height = height;
    }

    private static nint ClassOf(string name) => objc_getClass(name);
    private static nint New(string cls) => Send(Send(ClassOf(cls), "alloc"), "init");
    private static bool IsKind(nint obj, string cls) => ClassOf(cls) is not 0 and var c && GetBool(obj, "isKindOfClass:", c);

    private static nint NSString(string text)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(text + "\0");
        fixed (byte* p = utf8)
            return Send(ClassOf("NSString"), "stringWithUTF8String:", (nint)p);
    }

    private static Action? s_terminate;
    private static volatile bool s_stopped;

    /// <summary>
    /// Runs AppKit on this thread, which must be the process's main thread, until the process exits. The app starts
    /// with no Dock icon; it gets one while a window is open.
    /// </summary>
    /// <param name="delegateClass">
    /// The Objective-C class Photino's windows set as the application delegate, already loaded; it's patched so the
    /// last window closing doesn't end the process, and so a quit (Cmd-Q, logout) ends it through
    /// <paramref name="terminate"/>.
    /// </param>
    /// <param name="terminate">Stops the agent; it must call <see cref="Exit"/> once the agent has stopped.</param>
    public static void Run(string delegateClass, Action terminate)
    {
        _ = AppKit;
        s_terminate = terminate;
        nint app = Send(ClassOf("NSApplication"), "sharedApplication");
        Send(app, "setActivationPolicy:", ActivationAccessory);

        nint cls = ClassOf(delegateClass);
        if (cls == 0)
            throw new InvalidOperationException($"No Objective-C class {delegateClass}; is the webview library loaded?");
        class_replaceMethod(cls, Sel("applicationShouldTerminateAfterLastWindowClosed:"),
            (nint)(delegate* unmanaged<nint, nint, nint, byte>)&KeepRunning, "c@:@");
        class_replaceMethod(cls, Sel("applicationShouldTerminate:"),
            (nint)(delegate* unmanaged<nint, nint, nint, nuint>)&ShouldTerminate, "Q@:@");
        // Photino sets its own delegate of this class with each window; one now covers a quit before the first.
        Send(app, "setDelegate:", New(delegateClass));

        Send(app, "run");
    }

    [UnmanagedCallersOnly]
    private static byte KeepRunning(nint self, nint sel, nint app) => 0;

    [UnmanagedCallersOnly]
    private static nuint ShouldTerminate(nint self, nint sel, nint app)
    {
        if (s_stopped)
            return TerminateNow;
        // The agent hands the buttons back to the mouse before the process goes; Exit answers once it has.
        ThreadPool.QueueUserWorkItem(_ => s_terminate?.Invoke());
        return TerminateLater;
    }

    /// <summary>Ends the process from any thread, once the agent has stopped, through AppKit's own termination.</summary>
    public static void Exit()
    {
        s_stopped = true;
        OnMain(() =>
        {
            nint app = Send(ClassOf("NSApplication"), "sharedApplication");
            // Answers a quit that's waiting; with none waiting, starts one, which ShouldTerminate lets through.
            Send(app, "replyToApplicationShouldTerminate:", 1);
            Send(app, "terminate:", 0);
        });
    }

    /// <summary>Runs <paramref name="work"/> on the main thread, later.</summary>
    public static void OnMain(Action work)
    {
        var handle = GCHandle.Alloc(work);
        dispatch_async_f(MainQueue, GCHandle.ToIntPtr(handle), &RunWork);
    }

    [UnmanagedCallersOnly]
    private static void RunWork(nint context)
    {
        var handle = GCHandle.FromIntPtr(context);
        var work = (Action)handle.Target!;
        handle.Free();
        try
        {
            work();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Main-thread work failed: {error}");
        }
    }

    /// <summary>Shows or hides the Dock icon and the menu bar, which belong to an open window.</summary>
    public static void SetInDock(bool inDock)
    {
        nint app = Send(ClassOf("NSApplication"), "sharedApplication");
        Send(app, "setActivationPolicy:", inDock ? ActivationRegular : ActivationAccessory);
        if (inDock)
            SendBool(app, "activateIgnoringOtherApps:", true);
    }

    /// <summary>
    /// The open window that holds a webview, or 0. Photino doesn't hand out its window on macOS, and it's the only
    /// window with a webview in it.
    /// </summary>
    public static nint FindWebViewWindow()
    {
        nint windows = Send(Send(ClassOf("NSApplication"), "sharedApplication"), "windows");
        nuint count = GetUInt(windows, "count");
        for (nuint i = count; i-- > 0;)
        {
            nint window = Send(windows, "objectAtIndex:", (nint)i);
            if (WebViewIn(window) != 0)
                return window;
        }
        return 0;
    }

    private static nint WebViewIn(nint window)
    {
        nint content = Send(window, "contentView");
        if (content == 0)
            return 0;
        nint subviews = Send(content, "subviews");
        nuint count = GetUInt(subviews, "count");
        for (nuint i = 0; i < count; i++)
        {
            nint view = Send(subviews, "objectAtIndex:", (nint)i);
            if (IsKind(view, "WKWebView"))
                return view;
        }
        return 0;
    }

    /// <summary>
    /// Draws the window as the Tauri build did: the system's backdrop behind a see-through page, the traffic lights
    /// over the content with no title, and no zoom, since the layout has one size.
    /// </summary>
    public static void Style(nint window, double width, double height)
    {
        nint pool = objc_autoreleasePoolPush();
        try
        {
            SetUInt(window, "setStyleMask:", GetUInt(window, "styleMask") | FullSizeContentView);
            SendBool(window, "setTitlebarAppearsTransparent:", true);
            Send(window, "setTitleVisibility:", TitleHidden);
            SendBool(window, "setOpaque:", false);
            Send(window, "setBackgroundColor:", Send(ClassOf("NSColor"), "clearColor"));
            Send(Send(window, "standardWindowButton:", ZoomButton), "setEnabled:", 0);
            // The page is laid out for this size under the title bar, which is now part of the content.
            ((delegate* unmanaged<nint, nint, double, double, void>)MsgSend)(window, Sel("setContentSize:"), width, height);
            Send(window, "center");

            nint webView = WebViewIn(window);
            if (webView == 0)
                return;
            // WebKit's own switch for a see-through page; the one Tauri flips too.
            Send(webView, "setValue:forKey:", Send(ClassOf("NSNumber"), "numberWithBool:", 0), NSString("drawsBackground"));
            if (GetBool(webView, "respondsToSelector:", Sel("setUnderPageBackgroundColor:")))
                Send(webView, "setUnderPageBackgroundColor:", Send(ClassOf("NSColor"), "clearColor"));

            nint content = Send(window, "contentView");
            nint backdrop = Backdrop(Bounds(content));
            SetUInt(backdrop, "setAutoresizingMask:", WidthHeightSizable);
            Send(content, "addSubview:positioned:relativeTo:", backdrop, BelowWindow, webView);
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    /// <summary>Liquid glass where the system has it, as the Tauri build asked for first, else the sidebar material.</summary>
    private static nint Backdrop(Rect frame)
    {
        var initWithFrame = (delegate* unmanaged<nint, nint, Rect, nint>)MsgSend;
        if (ClassOf("NSGlassEffectView") is not 0 and var glass)
            return initWithFrame(Send(glass, "alloc"), Sel("initWithFrame:"), frame);
        nint effect = initWithFrame(Send(ClassOf("NSVisualEffectView"), "alloc"), Sel("initWithFrame:"), frame);
        Send(effect, "setMaterial:", SidebarMaterial);
        Send(effect, "setBlendingMode:", BehindWindow);
        Send(effect, "setState:", StateActive);
        return effect;
    }

    private static Rect Bounds(nint view)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return ((delegate* unmanaged<nint, nint, Rect>)MsgSend)(view, Sel("bounds"));
        // x86-64 returns a struct this size through memory, with objc_msgSend_stret.
        Rect rect;
        var stret = (delegate* unmanaged<Rect*, nint, nint, void>)NativeLibrary.GetExport(NativeLibrary.Load(Objc), "objc_msgSend_stret");
        stret(&rect, view, Sel("bounds"));
        return rect;
    }

    // An Objective-C block, laid out as the blocks ABI has it, for the one completion handler called from here.
    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public nint Isa;
        public int Flags;
        public int Reserved;
        public nint Invoke;
        public BlockDescriptor* Descriptor;
    }

    private const int BlockIsGlobal = 1 << 28;
    private static BlockLiteral* s_snapshotBlock;
    // Main thread only, like the snapshot itself.
    private static Action<byte[]?>? s_snapshotDone;

    /// <summary>
    /// Has the window's webview draw its page into an image and calls <paramref name="done"/> on the main thread
    /// with it as a PNG, or with null if there's no webview or a snapshot is already being taken. Main thread.
    /// </summary>
    public static void Snapshot(nint window, Action<byte[]?> done)
    {
        nint webView = window == 0 ? 0 : WebViewIn(window);
        if (webView == 0 || s_snapshotDone is not null)
        {
            done(null);
            return;
        }
        s_snapshotDone = done;
        // A null configuration snapshots the whole visible page.
        Send(webView, "takeSnapshotWithConfiguration:completionHandler:", 0, (nint)SnapshotBlock());
    }

    private static BlockLiteral* SnapshotBlock()
    {
        if (s_snapshotBlock is not null)
            return s_snapshotBlock;
        var descriptor = (BlockDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(BlockDescriptor));
        descriptor->Size = (nuint)sizeof(BlockLiteral);
        var block = (BlockLiteral*)NativeMemory.AllocZeroed((nuint)sizeof(BlockLiteral));
        // A global block has nothing captured, so the runtime never copies or frees it.
        block->Isa = NativeLibrary.GetExport(NativeLibrary.Load(LibSystem), "_NSConcreteGlobalBlock");
        block->Flags = BlockIsGlobal;
        block->Invoke = (nint)(delegate* unmanaged<BlockLiteral*, nint, nint, void>)&SnapshotTaken;
        block->Descriptor = descriptor;
        return s_snapshotBlock = block;
    }

    [UnmanagedCallersOnly]
    private static void SnapshotTaken(BlockLiteral* block, nint image, nint error)
    {
        var done = s_snapshotDone;
        s_snapshotDone = null;
        byte[]? png = null;
        try
        {
            png = image == 0 ? null : Png(image);
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine($"Encoding the snapshot failed: {failure.Message}");
        }
        done?.Invoke(png);
    }

    private static byte[]? Png(nint image)
    {
        nint pool = objc_autoreleasePoolPush();
        try
        {
            nint cgImage = Send(image, "CGImageForProposedRect:context:hints:", 0, 0, 0);
            if (cgImage == 0)
                return null;
            nint rep = Send(Send(ClassOf("NSBitmapImageRep"), "alloc"), "initWithCGImage:", cgImage);
            if (rep == 0)
                return null;
            try
            {
                const nint PngFileType = 4; // NSBitmapImageFileTypePNG
                nint data = Send(rep, "representationUsingType:properties:", PngFileType, Send(ClassOf("NSDictionary"), "dictionary"));
                if (data == 0)
                    return null;
                int length = (int)GetUInt(data, "length");
                nint bytes = Send(data, "bytes");
                return bytes == 0 ? null : new ReadOnlySpan<byte>((void*)bytes, length).ToArray();
            }
            finally
            {
                Send(rep, "release");
            }
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    /// <summary>Moves the window with the mouse, from a press the page saw where it has no control.</summary>
    public static void StartDrag(nint window)
    {
        nint app = Send(ClassOf("NSApplication"), "sharedApplication");
        nint e = Send(app, "currentEvent");
        if (e != 0)
            Send(window, "performWindowDragWithEvent:", e);
    }

    public static void Minimize(nint window) => Send(window, "miniaturize:", 0);

    public static void Focus(nint window)
    {
        SetInDock(true);
        Send(window, "makeKeyAndOrderFront:", 0);
    }

    /// <summary>
    /// Points the menu's Quit at the window instead, so Cmd-Q closes the window and the agent keeps the mouse;
    /// Photino builds that menu with each window.
    /// </summary>
    public static void QuitClosesWindow(string title)
    {
        nint menu = Send(Send(ClassOf("NSApplication"), "sharedApplication"), "mainMenu");
        if (menu == 0)
            return;
        nint terminate = Sel("terminate:");
        nint items = Send(menu, "itemArray");
        for (nuint i = 0; i < GetUInt(items, "count"); i++)
        {
            nint submenu = Send(Send(items, "objectAtIndex:", (nint)i), "submenu");
            if (submenu == 0)
                continue;
            nint subitems = Send(submenu, "itemArray");
            for (nuint j = 0; j < GetUInt(subitems, "count"); j++)
            {
                nint item = Send(subitems, "objectAtIndex:", (nint)j);
                if (Send(item, "action") != terminate)
                    continue;
                Send(item, "setAction:", Sel("performClose:"));
                Send(item, "setTitle:", NSString(title));
            }
        }
    }
}
