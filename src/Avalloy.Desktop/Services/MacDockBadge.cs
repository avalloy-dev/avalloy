using System.Runtime.InteropServices;

namespace Avalloy.Services;

/// <summary>
/// macOS-only: set or clear the badge label on the app's Dock tile
/// (<c>[[NSApp dockTile] setBadgeLabel:]</c>) — the small red pill apps use to
/// surface a pending-items count while the window is in the background. Pass
/// <c>null</c>/empty to clear. No-op off macOS. Call on the UI (main) thread.
/// </summary>
public static class MacDockBadge
{
    private const string Libobjc = "/usr/lib/libobjc.dylib";

    [DllImport(Libobjc, CharSet = CharSet.Ansi)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(Libobjc, CharSet = CharSet.Ansi)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr msg(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr msg_ptr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern void msg_void_ptr(IntPtr receiver, IntPtr selector, IntPtr arg);

    private static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static void Set(string? label)
    {
        if (!IsSupported) return;
        IntPtr utf8 = IntPtr.Zero;
        try
        {
            try { NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit"); } catch { }

            var app = msg(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            if (app == IntPtr.Zero) return;
            var tile = msg(app, sel_registerName("dockTile"));
            if (tile == IntPtr.Zero) return;

            IntPtr nsLabel = IntPtr.Zero; // nil clears the badge
            if (!string.IsNullOrEmpty(label))
            {
                utf8 = Marshal.StringToCoTaskMemUTF8(label);
                nsLabel = msg_ptr(objc_getClass("NSString"),
                    sel_registerName("stringWithUTF8String:"), utf8);
            }
            msg_void_ptr(tile, sel_registerName("setBadgeLabel:"), nsLabel);
        }
        catch { /* best-effort cosmetic */ }
        finally
        {
            if (utf8 != IntPtr.Zero) Marshal.FreeCoTaskMem(utf8);
        }
    }
}
