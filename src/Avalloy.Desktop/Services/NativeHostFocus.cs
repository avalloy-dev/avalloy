using System.Runtime.InteropServices;

namespace Avalloy.Services;

/// <summary>
/// Reclaim native keyboard focus from an embedded native control (a WebView2 /
/// WKWebView child) back to the hosting Avalonia top-level.
/// </summary>
/// <remarks>
/// Native web views own a child window of their own (an HWND on Windows, an
/// NSView on macOS). When that child holds OS keyboard focus, calling
/// <c>Focus()</c> on an Avalonia control only moves Avalonia's <em>logical</em>
/// focus — the caret appears, but the OS still routes keystrokes to the native
/// child, so typing does nothing until the user clicks the Avalonia control
/// (which moves OS focus for real). This helper performs that OS-level focus
/// hand-back programmatically so e.g. Cmd/Ctrl+F can land in a host search box.
/// </remarks>
public static class NativeHostFocus
{
    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    /// <summary>
    /// Move native keyboard focus to the given Avalonia top-level so its
    /// currently-focused element actually receives keys. Best-effort: no-op on
    /// unsupported platforms or a null handle.
    /// </summary>
    /// <param name="topLevelHandle">The platform handle from
    /// <c>TopLevel.TryGetPlatformHandle()</c> — the window HWND on Windows, the
    /// content NSView on macOS.</param>
    public static void Reclaim(IntPtr topLevelHandle)
    {
        if (topLevelHandle == IntPtr.Zero) return;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Win32 focus follows the HWND tree; the web view is a child of
                // the window HWND, so SetFocus on the window reclaims input.
                SetFocus(topLevelHandle);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // The Avalonia top-level handle is the content NSView; making it
                // its window's first responder pulls focus off the WKWebView.
                MacWebViewFirstMouse.MakeFirstResponder(topLevelHandle);
            }
        }
        catch { /* best-effort focus polish — never throw into the UI thread */ }
    }
}
