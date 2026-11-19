using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalloy.AttachedProperties;
using Avalloy.Commands;
using Avalloy.Services;
using Microsoft.Extensions.Logging;

namespace Avalloy;

/// <summary>
/// Base <see cref="Window"/> for Avalloy-built apps. Pre-wires:
/// <list type="bullet">
///   <item>Extended client area (so the window paints into the titlebar region).</item>
///   <item>macOS thick titlebar (taller, transparent, matches Tahoe chrome) via
///         <see cref="MacOSTitleBar"/>.</item>
///   <item>macOS frontmost activation on first open via
///         <see cref="MacAppActivation"/>.</item>
///   <item>A tunneled <see cref="Window.KeyDownEvent"/> handler that gives
///         subclasses first crack at the keystroke (modal routing) before
///         falling through to the injected <see cref="KeyboardDispatcher"/>
///         and finally to a subclass fallback hook.</item>
///   <item><see cref="ICommandSurface"/> default implementation that delegates
///         to the same dispatcher for toolbar and palette consumers.</item>
/// </list>
/// All of the macOS plumbing no-ops on other platforms, so subclasses are
/// cross-platform by default.
/// <para>
/// The keyboard dispatcher arrives via constructor injection — subclasses
/// expose a DI-friendly ctor that forwards it to <see cref="ShellWindow(KeyboardDispatcher?)"/>.
/// The parameterless constructor exists for the Avalonia XAML designer (which
/// instantiates types via reflection without DI); in that mode keyboard
/// dispatch is a no-op until the window is hosted in a running
/// <see cref="AvalloyApp"/>.
/// </para>
/// </summary>
public class ShellWindow : Window, ICommandSurface
{
    public static readonly AttachedProperty<bool> BypassGlobalKeyDispatchProperty =
        AvaloniaProperty.RegisterAttached<ShellWindow, InputElement, bool>(
            "BypassGlobalKeyDispatch");

    private readonly KeyboardDispatcher? _keyboard;

    /// <summary>Parameterless ctor for the Avalonia XAML designer only.
    /// Production code paths always resolve <see cref="ShellWindow"/> (or a
    /// subclass) through the DI container, which routes through the
    /// <see cref="ShellWindow(KeyboardDispatcher?)"/> overload.</summary>
    public ShellWindow() : this(keyboard: null) { }

    public ShellWindow(KeyboardDispatcher? keyboard)
    {
        _keyboard = keyboard;

        // Without this the OS chrome stays on top of any custom titlebar content
        // and the title bar has no overlap room for traffic lights.
        ExtendClientAreaToDecorationsHint = true;

        // Per-platform: on macOS, asks AppKit to use the taller unified-toolbar
        // titlebar layout. No-op everywhere else.
        MacOSTitleBar.SetIsThick(this, true);

        // Tunnel so modal shell state can claim keys before child controls; the
        // dispatch path below lets focused text inputs keep editing gestures.
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
    }

    public static bool GetBypassGlobalKeyDispatch(InputElement element) =>
        element.GetValue(BypassGlobalKeyDispatchProperty);

