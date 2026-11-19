using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Avalloy.Services;

/// <summary>
/// macOS-only: make <c>WKWebView</c> accept the click that activates an
/// inactive window (the "first mouse") so users don't have to double-click
/// when refocusing a preview window from elsewhere.
/// </summary>
/// <remarks>
/// <para>
/// Default <c>NSView.acceptsFirstMouse:</c> returns <c>NO</c>, which means
/// AppKit consumes the activation click for window-key-status instead of
/// passing it to the WKWebView's hit-tested subview. Web content (Syncfusion
/// PdfViewer, EJ2 Spreadsheet, pdf.js, …) therefore appears to require a
/// second click to register.
/// </para>
/// <para>
/// Fix: add an <c>acceptsFirstMouse:</c> implementation that returns
/// <c>YES</c> to the <c>WKWebView</c> class via <c>class_addMethod</c>. Since
/// <c>WKWebView</c> doesn't override the method itself, <c>class_addMethod</c>
/// installs ours without trampling an existing implementation; every
/// WKWebView instance picks it up immediately. One-shot install for the
/// process lifetime — no per-instance hookup.
/// </para>
/// <para>
/// Pattern mirrors <see cref="MacCtrlTabMonitor"/>: lazy-load the WebKit
/// framework so <c>objc_getClass("WKWebView")</c> resolves even before
/// Avalonia's WebView package has created its first instance, and stash a
/// tri-state init flag so repeated calls are cheap and a permanent failure
/// stays sticky.
/// </para>
/// </remarks>
public static unsafe class MacWebViewFirstMouse
{
    private const string Libobjc = "/usr/lib/libobjc.dylib";

    [DllImport(Libobjc, CharSet = CharSet.Ansi)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(Libobjc, CharSet = CharSet.Ansi)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(Libobjc, CharSet = CharSet.Ansi)]
    private static extern bool class_addMethod(IntPtr cls, IntPtr name, IntPtr imp, [MarshalAs(UnmanagedType.LPStr)] string types);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr msg_get(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern byte msg_makeFirstResponder(IntPtr receiver, IntPtr selector, IntPtr arg);

    private static IntPtr s_selWindow;
    private static IntPtr s_selMakeFirstResponder;

    /// <summary>
    /// Replacement for <c>-[NSView acceptsFirstMouse:]</c>. The signature an
    /// Obj-C runtime calls is <c>BOOL(*)(id self, SEL _cmd, NSEvent *event)</c>;
    /// in C#, BOOL is a 1-byte signed char so we return <c>byte</c>.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte AcceptsFirstMouse(IntPtr self, IntPtr cmd, IntPtr nsEvent) => 1;

    // 0 = untried, 1 = installed, 2 = permanently failed.
    private static int s_initState;
    private static readonly object s_initLock = new();

    public static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>Diagnostic — last failure step if Install returned false.</summary>
    public static string? LastErrorStep { get; private set; }

    /// <summary>Install the override. Idempotent — only the first call does
    /// real work; subsequent calls return the cached outcome.</summary>
    public static bool Install()
    {
        if (!IsSupported) { LastErrorStep = "not macos"; return false; }
        var state = Volatile.Read(ref s_initState);
        if (state == 1) return true;
        if (state == 2) return false;

        lock (s_initLock)
        {
            if (s_initState != 0) return s_initState == 1;
            try
            {
                // WebKit may not be loaded yet if no Avalonia.Controls.WebView
                // has been constructed; force the framework load so
                // objc_getClass resolves WKWebView.
                try { NativeLibrary.Load("/System/Library/Frameworks/WebKit.framework/WebKit"); } catch { }

                var cls = objc_getClass("WKWebView");
                if (cls == IntPtr.Zero) { LastErrorStep = "WKWebView class not found"; s_initState = 2; return false; }
                var sel = sel_registerName("acceptsFirstMouse:");
                if (sel == IntPtr.Zero) { LastErrorStep = "acceptsFirstMouse: selector"; s_initState = 2; return false; }

                var imp = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte>)&AcceptsFirstMouse;
                // Type encoding: c@:@ → return BOOL (signed char), then the
                // standard self/_cmd/event Obj-C method preamble.
                var added = class_addMethod(cls, sel, imp, "c@:@");
                if (!added) LastErrorStep = "class_addMethod returned NO (already implemented?)";

                // Selectors used by MakeFirstResponder; cached once to avoid
                // re-registering on every preview-open.
                s_selWindow = sel_registerName("window");
                s_selMakeFirstResponder = sel_registerName("makeFirstResponder:");

                s_initState = added ? 1 : 2;
                return added;
            }
            catch (Exception ex)
            {
                LastErrorStep = $"init: {ex.GetType().Name}: {ex.Message}";
                s_initState = 2;
                return false;
            }
        }
    }

    /// <summary>
    /// Ask the NSView's hosting window to make it the first responder.
    /// Compensates for first-open click latency: on cold preview-open AppKit
    /// hasn't always cascaded firstResponder down into the WKWebView yet, so
    /// the very first click goes to focus traversal rather than hit-testing
    /// into the web content. Calling this after <c>NavigationCompleted</c>
    /// pre-warms the responder chain so the first user click registers.
    /// </summary>
    /// <param name="nsView">The native NSView handle from
    /// <c>NativeWebView.TryGetPlatformHandle()</c>.</param>
    public static void MakeFirstResponder(IntPtr nsView)
    {
        if (!IsSupported || nsView == IntPtr.Zero) return;
        if (!Install()) return; // also primes the cached selectors
        if (s_selWindow == IntPtr.Zero || s_selMakeFirstResponder == IntPtr.Zero) return;
        try
        {
            var window = msg_get(nsView, s_selWindow);
            if (window == IntPtr.Zero) return;
            msg_makeFirstResponder(window, s_selMakeFirstResponder, nsView);
        }
        catch { /* swallow — pre-warming is a best-effort polish */ }
    }
}
