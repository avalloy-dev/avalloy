using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalloy.Theming;

namespace Avalloy.Controls.Tabs;

/// <summary>
/// A reusable horizontal tab strip: drag-to-reorder (with a FLIP neighbor-slide
/// animation), cross-strip transfer between siblings sharing a <c>DragOverlay</c>
/// canvas, pin / lock / duplicate / close context actions, a "+" new-tab button,
/// and a themable active-tab indicator. Drives any <see cref="ITabHost"/>; the
/// per-tab template binds to <see cref="ITabItem"/>.
/// <para>
/// Optional affordances (pin / lock / reorder / cross-host transfer / new-tab)
/// are gated by the host's capability flags, so a simpler host gets a simpler
/// strip without forking the control.
/// </para>
/// <para>
/// Cross-strip drag needs a <see cref="Canvas"/> named <c>DragOverlay</c> in the
/// ancestor window (the floating ghost is parented there); siblings are
/// discovered by walking the window's visual tree for other <see cref="TabStrip"/>
/// instances.
/// </para>
/// </summary>
public partial class TabStrip : UserControl
{
    /// <summary>The model this strip renders. Assign the host view model (which
    /// implements <see cref="ITabHost"/>).</summary>
    public static readonly StyledProperty<ITabHost?> HostProperty =
        AvaloniaProperty.Register<TabStrip, ITabHost?>(nameof(Host));

    public ITabHost? Host
    {
        get => GetValue(HostProperty);
        set => SetValue(HostProperty, value);
    }

    private ITabHost? _host;

    /// <summary>Indicator-shape classes; exactly one is stamped on the strip from
    /// the resolved <see cref="TabStripTokens.ActiveIndicatorKey"/> string.</summary>
    private static readonly string[] AllIndicatorClasses =
    {
        "indicator-underline",
        "indicator-overline",
        "indicator-border",
        "indicator-dot",
        "indicator-none",
    };

    public TabStrip()
    {
        InitializeComponent();
        TabItems.ContainerPrepared += OnTabContainerPrepared;
        TabsScroller.ScrollChanged += (_, _) => UpdateOverflowButton();
        TabsScroller.SizeChanged += (_, _) => UpdateOverflowButton();
    }

    /// <summary>Show the overflow chevron only when the tab content is wider than
    /// the strip's viewport — i.e. some tabs are scrolled out of reach.</summary>
    private void UpdateOverflowButton()
    {
        OverflowButton.IsVisible =
            _host is not null && TabsScroller.Extent.Width > TabsScroller.Viewport.Width + 0.5;
    }

    /// <summary>Open a flyout listing every tab for direct selection — the reach-
    /// anything affordance when the strip overflows (pinned + off-screen tabs).</summary>
    private void OnOverflowClick(object? sender, RoutedEventArgs e)
    {
        if (_host is not { } host) return;
        var flyout = new MenuFlyout();
        foreach (var tab in host.Tabs.Cast<ITabItem>())
        {
            var mi = new MenuItem { Header = tab.DisplayName, Tag = tab };
            if (ReferenceEquals(tab, host.ActiveTab))
                mi.Icon = new TextBlock { Text = "●", VerticalAlignment = VerticalAlignment.Center };
            mi.Click += OnOverflowItemClick;
            flyout.Items.Add(mi);
        }
        flyout.ShowAt(OverflowButton);
    }

