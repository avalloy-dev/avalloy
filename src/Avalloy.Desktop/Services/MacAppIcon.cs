using System.Runtime.InteropServices;

namespace Avalloy.Services;

/// <summary>
/// macOS-only: set the running process's Dock / Cmd-Tab / menu-bar icon at
/// startup. Bridges the gap that <c>dotnet run</c> / unbundled launches don't
/// fill — a published <c>.app</c> with an <c>.icns</c> resource is the proper
/// long-term path, but this lets the icon show up during dev iteration too.
/// </summary>
/// <remarks>
/// <para>
/// Sets <c>[NSApplication sharedApplication].applicationIconImage</c> via
/// objc_msgSend. The change sticks for the lifetime of the process and has no
/// effect on the binary at rest.
/// </para>
/// <para>
/// PNG bytes are handed to NSImage via <c>+[NSImage alloc] initWithData:</c>.
/// AppKit downsamples on its own for the Dock's various contexts; supplying a
/// single 1024px PNG is good enough for dev. If you later want crisp 1x/2x for
/// every Dock magnification step, build an NSImage with multiple
/// representations (one per common size) instead.
/// </para>
/// </remarks>
public static class MacAppIcon
{
    private const string Libobjc = "/usr/lib/libobjc.dylib";

    [DllImport(Libobjc, CharSet = CharSet.Ansi)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(Libobjc, CharSet = CharSet.Ansi)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr msg_get(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr msg_alloc(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr msg_initWithBytes(
        IntPtr receiver, IntPtr selector, IntPtr bytes, ulong length);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr msg_initWithData(
        IntPtr receiver, IntPtr selector, IntPtr data);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern void msg_setObject(
        IntPtr receiver, IntPtr selector, IntPtr arg);

    public static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>
    /// Set the application Dock icon from PNG bytes. No-op off macOS or if
    /// AppKit can't be loaded. Safe to call multiple times.
    /// </summary>
    public static void SetFromPng(byte[] pngBytes)
    {
        if (!IsSupported) return;
        if (pngBytes is null || pngBytes.Length == 0) return;
        if (!EnsureInit()) return;

        IntPtr nsDataBytes = IntPtr.Zero;
        try
        {
            // [NSData alloc] initWithBytes:length:
            // (initWithBytes:length: copies the buffer, so the GC-pinned span
            // doesn't need to outlive this call.)
            nsDataBytes = Marshal.AllocHGlobal(pngBytes.Length);
            Marshal.Copy(pngBytes, 0, nsDataBytes, pngBytes.Length);

            var nsDataAlloc = msg_alloc(s_classNSData, s_selAlloc);
            if (nsDataAlloc == IntPtr.Zero) return;
            var nsData = msg_initWithBytes(nsDataAlloc, s_selInitWithBytesLength,
                                           nsDataBytes, (ulong)pngBytes.Length);
            if (nsData == IntPtr.Zero) return;

            var nsImageAlloc = msg_alloc(s_classNSImage, s_selAlloc);
            if (nsImageAlloc == IntPtr.Zero) return;
            var nsImage = msg_initWithData(nsImageAlloc, s_selInitWithData, nsData);
            if (nsImage == IntPtr.Zero) return;

            var nsApp = msg_get(s_classNSApplication, s_selSharedApplication);
            if (nsApp == IntPtr.Zero) return;
            msg_setObject(nsApp, s_selSetApplicationIconImage, nsImage);
        }
        catch { /* best-effort */ }
        finally
        {
            if (nsDataBytes != IntPtr.Zero) Marshal.FreeHGlobal(nsDataBytes);
        }
    }

    /// <summary>
    /// Convenience: load the PNG bytes from an avares://Alloy/... URI
    /// and apply. The URI must point to an AvaloniaResource (build-included).
    /// </summary>
    public static void SetFromAvaloniaResource(string avaresUri)
    {
        if (!IsSupported) return;
        try
        {
            var uri = new Uri(avaresUri);
            using var stream = Avalonia.Platform.AssetLoader.Open(uri);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            SetFromPng(ms.ToArray());
        }
        catch { /* best-effort */ }
    }

    private static IntPtr s_classNSApplication;
    private static IntPtr s_classNSImage;
    private static IntPtr s_classNSData;
    private static IntPtr s_selSharedApplication;
    private static IntPtr s_selAlloc;
    private static IntPtr s_selInitWithBytesLength;
    private static IntPtr s_selInitWithData;
    private static IntPtr s_selSetApplicationIconImage;
    private static int s_initState; // 0=untried, 1=ready, 2=failed
    private static readonly object s_initLock = new();

    private static bool EnsureInit()
    {
        var state = Volatile.Read(ref s_initState);
        if (state == 1) return true;
        if (state == 2) return false;

        lock (s_initLock)
        {
            if (s_initState != 0) return s_initState == 1;
            try
            {
                try { NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit"); } catch { }
                try { NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation"); } catch { }
                s_classNSApplication = objc_getClass("NSApplication");
                s_classNSImage = objc_getClass("NSImage");
                s_classNSData = objc_getClass("NSData");
                s_selSharedApplication = sel_registerName("sharedApplication");
                s_selAlloc = sel_registerName("alloc");
                s_selInitWithBytesLength = sel_registerName("initWithBytes:length:");
                s_selInitWithData = sel_registerName("initWithData:");
                s_selSetApplicationIconImage = sel_registerName("setApplicationIconImage:");
                var ok = s_classNSApplication != IntPtr.Zero
                         && s_classNSImage != IntPtr.Zero
                         && s_classNSData != IntPtr.Zero
                         && s_selSharedApplication != IntPtr.Zero
                         && s_selAlloc != IntPtr.Zero
                         && s_selInitWithBytesLength != IntPtr.Zero
                         && s_selInitWithData != IntPtr.Zero
                         && s_selSetApplicationIconImage != IntPtr.Zero;
                s_initState = ok ? 1 : 2;
                return ok;
            }
            catch
            {
                s_initState = 2;
                return false;
            }
        }
    }
}
