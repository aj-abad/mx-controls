using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MxControls.Platform.MacOS;

/// <summary>
/// An app's icon as Finder shows it, from NSWorkspace, as a PNG. NSWorkspace reads asset catalogs too, which apps
/// without an .icns file keep their icon in.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class MacIcons
{
    private const string Objc = "/usr/lib/libobjc.A.dylib";
    private const double Points = 48;
    private const nuint PngFileType = 4; // NSBitmapImageFileTypePNG

    // Loaded for its classes; NSWorkspace and NSBitmapImageRep live in AppKit.
    private static readonly nint AppKit = NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");

    [LibraryImport(Objc, StringMarshalling = StringMarshalling.Utf8)] private static partial nint objc_getClass(string name);
    [LibraryImport(Objc, StringMarshalling = StringMarshalling.Utf8)] private static partial nint sel_registerName(string name);
    [LibraryImport(Objc)] private static partial nint objc_autoreleasePoolPush();
    [LibraryImport(Objc)] private static partial void objc_autoreleasePoolPop(nint pool);

    // objc_msgSend takes whatever the method does, so each shape it's called with gets its own declaration.
    [LibraryImport(Objc, EntryPoint = "objc_msgSend")] private static partial nint Send(nint receiver, nint selector);
    [LibraryImport(Objc, EntryPoint = "objc_msgSend")] private static partial nint Send(nint receiver, nint selector, nint a);
    [LibraryImport(Objc, EntryPoint = "objc_msgSend")] private static partial nint Send(nint receiver, nint selector, nint a, nint b, nint c);
    [LibraryImport(Objc, EntryPoint = "objc_msgSend")] private static partial nint Send(nint receiver, nint selector, nuint a, nint b);
    [LibraryImport(Objc, EntryPoint = "objc_msgSend")] private static partial void SendSize(nint receiver, nint selector, double width, double height);
    [LibraryImport(Objc, EntryPoint = "objc_msgSend")] private static partial nuint SendLength(nint receiver, nint selector);

    public static byte[]? Extract(string bundle)
    {
        _ = AppKit;
        nint pool = objc_autoreleasePoolPush();
        try
        {
            nint workspace = Send(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
            var utf8 = System.Text.Encoding.UTF8.GetBytes(bundle + "\0");
            nint path;
            fixed (byte* text = utf8)
                path = Send(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), (nint)text);
            nint image = Send(workspace, sel_registerName("iconForFile:"), path);
            if (image == 0)
                return null;
            SendSize(image, sel_registerName("setSize:"), Points, Points);
            nint cgImage = Send(image, sel_registerName("CGImageForProposedRect:context:hints:"), 0, 0, 0);
            if (cgImage == 0)
                return null;
            nint rep = Send(Send(objc_getClass("NSBitmapImageRep"), sel_registerName("alloc")), sel_registerName("initWithCGImage:"), cgImage);
            if (rep == 0)
                return null;
            try
            {
                nint options = Send(objc_getClass("NSDictionary"), sel_registerName("dictionary"));
                nint data = Send(rep, sel_registerName("representationUsingType:properties:"), PngFileType, options);
                if (data == 0)
                    return null;
                int length = (int)SendLength(data, sel_registerName("length"));
                nint bytes = Send(data, sel_registerName("bytes"));
                return bytes == 0 ? null : new ReadOnlySpan<byte>((void*)bytes, length).ToArray();
            }
            finally
            {
                Send(rep, sel_registerName("release"));
            }
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }
}