    private void OnOverflowItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: ITabItem tab } && _host is { } host)
            host.ActivateTab(tab); // ActiveTab change brings it into view via OnHostPropertyChanged
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AvalloyTheme.Applied += OnThemeApplied;
        ApplyIndicatorClass();
        ReconcileTabClasses();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AvalloyTheme.Applied -= OnThemeApplied;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnThemeApplied(object? sender, EventArgs e) => ApplyIndicatorClass();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HostProperty)
            HookUpHost(change.GetNewValue<ITabHost?>());
    }

    private void HookUpHost(ITabHost? host)
    {
        if (ReferenceEquals(_host, host)) { ReconcileTabClasses(); return; }
        if (_host is not null)
        {
            _host.PropertyChanged -= OnHostPropertyChanged;
            if (_host.Tabs is INotifyCollectionChanged oldcc)
                oldcc.CollectionChanged -= OnHostTabsChanged;
        }
        _host = host;
        TabItems.ItemsSource = host?.Tabs;
        NewTabButton.IsVisible = host?.ShowNewTabButton ?? false;
        // Collapse the activity slot entirely when the host doesn't track status.
        StripRoot.Classes.Set("activity-off", !(host?.ShowActivity ?? false));
        if (_host is not null)
        {
            _host.PropertyChanged += OnHostPropertyChanged;
            if (_host.Tabs is INotifyCollectionChanged newcc)
                newcc.CollectionChanged += OnHostTabsChanged;
        }
        ReconcileTabClasses();
        UpdateOverflowButton();
    }

    private void OnHostTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ReconcileTabClasses();
        // Tab count changed → the strip may have started/stopped overflowing.
        // Defer so the StackPanel has re-measured before we read its extent.
        Dispatcher.UIThread.Post(UpdateOverflowButton, DispatcherPriority.Loaded);
    }

    private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ITabHost.ActiveTab))
        {
            ReconcileTabClasses();
            BringActiveTabIntoView();
        }
    }

    // === Active-indicator + per-tab class stamping ===

    /// <summary>Read the resolved indicator string from app resources and stamp the
    /// matching <c>indicator-*</c> class on the strip. The per-shape style selectors
    /// pick it up to drive the active tab's underline / border / dot. Default
    /// "Underline".</summary>
    private void ApplyIndicatorClass()
    {
        var indicator = TabStripTokens.DefaultActiveIndicator;
        if (Application.Current?.Resources.TryGetValue(TabStripTokens.ActiveIndicatorKey, out var v) == true
            && v is string s && !string.IsNullOrWhiteSpace(s))
        {
            indicator = s;
        }
        var target = indicator switch
        {
            "Overline" => "indicator-overline",
            "Border" => "indicator-border",
            "Dot"    => "indicator-dot",
            "None"   => "indicator-none",
            _        => "indicator-underline",
        };
        foreach (var cls in AllIndicatorClasses)
        {
            if (cls == target)
            {
                if (!StripRoot.Classes.Contains(cls)) StripRoot.Classes.Add(cls);
            }
            else
            {
                StripRoot.Classes.Remove(cls);
            }
        }
    }

    private void OnTabContainerPrepared(object? sender, ContainerPreparedEventArgs e) =>
        SetTabClasses(e.Container);

    private void ReconcileTabClasses()
    {
        if (_host is null) return;
        for (int i = 0; i < TabItems.ItemCount; i++)
        {
            if (TabItems.ContainerFromIndex(i) is { } container)
                SetTabClasses(container);
        }
    }

    /// <summary>Stamp <c>active</c> on the host's current tab and <c>only-tab</c>
    /// when there's a single tab (used to suppress the close button).</summary>
    private void SetTabClasses(Control container)
    {
        if (_host is null) return;
        var isActive = container.DataContext is ITabItem item &&
                       ReferenceEquals(item, _host.ActiveTab);
        container.Classes.Set("active", isActive);
        container.Classes.Set("only-tab", _host.TabCount == 1);
    }

    /// <summary>Scroll the active tab into view if the strip's horizontal viewport
    /// clipped it (e.g. activated far-away via keyboard / context menu).</summary>
    private void BringActiveTabIntoView()
    {
        if (_host?.ActiveTab is not { } active) return;
        var idx = TabItems.Items is IList list ? list.IndexOf(active) : -1;
        if (idx < 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (TabItems.ContainerFromIndex(idx) is Control container)
                container.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private void OnNewTabClick(object? sender, RoutedEventArgs e)
    {
        if (_host is { } host) _ = host.NewTabAsync();
    }

    // === Drag-reorder state ===
    // We don't start dragging on press — the user might just be clicking to
    // activate. Track the candidate + press point, promote to a drag once the
    // pointer crosses the threshold in OnStripPointerMoved.
    private ITabItem? _dragCandidate;
    private Point _dragOrigin;       // pointer at press, in TabItems coords
    private Point _dragGrabOffset;   // grab point within the grabbed tab
    private bool _isDragging;
    private Border? _dragGhost;      // floating visual in the window's DragOverlay
    private double _dragSourceWidth; // width of the grabbed tab, sizes cross-strip gaps
    private int _dropGapIndex = -1;  // index a foreign drag's gap sits in front of (-1 = none)
    private double _dropGapWidth;
    private const double DragThresholdPx = 4;

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control c || c.DataContext is not ITabItem tab) return;
        if (_host is not { } host) return;

        var pt = e.GetCurrentPoint(c);
        // Double-click commits a preview tab (VSCode: double-click the tab keeps
        // it open). Activate too so the kept tab is the focused one.
        if (pt.Properties.IsLeftButtonPressed && e.ClickCount >= 2)
        {
            if (tab.IsPreview) host.KeepTab(tab);
            host.ActivateTab(tab);
            _dragCandidate = null;
            _isDragging = false;
            e.Handled = true;
            return;
        }
        // Middle-click closes (skip pinned/uncloseable — no close affordance, matching browsers).
        if (pt.Properties.IsMiddleButtonPressed && !tab.IsPinned && tab.ShowCloseButton)
        {
            host.CloseTab(tab);
            e.Handled = true;
            return;
        }
        // Activation is deferred to release-without-drag so grabbing a non-active
        // tab to drag elsewhere doesn't swap focus mid-gesture.
        if (pt.Properties.IsLeftButtonPressed)
        {
            _dragCandidate = tab;
            _dragOrigin = e.GetPosition(TabItems);
            _dragGrabOffset = e.GetPosition(c);
            _isDragging = false;
        }
        e.Handled = true;
    }

    private void OnStripPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCandidate is null || _host is not { } host) return;
        // Reorder/transfer disabled → still allow click-to-activate, no drag.
        if (!host.AllowReorder && !host.AllowCrossHostTransfer) return;
        var p = e.GetPosition(TabItems);
        if (!_isDragging)
        {
            if (Math.Abs(p.X - _dragOrigin.X) < DragThresholdPx) return;
            _isDragging = true;
            if (sender is IInputElement el) e.Pointer.Capture(el);
            SetDraggingClass(_dragCandidate, true);
            CreateDragGhost(_dragCandidate);
            CaptureDragSourceWidth();
            SetDragCursor(true);
        }
        UpdateDragGhostPosition(e);
        // Only tease sibling-strip drop gaps when this host can actually transfer out — otherwise a
        // reorder-only drag (e.g. file tabs over the terminal strip) shows a gap that can never commit.
        if (host.AllowCrossHostTransfer) UpdateCrossStripDropGap(e);

        // Live in-strip reorder; neighbors slide aside, the dragged tab's motion
        // is the floating ghost. Cross-strip handover stays drop-based.
        if (host.AllowReorder && PointerIsOverOwnStrip(e))
        {
            var target = FindDropIndex(p.X);
            if (target >= 0 && target != host.IndexOf(_dragCandidate))
                SlideReorder(target);
        }
    }

    private void OnStripPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Snapshot BEFORE releasing capture — Capture(null) synchronously fires
        // PointerCaptureLost, which clears the drag fields.
        var wasDragging = _isDragging;
        var draggedTab = _dragCandidate;
        var sourceHost = _host;

        if (sender is IInputElement) e.Pointer.Capture(null);

        try
        {
            if (draggedTab is null || sourceHost is null) return;
            if (!wasDragging)
            {
                sourceHost.ActivateTab(draggedTab);
                return;
            }
            // Cross-strip drop wins if the pointer is over a sibling strip; same-
            // strip reorder already happened live during PointerMoved.
            if (sourceHost.AllowCrossHostTransfer &&
                FindOtherStripDropTargetUnderPointer(e) is { } found &&
                found.Host.AllowCrossHostTransfer)
            {
                found.View.SnapClearDropGap();
                sourceHost.TransferTab(draggedTab, found.Host, found.Index);
            }
            sourceHost.RequestSave();
        }
        finally
        {
            SetDraggingClass(draggedTab, false);
            SetDragCursor(false);
            RemoveDragGhost();
            ClearAllDropGaps();
            _dragCandidate = null;
            _isDragging = false;
        }
    }

    private void OnStripPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isDragging) _host?.RequestSave();
        SetDraggingClass(_dragCandidate, false);
        SetDragCursor(false);
        RemoveDragGhost();
        ClearAllDropGaps();
        _dragCandidate = null;
        _isDragging = false;
    }

    // === Drag ghost (floating tab visual that follows the pointer) ===

    private void CreateDragGhost(ITabItem tab)
    {
        var overlay = FindDragOverlay();
        if (overlay is null) return;
        RemoveDragGhost();

        var content = new DockPanel { LastChildFill = true };
        if (tab.Icon is not null)
        {
            var img = new Image
            {
                Source = tab.Icon,
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.Uniform,
            };
            DockPanel.SetDock(img, Dock.Left);
            content.Children.Add(img);
        }
        content.Children.Add(new TextBlock { Text = tab.DisplayName });

        _dragGhost = new Border();
        _dragGhost.Classes.Add("tab-ghost");
        // The ghost is parented to the window's overlay, OUTSIDE this control's
        // style scope, so attach the strip styles directly to it — keeps the
        // ghost styled without the host app having to include them app-globally.
        _dragGhost.Styles.Add(new TabStripStyles());
        _dragGhost.Child = content;
        overlay.Children.Add(_dragGhost);
    }

    private void UpdateDragGhostPosition(PointerEventArgs e)
    {
        if (_dragGhost is null) return;
        var overlay = FindDragOverlay();
        if (overlay is null) return;
        var pInOverlay = e.GetPosition(overlay);
        Canvas.SetLeft(_dragGhost, pInOverlay.X - _dragGrabOffset.X);
        Canvas.SetTop(_dragGhost, pInOverlay.Y - _dragGrabOffset.Y);
    }

    private void RemoveDragGhost()
    {
        if (_dragGhost is null) return;
        var overlay = FindDragOverlay();
        overlay?.Children.Remove(_dragGhost);
        _dragGhost = null;
    }

    private Canvas? FindDragOverlay()
    {
        var root = this.FindAncestorOfType<Window>();
        return root?.FindControl<Canvas>("DragOverlay");
    }

    private IEnumerable<TabStrip> SiblingStrips()
    {
        var root = this.FindAncestorOfType<Window>();
        if (root is null) yield break;
        foreach (var strip in root.GetVisualDescendants().OfType<TabStrip>())
            if (!ReferenceEquals(strip, this))
                yield return strip;
    }

    /// <summary>Show a drop gap in whichever sibling strip the pointer is over,
    /// hide on the rest.</summary>
    private void UpdateCrossStripDropGap(PointerEventArgs e)
    {
        var target = FindOtherStripDropTargetUnderPointer(e);
        foreach (var strip in SiblingStrips())
        {
            if (target is { } t && ReferenceEquals(t.View, strip) && t.Host.AllowCrossHostTransfer)
                strip.ShowDropGap(t.Index, _dragSourceWidth);
            else
                strip.HideDropGap();
        }
    }

    private void ClearAllDropGaps()
    {
        foreach (var strip in SiblingStrips())
            strip.HideDropGap();
    }

    private void CaptureDragSourceWidth()
    {
        if (_dragCandidate is null || _host is not { } host) return;
        var idx = host.IndexOf(_dragCandidate);
        if (idx < 0) return;
        if (TabItems.ContainerFromIndex(idx) is Control c)
            _dragSourceWidth = c.Bounds.Width;
    }

    /// <summary>Apply / clear a drop gap on this strip: the container at
    /// <paramref name="atIndex"/> gains a left margin so a dragged tab from a
    /// sibling has a visible landing slot. Called by the source strip.</summary>
    public void ShowDropGap(int atIndex, double gapWidth)
    {
        if (atIndex == _dropGapIndex && Math.Abs(gapWidth - _dropGapWidth) < 0.5) return;
        _dropGapIndex = atIndex;
        _dropGapWidth = gapWidth;
        ApplyDropGap();
    }

    public void HideDropGap()
    {
        if (_dropGapIndex < 0 && _dropGapWidth == 0) return;
        _dropGapIndex = -1;
        _dropGapWidth = 0;
        ApplyDropGap();
    }

    /// <summary>Clear the gap WITHOUT animating — called right before a transfer
    /// commits so the gap-holding container doesn't animate its margin back to 0
    /// as the real inserted tab takes the slot (reads as a phantom slide).</summary>
    public void SnapClearDropGap()
    {
        _dropGapIndex = -1;
        _dropGapWidth = 0;
        for (int i = 0; i < TabItems.ItemCount; i++)
        {
            if (TabItems.ContainerFromIndex(i) is not Control c) continue;
            c.Transitions = null;
            var m = c.Margin;
            if (m.Left != 0) c.Margin = new Thickness(0, m.Top, m.Right, m.Bottom);
        }
    }

    private void ApplyDropGap()
    {
        for (int i = 0; i < TabItems.ItemCount; i++)
        {
            if (TabItems.ContainerFromIndex(i) is not Control c) continue;
            // Margin.Left (not RenderTransform) so the StackPanel grows the
            // ScrollViewer content to fit the gap and trailing tabs don't clip.
            var leftMargin = _dropGapIndex >= 0 && i == _dropGapIndex ? _dropGapWidth : 0;
            EnsureSlideTransitions(c);
            var m = c.Margin;
            if (Math.Abs(m.Left - leftMargin) > 0.5)
                c.Margin = new Thickness(leftMargin, m.Top, m.Right, m.Bottom);
        }
    }

    // === FLIP-animated in-strip reorder ===

    private void SlideReorder(int newIndex)
    {
        if (_dragCandidate is null || _host is not { } host) return;
        var oldIndex = host.IndexOf(_dragCandidate);
        if (oldIndex < 0 || newIndex == oldIndex) return;

        var oldXs = new Dictionary<Control, double>();
        for (int i = 0; i < TabItems.ItemCount; i++)
        {
            if (TabItems.ContainerFromIndex(i) is Control c)
                oldXs[c] = c.Bounds.X;
        }

        host.MoveTab(_dragCandidate, newIndex);

        // Wait for the layout pass so Bounds reflects the new arrangement, then
        // invert each moved neighbor to its old X and animate back to identity.
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var (container, oldX) in oldXs)
            {
                if (ReferenceEquals(container.DataContext, _dragCandidate)) continue;
                var delta = oldX - container.Bounds.X;
                if (Math.Abs(delta) < 0.5) continue;

                // I (Invert): snap to old position without animating.
                container.Transitions = null;
                container.RenderTransform = TransformOperations.Parse(
                    $"translate({delta.ToString(System.Globalization.CultureInfo.InvariantCulture)}px,0)");

                // P (Play): re-enable transitions and animate back to identity on
                // the next tick so the transition has a clear from → to.
                var captured = container;
                Dispatcher.UIThread.Post(() =>
                {
                    EnsureSlideTransitions(captured);
                    captured.RenderTransform = TransformOperations.Identity;
                }, DispatcherPriority.Background);
            }
        }, DispatcherPriority.Loaded);
    }

    private static void EnsureSlideTransitions(Control container)
    {
        // Re-applied every slide because the FLIP invert nulls Transitions out.
        //   • RenderTransform — same-strip FLIP (translate, animate to identity).
        //   • Margin — cross-strip drop gap (Margin.Left expands layout).
        container.Transitions = new Transitions
        {
            new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(320),
                Easing = new CubicEaseOut(),
            },
            new ThicknessTransition
            {
                Property = Layoutable.MarginProperty,
                Duration = TimeSpan.FromMilliseconds(320),
                Easing = new CubicEaseOut(),
            },
        };
    }

    /// <summary>Show a "drag in progress" cursor on every strip under the window —
    /// the captured strip is the only one getting pointer events, but the cursor
    /// must read right wherever the user drags (including into a sibling strip).</summary>
    private void SetDragCursor(bool dragging)
    {
        var cursor = dragging ? new Cursor(StandardCursorType.DragMove) : new Cursor(StandardCursorType.Hand);
        StripRoot.Cursor = cursor;
        foreach (var strip in SiblingStrips())
            strip.StripRoot.Cursor = cursor;
    }

    /// <summary>Toggle the "dragging" class on the templated tab Border so it dims.
    /// Walks under the container to the inner Border (the container is the wrapper).</summary>
    private void SetDraggingClass(ITabItem? tab, bool dragging)
    {
        if (tab is null) return;
        var idx = TabItems.Items is IList list ? list.IndexOf(tab) : -1;
        if (idx < 0 || TabItems.ContainerFromIndex(idx) is not Control container) return;
        foreach (var v in container.GetVisualDescendants())
        {
            if (v is Border b && b.Classes.Contains("tab"))
            {
                b.Classes.Set("dragging", dragging);
                return;
            }
        }
    }

    private bool PointerIsOverOwnStrip(PointerEventArgs e)
    {
        var p = e.GetPosition(StripRoot);
        return p.X >= 0 && p.Y >= 0 && p.X <= StripRoot.Bounds.Width && p.Y <= StripRoot.Bounds.Height;
    }

    private readonly record struct CrossStripDropTarget(TabStrip View, ITabHost Host, int Index);

    /// <summary>If the pointer is over a sibling strip, return that strip + its host
    /// + the drop index within it.</summary>
    private CrossStripDropTarget? FindOtherStripDropTargetUnderPointer(PointerEventArgs e)
    {
        var root = this.FindAncestorOfType<Window>();
        if (root is null) return null;
        var pInRoot = e.GetPosition(root);
        foreach (var strip in SiblingStrips())
        {
            if (strip.Host is not { } host) continue;
            var topLeft = strip.StripRoot.TranslatePoint(new Point(0, 0), root);
            if (topLeft is null) continue;
            var rect = new Rect(topLeft.Value, strip.StripRoot.Bounds.Size);
            if (!rect.Contains(pInRoot)) continue;
            var targetItems = strip.TabItems;
            var pInItems = e.GetPosition(targetItems);
            for (int i = 0; i < targetItems.ItemCount; i++)
            {
                if (targetItems.ContainerFromIndex(i) is not Control container) continue;
                var b = container.Bounds;
                if (pInItems.X < b.X + b.Width / 2) return new CrossStripDropTarget(strip, host, i);
            }
            return new CrossStripDropTarget(strip, host, host.TabCount);
        }
        return null;
    }

    /// <summary>Target index for the dragged tab given a pointer X in TabItems
    /// coords: the first container whose midpoint is past the pointer, else last.</summary>
    private int FindDropIndex(double x)
    {
        for (int i = 0; i < TabItems.ItemCount; i++)
        {
            if (TabItems.ContainerFromIndex(i) is not Control container) continue;
            var bounds = container.Bounds;
            if (x < bounds.X + bounds.Width / 2) return i;
        }
        return TabItems.ItemCount - 1;
    }

    // === Context menu + close ===

    private static ITabItem? TabOf(object? sender) => (sender as Control)?.DataContext as ITabItem;

    private void OnTabCloseClick(object? sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab && _host is { } host) host.CloseTab(tab);
        e.Handled = true;
    }
    private void OnTabPinClick(object? sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab && _host is { } host) host.SetPinned(tab, true);
    }
    private void OnTabUnpinClick(object? sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab && _host is { } host) host.SetPinned(tab, false);
    }
    private void OnTabLockClick(object? sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab && _host is { } host) host.SetLocked(tab, true);
    }
    private void OnTabUnlockClick(object? sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab && _host is { } host) host.SetLocked(tab, false);
    }
    private void OnTabBrowseOnClick(object? sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab && _host is { } host) host.SetBrowseSubfolders(tab, true);
    }
    private void OnTabBrowseOffClick(object? sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab && _host is { } host) host.SetBrowseSubfolders(tab, false);
    }
    private void OnTabDuplicateClick(object? sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab && _host is { } host) _ = host.DuplicateAsync(tab);
    }
    private void OnTabKeepClick(object? sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab && _host is { } host) host.KeepTab(tab);
    }
    private void OnTabCloseOthersClick(object? sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab && _host is { } host) host.CloseOthers(tab);
    }
    private void OnTabCloseAllClick(object? sender, RoutedEventArgs e)
    {
        if (_host is { } host) host.CloseAll();
    }

    /// <summary>Compute each menu item's visibility before the menu opens. Pin /
    /// lock items respect both per-tab state and the host capability flags; close
    /// items hide when they'd be no-ops.</summary>
    private void OnTabContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control c) return;
        if (c.DataContext is not ITabItem tab) return;
        if (c.ContextMenu is not { } menu) return;
        if (_host is not { } host) return;
        foreach (var item in menu.Items)
        {
            if (item is not MenuItem mi || mi.Tag is not string tag) continue;
            mi.IsVisible = tag switch
            {
                "keep"         => tab.IsPreview,
                "pin"          => host.AllowPin && !tab.IsPinned,
                "unpin"        => host.AllowPin && tab.IsPinned,
                "lock"         => host.AllowLock && !tab.IsLocked,
                "unlock"       => host.AllowLock && tab.IsLocked,
                "browse-on"    => host.AllowLock && tab.IsLocked && !tab.BrowseSubfolders,
                "browse-off"   => host.AllowLock && tab.IsLocked && tab.BrowseSubfolders,
                // Close items follow each tab's close affordance (ShowCloseButton) so hosts with fixed /
                // uncloseable tabs don't get no-op menu entries.
                "duplicate"    => host.AllowDuplicate,
                "close"        => tab.ShowCloseButton && !tab.IsPinned && host.TabCount > 1,
                "close-others" => Enumerable.Any(host.Tabs.Cast<ITabItem>(),
                    t => !ReferenceEquals(t, tab) && !t.IsPinned && t.ShowCloseButton),
                "close-all"    => Enumerable.Any(host.Tabs.Cast<ITabItem>(),
                    t => !t.IsPinned && t.ShowCloseButton),
                _ => mi.IsVisible,
            };
        }
    }
}
