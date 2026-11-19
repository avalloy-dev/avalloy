using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Avalloy.AttachedProperties;

// Verbatim port of the implementation from AvaloniaUI/Avalonia#21119
// (jpgarza93's reply). The technique — attaching an empty NSToolbar to
// the NSWindow to trigger AppKit's unified taller-titlebar layout — was
// dead-on-arrival when the apphost was linked against macOS SDK 15.5
// (pre-Tahoe chrome). With the csproj's vtool target now declaring SDK
// 26.0 on the apphost, AppKit honors the trick.
public static class MacOSTitleBar
{
    // ── attached property ──────────────────────────────────

    public static readonly AttachedProperty<bool> IsThickProperty =
        AvaloniaProperty.RegisterAttached<Window, bool>(
            "IsThick", typeof(MacOSTitleBar), defaultValue: false);

    public static bool GetIsThick(Window window) => window.GetValue(IsThickProperty);
    public static void SetIsThick(Window window, bool value) => window.SetValue(IsThickProperty, value);

    static MacOSTitleBar()
    {
        IsThickProperty.Changed.AddClassHandler<Window>(OnIsThickChanged);
    }

    // ── per-window state ───────────────────────────────────
    // ConditionalWeakTable ensures state is garbage-collected alongside the
    // Window instance, preventing leaks when windows are closed.

    private static readonly ConditionalWeakTable<Window, ThickTitleBarState> States = new();

    private static void OnIsThickChanged(Window window, AvaloniaPropertyChangedEventArgs args)
    {
        // no-op on non-macOS — safe to leave the property in cross-platform XAML
        if (!OperatingSystem.IsMacOS())
            return;

        if (args.NewValue is true)
        {
            var state = new ThickTitleBarState(window);
            States.AddOrUpdate(window, state);
            state.Attach();
        }
        else
        {
            if (States.TryGetValue(window, out var state))
            {
                state.Detach();
                States.Remove(window);
            }
        }
    }

    // ── per-window lifecycle ───────────────────────────────

    private sealed class ThickTitleBarState
    {
        private readonly Window _window;
        private IntPtr _nsWindow;
        private IntPtr _nsToolbar;
        private IntPtr _selSetToolbar;
        private IntPtr _selSetTitlebarAppearsTransparent;
        private IntPtr _selSetTitleVisibility;
        private bool _attached;

        public ThickTitleBarState(Window window) => _window = window;

        public void Attach()
        {
            if (_attached)
                return;

            // attached-property change fires before the native window exists;
            // Setup needs the NSWindow handle, so defer to Opened when needed.
            if (_window.IsLoaded)
                Setup();
            else
                _window.Opened += OnOpened;
        }

        public void Detach()
        {
            if (!_attached)
                return;

            _window.Opened -= OnOpened;
            _window.PropertyChanged -= OnWindowPropertyChanged;

            // remove the toolbar to restore the default thin title bar
            if (_nsWindow != IntPtr.Zero && _selSetToolbar != IntPtr.Zero)
                objc_msgSend_arg(_nsWindow, _selSetToolbar, IntPtr.Zero);

            _attached = false;
        }

        private void OnOpened(object? sender, EventArgs e)
        {
            _window.Opened -= OnOpened;
            Setup();
        }

        private void Setup()
        {
            try
            {
                // 1. get the native NSWindow handle from Avalonia's platform interop.
                _nsWindow = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (_nsWindow == IntPtr.Zero)
                    return;

                // 2. cache ObjC selectors used during setup and fullscreen transitions.
                _selSetToolbar = sel_registerName("setToolbar:");
                _selSetTitlebarAppearsTransparent = sel_registerName("setTitlebarAppearsTransparent:");
                _selSetTitleVisibility = sel_registerName("setTitleVisibility:");

                // 3. create an empty NSToolbar. assigning ANY toolbar to an NSWindow causes
                //    macOS to switch to the "unified toolbar" layout with a taller title bar.
                _nsToolbar = objc_msgSend_ret(
                    objc_getClass("NSToolbar"), sel_registerName("alloc"));

                _nsToolbar = objc_msgSend_arg_ret(
                    _nsToolbar,
                    sel_registerName("initWithIdentifier:"),
                    CreateNSString("avalloyTitleBar"));

                if (_nsToolbar == IntPtr.Zero)
                    return;

                // 4. hide the baseline separator line.
                objc_msgSend_bool(_nsToolbar, sel_registerName("setShowsBaselineSeparator:"), false);

                // 5. assign the toolbar — this is what triggers the height increase.
                objc_msgSend_arg(_nsWindow, _selSetToolbar, _nsToolbar);

                // 5a. force unifiedCompact (= 4). AppKit's default "automatic"
                //     resolves to the taller expanded layout on Tahoe; the
                //     compact variant is shorter — closer to a sane chrome
                //     height for an app titlebar. Values per NSWindowToolbarStyle:
                //     automatic=0, expanded=1, preference=2, unified=3, unifiedCompact=4.
                objc_msgSend_long(_nsWindow, sel_registerName("setToolbarStyle:"), 4);

                // 6. hide the window title text (NSWindowTitleHidden = 1).
                objc_msgSend_long(_nsWindow, _selSetTitleVisibility, 1);

                // 7. subscribe to WindowState changes for fullscreen transitions.
                _window.PropertyChanged += OnWindowPropertyChanged;

                _attached = true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("macOS thick title bar setup failed: {0}", ex.Message);
            }
        }

        private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != Window.WindowStateProperty)
                return;

            var newState = e.GetNewValue<WindowState>();

            // Background priority so this runs AFTER Avalonia's own fullscreen
            // handlers, which set titlebarAppearsTransparent = NO during transitions.
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (newState == WindowState.FullScreen)
                    {
                        // remove the toolbar entirely — leaving it attached renders
                        // an opaque gray bar across the top of the fullscreen space.
                        objc_msgSend_arg(_nsWindow, _selSetToolbar, IntPtr.Zero);
                        objc_msgSend_bool(_nsWindow, _selSetTitlebarAppearsTransparent, true);
                        objc_msgSend_long(_nsWindow, _selSetTitleVisibility, 1);
                    }
                    else
                    {
                        // re-attach the toolbar to restore the thick title bar.
                        objc_msgSend_arg(_nsWindow, _selSetToolbar, _nsToolbar);
                        objc_msgSend_bool(_nsWindow, _selSetTitlebarAppearsTransparent, true);
                        objc_msgSend_long(_nsWindow, _selSetTitleVisibility, 1);
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("macOS thick title bar fullscreen toggle failed: {0}", ex.Message);
                }
            }, DispatcherPriority.Background);
        }
    }

    // ── ObjC helpers ───────────────────────────────────────

    private static IntPtr CreateNSString(string str)
    {
        var nsString = objc_msgSend_ret(objc_getClass("NSString"), sel_registerName("alloc"));
        var utf8Ptr = Marshal.StringToCoTaskMemUTF8(str);
        try
        {
            return objc_msgSend_arg_ret(nsString, sel_registerName("initWithUTF8String:"), utf8Ptr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8Ptr);
        }
    }

    // ── P/Invoke: ObjC runtime ─────────────────────────────

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ret(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_arg(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_arg_ret(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_bool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool arg);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_long(IntPtr receiver, IntPtr selector, long arg);
}
