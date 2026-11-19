using System.Runtime.InteropServices;

namespace Avalloy.Services;

/// <summary>
/// macOS-only: bring Alloy (and individual windows) to the foreground.
/// Companion to <see cref="MacWebViewFirstMouse"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Window.Activate()</c> in Avalonia approximately maps to AppKit's
/// <c>orderFront:</c> for the window but doesn't reliably make it key or
/// promote the owning <c>NSApplication</c> to frontmost. This service exposes
/// two helpers that close those gaps via direct objc_msgSend:
/// </para>
/// <list type="bullet">
///   <item><see cref="Activate"/> — promotes the app to Regular policy and
///   asks AppKit to make it frontmost. macOS 14+ silently no-ops the call
///   when the OS doesn't see a user-gesture activation context (e.g.
///   terminal-launched apps), so this is best-effort; the deterministic fix
///   for that case is to launch via a `.app` bundle.</item>
///   <item><see cref="MakeWindowKey"/> — direct
///   <c>[NSWindow makeKeyAndOrderFront:]</c>. Required for modal child
///   windows opened via <c>ShowDialog</c>: Avalonia's surface doesn't drive
///   key-window cascade for those reliably, so we call the native API
///   ourselves on the dispatcher tick after the window is wired in.</item>
/// </list>
/// </remarks>
public static class MacAppActivation
{
    private const string Libobjc = "/usr/lib/libobjc.dylib";

    [DllImport(Libobjc, CharSet = CharSet.Ansi)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(Libobjc, CharSet = CharSet.Ansi)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr msg_get(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern void msg_activate(IntPtr receiver, IntPtr selector, byte ignoringOtherApps);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern byte msg_setActivationPolicy(IntPtr receiver, IntPtr selector, long policy);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern void msg_void(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern void msg_makeKey(IntPtr receiver, IntPtr selector, IntPtr sender);

    // NSApplicationActivationPolicyRegular — full GUI app: menu bar, dock icon,
    // can become frontmost. Set defensively in case the launching environment
    // left us classified as accessory/prohibited.
    private const long ActivationPolicyRegular = 0;

    private static IntPtr s_classNSApplication;
    private static IntPtr s_selSharedApplication;
    private static IntPtr s_selActivateIgnoringOtherApps;
    private static IntPtr s_selSetActivationPolicy;
    private static IntPtr s_selActivate;
    private static IntPtr s_selMakeKeyAndOrderFront;
    private static IntPtr s_selHide;
    private static int s_initState; // 0=untried, 1=ready, 2=failed
    private static readonly object s_initLock = new();

    public static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>Bring Alloy to the foreground. No-op everywhere except macOS.</summary>
    public static void Activate()
    {
        if (!IsSupported) return;
        if (!EnsureInit()) return;
        try
        {
            var app = msg_get(s_classNSApplication, s_selSharedApplication);
            if (app == IntPtr.Zero) return;
            msg_setActivationPolicy(app, s_selSetActivationPolicy, ActivationPolicyRegular);
            // Call both the legacy and the macOS-14+ activate APIs — the OS
            // chooses which one actually applies based on version + context.
            msg_activate(app, s_selActivateIgnoringOtherApps, 1);
            msg_void(app, s_selActivate);
        }
        catch { /* best-effort */ }
    }

    /// <summary>Force the given NSWindow to become key and order-front.
    /// Used to recover from the cascade case where opening a modal child via
    /// <c>ShowDialog</c> leaves the child window non-key with dimmed
    /// stoplights even after the parent app is active.</summary>
    public static void MakeWindowKey(IntPtr nsWindow)
    {
        if (!IsSupported || nsWindow == IntPtr.Zero) return;
        if (!EnsureInit()) return;
        try
        {
            msg_makeKey(nsWindow, s_selMakeKeyAndOrderFront, IntPtr.Zero);
        }
        catch { /* best-effort */ }
    }

    /// <summary>Hide the whole application (equivalent to ⌘H / <c>[NSApp hide:]</c>).
    /// Unlike closing or minimising a window, an app hidden this way is restored
    /// natively by AppKit when the user clicks its Dock icon or ⌘-tabs back — no
    /// reopen handler required. Used for close-to-background on macOS. No-op
    /// elsewhere.</summary>
    public static void Hide()
    {
        if (!IsSupported || !EnsureInit()) return;
        try
        {
            var app = msg_get(s_classNSApplication, s_selSharedApplication);
            if (app != IntPtr.Zero) msg_makeKey(app, s_selHide, IntPtr.Zero); // [NSApp hide:nil]
        }
        catch { /* best-effort */ }
    }

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
                s_classNSApplication = objc_getClass("NSApplication");
                s_selSharedApplication = sel_registerName("sharedApplication");
                s_selActivateIgnoringOtherApps = sel_registerName("activateIgnoringOtherApps:");
                s_selSetActivationPolicy = sel_registerName("setActivationPolicy:");
                s_selActivate = sel_registerName("activate");
                s_selMakeKeyAndOrderFront = sel_registerName("makeKeyAndOrderFront:");
                s_selHide = sel_registerName("hide:");
                var ok = s_classNSApplication != IntPtr.Zero
                         && s_selSharedApplication != IntPtr.Zero
                         && s_selActivateIgnoringOtherApps != IntPtr.Zero
                         && s_selSetActivationPolicy != IntPtr.Zero
                         && s_selActivate != IntPtr.Zero
                         && s_selMakeKeyAndOrderFront != IntPtr.Zero
                         && s_selHide != IntPtr.Zero;
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
