namespace Avalloy.Controls.Sidebar;

/// <summary>
/// The seam between the sidebar FRAMEWORK (<see cref="SidebarDockController"/> — reserved-column
/// dock + edge pull; <see cref="SidebarPeekBehavior"/> — hover-peek choreography) and the app's
/// shell state. The framework owns the mechanics and the feel; the host owns the state and its
/// persistence. An app's shell view-model typically implements this directly.
/// </summary>
public interface ISidebarHost
{
    /// <summary>The pin latch — pinned docks the sidebar into the layout (reflows, resizable);
    /// unpinned leaves the narrow rail (+ optional hover peek overlay).</summary>
    bool Pinned { get; }

    /// <summary>Expanded (peeked OR pinned). Apps without a peek overlay can return
    /// <see cref="Pinned"/>.</summary>
    bool Open { get; }

    /// <summary>Toggle the pin latch (edge-pull tap, ⌘B). The host flips its state and persists;
    /// it then tells the dock controller via <see cref="SidebarDockController.NotifyPinChanged"/>
    /// (usually from its property-changed watcher) so the reserved column morphs.</summary>
    void TogglePin();

    /// <summary>Hover dwelled on the collapsed surface — open the peek overlay. No-op for apps
    /// without one.</summary>
    void PeekOpen();

    /// <summary>Pointer left / user clicked through — retract the peek (never the pin).</summary>
    void PeekClose();

    /// <summary>Gate for the edge pull's seam reveal — false while something owns the pointer story
    /// (a modal overlay, the peek itself). Return true when in doubt.</summary>
    bool EdgePullAllowed { get; }

    /// <summary>The persisted docked width. The setter is called (already clamped) after a
    /// deliberate resize — persist it debounced; the framework never spams it.</summary>
    double SavedDockedWidth { get; set; }
}
