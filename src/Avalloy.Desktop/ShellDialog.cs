using System;
using Avalonia.Threading;
using Avalloy.Services;

namespace Avalloy;

/// <summary>
/// A <see cref="ShellWindow"/> for modal / child windows shown via
/// <c>ShowDialog</c> (previews, dialogs). It keeps the shared chrome (extended
/// client area + thick macOS titlebar) but fixes the activation cascade a modal
/// child needs:
/// <list type="bullet">
/// <item>It does <b>not</b> promote the whole app to frontmost on open
/// (<see cref="ShellWindow.ActivateAppOnOpen"/> is false): the app is already
/// active, and re-activating it re-keys the parent — leaving this window non-key
/// with dimmed macOS traffic lights.</item>
/// <item>Instead it makes <i>itself</i> key once AppKit has wired it into the
/// window list, via <see cref="MacAppActivation.MakeWindowKey"/> on the
/// dispatcher tick after <c>Opened</c> (an immediate call can no-op because the
/// modal child isn't in AppKit's window list yet).</item>
/// </list>
/// Top-level windows (a main window, a non-modal settings window) should derive
/// from <see cref="ShellWindow"/> directly so they still app-activate on open.
/// </summary>
public class ShellDialog : ShellWindow
{
    protected override bool ActivateAppOnOpen => false;

    public ShellDialog()
    {
        // Two-stage activation cascade-recovery:
        //   1. Avalonia's Window.Activate() (≈ orderFront) immediately on Opened.
        //   2. Native [NSWindow makeKeyAndOrderFront:nil] on the next dispatcher
        //      tick — covers the case where the modal child opened via ShowDialog
        //      hasn't yet been wired into AppKit's window list when Opened fires,
        //      so an immediate makeKey would silently no-op. No-op off macOS.
        Opened += (_, _) => MakeKey();
    }

    /// <summary>Run the two-stage macOS key-window cascade (Avalonia
    /// <c>Activate()</c> now + native <c>makeKeyAndOrderFront:</c> on the next
    /// dispatcher tick). Fired automatically on <c>Opened</c>, but exposed so a
    /// <b>reused</b> window — one that's hidden and re-shown rather than recreated,
    /// like the shared preview window — can re-key on each present. Without it the
    /// re-shown window comes up non-key: the parent keeps focus (its traffic lights
    /// dim) and keystrokes meant for this window go to the parent. No-op off macOS.</summary>
    public void MakeKey()
    {
        Activate();
        Dispatcher.UIThread.Post(
            () => MacAppActivation.MakeWindowKey(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero),
            DispatcherPriority.Loaded);
    }
}
