using System.Collections;
using System.ComponentModel;

namespace Avalloy.Controls.Tabs;

/// <summary>
/// The model a <see cref="TabStrip"/> drives: the live tab collection, which
/// tab is active, a set of capability flags that gate optional affordances, and
/// the operations the strip invokes from pointer / context-menu gestures.
/// <para>
/// <see cref="Tabs"/> is exposed as a bare <see cref="IEnumerable"/> so the host
/// can keep its own strongly-typed <c>ObservableCollection&lt;T&gt;</c>; back it
/// with a collection that raises
/// <see cref="System.Collections.Specialized.INotifyCollectionChanged"/> so the
/// strip's <c>ItemsControl</c> tracks add / remove / move. Raise
/// <see cref="INotifyPropertyChanged.PropertyChanged"/> for
/// <see cref="ActiveTab"/> (property name <c>"ActiveTab"</c>) when activation
/// changes so the strip re-stamps the active tab.
/// </para>
/// </summary>
public interface ITabHost : INotifyPropertyChanged
{
    /// <summary>Live tab list, ordered as rendered. Items implement
    /// <see cref="ITabItem"/>.</summary>
    IEnumerable Tabs { get; }

    /// <summary>The currently active tab, or null before the host hydrates.</summary>
    ITabItem? ActiveTab { get; }

    /// <summary>Number of tabs currently open.</summary>
    int TabCount { get; }

    /// <summary>Index of <paramref name="tab"/> in <see cref="Tabs"/>, or -1.</summary>
    int IndexOf(ITabItem tab);

    // -- Capability flags: the strip hides affordances a host doesn't support. --

    /// <summary>Show pin affordances + honor pinned clustering.</summary>
    bool AllowPin { get; }

    /// <summary>Show lock affordances.</summary>
    bool AllowLock { get; }

    /// <summary>Permit drag-to-reorder within this strip.</summary>
    bool AllowReorder { get; }

    /// <summary>Permit dragging a tab into a sibling strip sharing the same
    /// drag overlay (cross-host transfer). Both source and target hosts must
    /// allow it for a transfer to commit.</summary>
    bool AllowCrossHostTransfer { get; }

    /// <summary>Show the trailing "+" new-tab button.</summary>
    bool ShowNewTabButton { get; }

    /// <summary>Show the "Duplicate tab" context action. Defaults to true; hosts
    /// where duplicating is meaningless (e.g. one-tab-per-file editors) opt out.</summary>
    bool AllowDuplicate => true;

    /// <summary>Render per-tab <see cref="ITabItem.Activity"/> status indicators.
    /// When false the indicator slot collapses regardless of item state — for hosts
    /// (e.g. a plain file browser) with no status concept.</summary>
    bool ShowActivity { get; }

    // -- Operations invoked by the strip. --

    /// <summary>Make <paramref name="tab"/> the active tab (no-op for foreign tabs).</summary>
    void ActivateTab(ITabItem tab);

    /// <summary>Activate the next tab, wrapping to the first. Exposed so apps can
    /// bind their own next-tab shortcut (Ctrl+Tab etc.) to the host; the control
    /// itself stays input-policy-free.</summary>
    void ActivateNext();

    /// <summary>Activate the previous tab, wrapping to the last.</summary>
    void ActivatePrevious();

    /// <summary>Open a new tab and activate it.</summary>
    Task NewTabAsync();

    /// <summary>Close <paramref name="tab"/> (hosts may refuse, e.g. last/pinned).</summary>
    void CloseTab(ITabItem tab);

    /// <summary>Close every tab except <paramref name="keep"/> (and pinned ones).</summary>
    void CloseOthers(ITabItem keep);

    /// <summary>Close all tabs (hosts may keep pinned ones and/or one survivor when
    /// the host requires at least one tab).</summary>
    void CloseAll();

    /// <summary>Open a copy of <paramref name="tab"/> and activate it.</summary>
    Task DuplicateAsync(ITabItem tab);

    /// <summary>Promote a preview (<see cref="ITabItem.IsPreview"/>) tab to a
    /// permanent one. The strip calls this on a commit gesture — double-clicking
    /// the tab or the "Keep open" menu item. Hosts that don't use preview tabs
    /// can no-op. The complementary "open as preview / reuse the preview slot"
    /// flow lives in the host, since only it knows what opening means.</summary>
    void KeepTab(ITabItem tab);

    /// <summary>Toggle the pinned flag (and any host-side reclustering).</summary>
    void SetPinned(ITabItem tab, bool pinned);

    /// <summary>Toggle the locked flag.</summary>
    void SetLocked(ITabItem tab, bool locked);

    /// <summary>Toggle a locked tab's "browse subfolders in place" flag (see
    /// <see cref="ITabItem.BrowseSubfolders"/>). Defaults to a no-op for hosts
    /// that don't support it.</summary>
    void SetBrowseSubfolders(ITabItem tab, bool value) { }

    /// <summary>Reorder <paramref name="tab"/> to <paramref name="newIndex"/>
    /// within this host. Callers coalesce persistence to drop time, so this does
    /// not itself save.</summary>
    void MoveTab(ITabItem tab, int newIndex);

    /// <summary>Move <paramref name="tab"/> from this host into
    /// <paramref name="target"/> at <paramref name="targetIndex"/>.</summary>
    void TransferTab(ITabItem tab, ITabHost target, int targetIndex);

    /// <summary>Persist tab geometry after a drag completes. The strip calls this
    /// once on drop so reorders/transfers survive a restart.</summary>
    void RequestSave();
}
