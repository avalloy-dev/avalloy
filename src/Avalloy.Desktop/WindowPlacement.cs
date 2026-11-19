using System;
using Avalonia;
using Avalonia.Controls;

namespace Avalloy;

/// <summary>
/// Places a top-level window centered over another window <em>without parenting
/// it</em>. Parenting (<see cref="Window.Show(Window)"/>) is undesirable for
/// independent utility windows on macOS — an owned window never becomes the key
/// window, so its traffic lights stay gray and it reads as inactive. These
/// helpers give the "opens over the window I'm working in" placement of
/// <see cref="WindowStartupLocation.CenterOwner"/> while keeping the window an
/// unparented top-level.
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// Position <paramref name="window"/> centered over <paramref name="owner"/>
    /// and switch it to <see cref="WindowStartupLocation.Manual"/> so the OS
    /// doesn't re-center it on the primary display. Call right before
    /// <see cref="Window.Show()"/>; no-op when <paramref name="owner"/> is null or
    /// not yet laid out.
    /// <para>
    /// The geometry is done in screen coordinates via <see cref="Visual.PointToScreen"/>
    /// rather than hand-applying <see cref="TopLevel.RenderScaling"/>. The two
    /// disagree: macOS window/screen coordinates are logical points (scale factor
    /// 1 regardless of a Retina backing scale), while Windows uses physical
    /// pixels. <c>PointToScreen</c> bakes in the right per-platform conversion, so
    /// the same code centers correctly on both. Centers over the owner's client
    /// area (close enough to frame-center; titlebar offset is negligible).
    /// </para>
    /// <para>
    /// Deliberately does NOT clamp to the owner's screen working area: when the
    /// owner sits near a display edge, clamping fights the centering and yanks the
    /// child to a corner. Centering over the main window is the goal, so a child
    /// that slightly overhangs a monitor edge is fine.
    /// </para>
    /// </summary>
    public static void CenterOver(this Window window, Window? owner)
    {
        if (owner is null) return;

        // Owner client size in DIP. Zero before the owner is laid out — bail
        // rather than divide by zero deriving the scale factor below.
        var clientSize = owner.Bounds.Size;
        if (clientSize.Width <= 0 || clientSize.Height <= 0) return;

        // Owner client rectangle in screen coordinates. PointToScreen applies the
        // platform's real DIP->screen conversion (points on macOS, physical px on
        // Windows), so the derived factor is correct everywhere.
        var topLeft = owner.PointToScreen(new Point(0, 0));
        var bottomRight = owner.PointToScreen(new Point(clientSize.Width, clientSize.Height));
        double ownerW = bottomRight.X - topLeft.X;
        double ownerH = bottomRight.Y - topLeft.Y;
        var factor = ownerW / clientSize.Width; // screen units per DIP

        // New window's size in the same screen units. Width/Height are DIP and are
        // set from XAML (and, for geometry-bound windows, restored before this
        // runs); guard NaN so an unsized window just pins to the owner's top-left.
        var newW = (double.IsNaN(window.Width) ? 0 : window.Width) * factor;
        var newH = (double.IsNaN(window.Height) ? 0 : window.Height) * factor;

        window.Position = CenteredPosition(topLeft, ownerW, ownerH, newW, newH, workingArea: null);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
    }

    /// <summary>
    /// Pure geometry behind <see cref="CenterOver"/>: the top-left (physical px)
    /// that centers a <paramref name="newWidth"/>×<paramref name="newHeight"/>
    /// window over an owner at <paramref name="ownerPos"/> sized
    /// <paramref name="ownerWidth"/>×<paramref name="ownerHeight"/>, clamped into
    /// <paramref name="workingArea"/> when supplied. All sizes are physical
    /// pixels. Exposed for testing.
    /// </summary>
    public static PixelPoint CenteredPosition(
        PixelPoint ownerPos,
        double ownerWidth, double ownerHeight,
        double newWidth, double newHeight,
        PixelRect? workingArea)
    {
        var x = ownerPos.X + (int)((ownerWidth - newWidth) / 2);
        var y = ownerPos.Y + (int)((ownerHeight - newHeight) / 2);

        if (workingArea is { } wa)
        {
            x = Math.Clamp(x, wa.X, Math.Max(wa.X, wa.Right - (int)newWidth));
            y = Math.Clamp(y, wa.Y, Math.Max(wa.Y, wa.Bottom - (int)newHeight));
        }

        return new PixelPoint(x, y);
    }
}
