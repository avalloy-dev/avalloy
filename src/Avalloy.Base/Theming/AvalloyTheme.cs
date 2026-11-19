namespace Avalloy.Theming;

/// <summary>
/// Engine-agnostic theme notification hub. Any theme engine — the shell's
/// <c>ThemeService</c> or the native <c>NativeThemeController</c> — calls
/// <see cref="NotifyApplied"/> after it finishes pushing a theme into the
/// Application's resources. Reusable controls (e.g.
/// <see cref="Controls.Tabs.TabStrip"/>) subscribe to <see cref="Applied"/> to
/// re-resolve engine-published tokens without taking a dependency on any specific
/// engine, so the same control works under either theming stack.
/// </summary>
public static class AvalloyTheme
{
    /// <summary>Raised at the end of every successful theme apply, on the UI thread.</summary>
    public static event EventHandler? Applied;

    /// <summary>Engines call this after applying a theme to fan the notification
    /// out to subscribed controls.</summary>
    public static void NotifyApplied() => Applied?.Invoke(null, EventArgs.Empty);
}
