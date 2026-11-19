using System.ComponentModel;
using Avalonia.Media;

namespace Avalloy.Controls.Tabs;

/// <summary>
/// One tab rendered by <see cref="TabStrip"/>. The control binds the per-tab
/// template purely against this contract, so any view model can back a tab —
/// a file manager's folder panes and an agent app's session documents implement
/// it without sharing a base class.
/// <para>
/// Implementers must raise <see cref="INotifyPropertyChanged.PropertyChanged"/>
/// for <see cref="DisplayName"/>, <see cref="Icon"/>, the pin/lock flags, and
/// the three <c>Show*Button</c> visibilities so the strip reacts live.
/// </para>
/// </summary>
public interface ITabItem : INotifyPropertyChanged
{
    /// <summary>Label shown on the tab.</summary>
    string DisplayName { get; }

    /// <summary>Optional leading icon (16×16). Null renders a reserved-but-empty
    /// slot so the row doesn't shift when a lazily-loaded bitmap arrives.</summary>
    IImage? Icon { get; }

    /// <summary>Live status, rendered by the strip as a status indicator that stays
    /// visible on background tabs. Orthogonal to <see cref="Icon"/>. Raise
    /// PropertyChanged for "Activity" so the indicator reacts live. Hosts that don't
    /// track status return <see cref="TabActivity.Idle"/> (and typically set
    /// <see cref="ITabHost.ShowActivity"/> false so the slot collapses).</summary>
    TabActivity Activity { get; }

    /// <summary>Pinned tabs cluster at the front of the strip and suppress the
    /// close button. Only meaningful when the host's
    /// <see cref="ITabHost.AllowPin"/> is true.</summary>
    bool IsPinned { get; }

    /// <summary>Locked tabs refuse in-place mutation. Only meaningful when the
    /// host's <see cref="ITabHost.AllowLock"/> is true.</summary>
    bool IsLocked { get; }

    /// <summary>When a locked tab permits in-place navigation into descendants of
    /// its locked root (instead of spawning a new tab). Only meaningful when
    /// <see cref="IsLocked"/> is true. Defaults to false so hosts that don't model
    /// the concept need not implement it; those that do should raise
    /// PropertyChanged for "BrowseSubfolders" so the strip's menu reacts live.</summary>
    bool BrowseSubfolders => false;

    /// <summary>Preview (provisional) tab, à la VSCode: rendered with an italic
    /// title to signal it's transient. The host owns the behavior — keeping a
    /// single preview slot and reusing it as the user browses — while the strip
    /// owns the look and the commit gestures (double-click the tab / "Keep open"),
    /// which call <see cref="ITabHost.KeepTab"/>. Raise PropertyChanged for
    /// "IsPreview" so the title switches between italic and upright live.</summary>
    bool IsPreview { get; }

    /// <summary>Whether the close (×) affordance shows. Typically
    /// <c>!IsPinned &amp;&amp; !IsLocked</c> and more than one tab open.</summary>
    bool ShowCloseButton { get; }

    /// <summary>Whether the unpin affordance shows in the trailing slot.</summary>
    bool ShowPinButton { get; }

    /// <summary>Whether the unlock affordance shows in the trailing slot.</summary>
    bool ShowLockButton { get; }

    /// <summary>Icon-pack URI for the pin glyph (resolved by the host app's
    /// icon pack). Null hides the glyph.</summary>
    string? PinIconUri { get; }

    /// <summary>Icon-pack URI for the lock glyph.</summary>
    string? LockIconUri { get; }

    /// <summary>Optional status glyph shown in the tab's badge slot, just left of the
    /// pin/lock/close cluster — a passive marker for a tab that something outside the
    /// strip has singled out (a file manager's sorter destination, a session bound to a
    /// running job). Null (the default) collapses the slot, so hosts that don't mark
    /// tabs implement nothing. Unlike the pin/lock glyphs this isn't a button: the
    /// state it reflects is owned elsewhere, so clicking it would have nowhere to go.
    /// Raise PropertyChanged for "BadgeIconUri" so the badge appears/disappears live.</summary>
    string? BadgeIconUri => null;

    /// <summary>Tooltip for the badge — what the mark means and, ideally, where to
    /// clear it. Null falls back to the tab's own <see cref="DisplayName"/> tooltip.</summary>
    string? BadgeToolTip => null;
}
