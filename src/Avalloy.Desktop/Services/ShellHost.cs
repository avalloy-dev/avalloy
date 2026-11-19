using Avalonia.Controls;

namespace Avalloy.Services;

/// <summary>
/// Holds a reference to the application's main <see cref="Window"/> so that
/// services without their own window handle (dialog service, native shell
/// callbacks, …) can find a modal parent. Populated by
/// <see cref="AvalloyApp"/> right after the main window is created.
/// </summary>
public sealed class ShellHost
{
    /// <summary>The current main window, or null before initialization
    /// completes. Setter is intended for the framework; consumers read only.</summary>
    public Window? MainWindow { get; set; }
}
