using Avalonia;
using Avalonia.Controls;

namespace Avalloy.AttachedProperties;

/// <summary>
/// Attach to a <see cref="ScrollViewer"/> to keep it pinned to the bottom as its content grows — the standard
/// streaming-chat behaviour. It only follows when the user is already at the end: scroll up to read back and
/// it stops auto-following until you return to the bottom. Setting the offset re-enters with a zero extent
/// delta, so there is no feedback loop.
/// </summary>
public static class AutoScroll
{
    /// <summary>When true, the attached <see cref="ScrollViewer"/> sticks to the bottom on content growth.</summary>
    public static readonly AttachedProperty<bool> ToEndProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("ToEnd", typeof(AutoScroll));

    public static bool GetToEnd(ScrollViewer sv) => sv.GetValue(ToEndProperty);
    public static void SetToEnd(ScrollViewer sv, bool value) => sv.SetValue(ToEndProperty, value);

    static AutoScroll()
    {
        ToEndProperty.Changed.AddClassHandler<ScrollViewer>((sv, e) =>
        {
            if (e.NewValue is true)
                sv.ScrollChanged += OnScrollChanged;
            else
                sv.ScrollChanged -= OnScrollChanged;
        });
    }

    // Tolerance (px) for "at the bottom" — covers sub-pixel rounding and small layout jitter.
    private const double Epsilon = 8;

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv)
            return;

        // Only react to the content getting taller (a new line/block streamed in); ignore plain user scrolls
        // and our own corrective offset writes (which carry a zero extent delta).
        if (e.ExtentDelta.Y <= 0)
            return;

        // Reconstruct the pre-growth position: were we parked at the bottom before this content arrived?
        var prevOffset = sv.Offset.Y - e.OffsetDelta.Y;
        var prevExtent = sv.Extent.Height - e.ExtentDelta.Y;
        var wasAtBottom = prevOffset + sv.Viewport.Height >= prevExtent - Epsilon;

        if (wasAtBottom)
            sv.Offset = new Vector(sv.Offset.X, sv.Extent.Height - sv.Viewport.Height);
    }
}