    public static void SetBypassGlobalKeyDispatch(InputElement element, bool value) =>
        element.SetValue(BypassGlobalKeyDispatchProperty, value);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // `dotnet run` from a terminal doesn't hand off LaunchServices' frontmost
        // gesture, so the window opens with dimmed stoplights and no keyboard
        // focus until the user clicks into it. This call promotes the process
        // to Regular activation policy and asks AppKit for frontmost.
        // No-op off macOS.
        if (ActivateAppOnOpen)
            MacAppActivation.Activate();
    }

    /// <summary>Whether to promote the whole app to frontmost when this window
    /// opens. True for top-level shell windows (the main window needs it when
    /// launched via <c>dotnet run</c>). <b>Modal child windows override to
    /// false</b>: the app is already active, and re-activating it re-keys the
    /// parent — leaving the child non-key with dimmed traffic lights. Such
    /// windows make themselves key directly (e.g. via
    /// <see cref="MacAppActivation.MakeWindowKey"/>) instead.</summary>
    protected virtual bool ActivateAppOnOpen => true;

    /// <summary><see cref="ICommandSurface"/> implementation — delegates to the
    /// injected <see cref="KeyboardDispatcher"/>. No-op when the dispatcher is
    /// absent (designer mode).</summary>
    public Task RunCommandAsync(string commandName) =>
        _keyboard?.RunByNameAsync(commandName) ?? Task.CompletedTask;

    /// <summary>
    /// Subclass hook for modal keystroke routing — runs BEFORE the keyboard
    /// dispatcher. Use this for preview-window-active Escape/Space, inline-rename
    /// Enter/Esc/Up/Down, or any other case where the active UI state should
    /// claim a key before commands run.
    /// <para>
    /// Return <c>true</c> when the override has consumed the key (the
    /// dispatcher is skipped). Set <c>e.Handled = true</c> inside the override
    /// if no further bubble handlers should fire either — returning true
    /// alone only suppresses dispatch, not propagation.
    /// </para>
    /// <para>Default: no-op, returns false.</para>
    /// </summary>
    protected virtual ValueTask<bool> OnPreviewKeyDownAsync(KeyEventArgs e) =>
        new ValueTask<bool>(false);

    /// <summary>Subclass hook for keys the dispatcher didn't claim. Type-to-
    /// search, focus-trap swallows, and similar fallbacks belong here. Default
    /// no-op.</summary>
    protected virtual void OnUnhandledKeyDown(KeyEventArgs e) { }

    /// <summary>Hook for exceptions thrown during preview routing, command
    /// resolution, or command execution. The default routes through
    /// <see cref="AlloyLogging"/> so the error reaches DevTools' Logs
    /// panel (and any production sink the host app wires up, e.g. Sentry);
    /// subclasses typically still override to also toast.</summary>
    protected virtual void OnKeyDispatchError(Exception ex) =>
        AlloyLogging.CreateLogger<ShellWindow>()
            .LogError(ex, "Shell keyboard dispatch failed");

    private async void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            // 1. Subclass modal routing wins over command dispatch. Subclass
            //    is responsible for setting e.Handled itself if it wants to
            //    swallow propagation.
            if (await OnPreviewKeyDownAsync(e)) return;

            if (FocusedBypassesGlobalKeyDispatch()) return;

            // 2. Text editors get first refusal. Only commands that explicitly
            //    opt in via CommandAttribute.AllowWhenTextInputFocused remain
            //    global while a text editor has focus.
            var textInputFocused = IsTextInputFocused();
            if (textInputFocused)
            {
                var textTask = _keyboard?.TryDispatch(e, textInputFocused: true);
                if (textTask is not null)
                {
                    e.Handled = true;
                    await textTask;
                }
                return;
            }

            // 3. Keyboard dispatcher. Mark Handled BEFORE awaiting — async-void
            //    handlers return control to the dispatcher on the first await,
            //    so the bubble phase would fire mid-suspend without this.
            var task = _keyboard?.TryDispatch(e);
            if (task is not null)
            {
                e.Handled = true;
                await task;
                return;
            }

            // 4. Subclass fallback — unbound keys that still want handling.
            OnUnhandledKeyDown(e);
        }
        catch (Exception ex)
        {
            // async void: an unhandled throw past here walks to abort(). Defer
            // to the subclass hook (typically toasts); always swallow.
            OnKeyDispatchError(ex);
        }
    }

    private bool IsTextInputFocused()
    {
        var focused = FocusManager?.GetFocusedElement();
        return focused is TextBox or AutoCompleteBox;
    }

    private bool FocusedBypassesGlobalKeyDispatch()
    {
        var focused = FocusManager?.GetFocusedElement();
        var current = focused as StyledElement;
        while (current is not null)
        {
            if (current is InputElement input && GetBypassGlobalKeyDispatch(input))
                return true;
            current = current.Parent as StyledElement;
        }
        return false;
    }
}
