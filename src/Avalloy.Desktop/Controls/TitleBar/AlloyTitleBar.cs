using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Avalloy.Controls.TitleBar;

/// <summary>
/// Cross-platform integrated title bar for extended-client-area windows
/// (<c>ExtendClientAreaToDecorationsHint=true</c>, optionally
/// <c>MacOSTitleBar.IsThick</c>). The control NEVER draws caption buttons —
/// native chrome owns them (macOS traffic lights on the left, the Windows/Linux
/// caption strip on the right) — it measures around them instead:
/// <list type="bullet">
///   <item><see cref="ContentControl.Content"/> fills the bar and is padded by
///         <see cref="TemplatedControl.Padding"/>, whose platform default
///         (14 left; ~100 on macOS) clears the traffic lights. Set
///         <c>Padding="0"</c> for content that centers itself.</item>
///   <item><see cref="RightContent"/> is pinned right and automatically inset
///         past the OS caption buttons: <c>WindowDecorationMargin.Right</c>
///         when the platform reports it, else the exact DWM caption-button
///         bounds on Windows (DPI-aware, 3-vs-4-button-aware), else a
///         4-button worst-case reservation. On Windows/Linux it also gets a
///         small render-transform up-nudge onto the caption-glyph line.</item>
///   <item>Empty bar space drags the window; double-click toggles
///         Normal ↔ Maximized (<see cref="CanDrag"/> /
///         <see cref="CanToggleWindowStateOnDoubleTapped"/>).</item>
/// </list>
/// The pointer handling is modelled on Avalonia's own <c>TitleBar</c> chrome control:
/// <see cref="Window.BeginMoveDrag"/> for the drag and the <see cref="InputElement.DoubleTappedEvent"/>
/// gesture for the toggle. Caption-button measurement proven on macOS + Windows.
/// </summary>
public class AlloyTitleBar : ContentControl
{
    public static readonly StyledProperty<object?> RightContentProperty =
        AvaloniaProperty.Register<AlloyTitleBar, object?>(nameof(RightContent));

    public static readonly StyledProperty<bool> CanDragProperty =
        AvaloniaProperty.Register<AlloyTitleBar, bool>(nameof(CanDrag), defaultValue: true);

    public static readonly StyledProperty<bool> CanToggleWindowStateOnDoubleTappedProperty =
        AvaloniaProperty.Register<AlloyTitleBar, bool>(nameof(CanToggleWindowStateOnDoubleTapped), defaultValue: true);

    /// <summary>Content pinned to the bar's right edge, kept clear of the OS
    /// caption buttons automatically.</summary>
    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    /// <summary>Whether pressing empty bar space starts a window move-drag.</summary>
    public bool CanDrag
    {
        get => GetValue(CanDragProperty);
        set => SetValue(CanDragProperty, value);
    }

    /// <summary>Whether double-clicking empty bar space toggles Normal ↔ Maximized.</summary>
    public bool CanToggleWindowStateOnDoubleTapped
    {
        get => GetValue(CanToggleWindowStateOnDoubleTappedProperty);
        set => SetValue(CanToggleWindowStateOnDoubleTappedProperty, value);
    }

    private static readonly FuncControlTemplate<AlloyTitleBar> s_template = new((owner, scope) =>
    {
        var content = new ContentPresenter
        {
            Name = "PART_ContentPresenter",
            [!ContentPresenter.ContentProperty] = owner[!ContentProperty],
            [!ContentPresenter.ContentTemplateProperty] = owner[!ContentTemplateProperty],
            [!ContentPresenter.PaddingProperty] = owner[!PaddingProperty],
        }.RegisterInNameScope(scope);

        var right = new ContentPresenter
        {
            Name = "PART_RightContentPresenter",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            [!ContentPresenter.ContentProperty] = owner[!RightContentProperty],
        }.RegisterInNameScope(scope);

        // Panel (not DockPanel) so Content can center against the FULL bar
        // width — the right cluster overlays rather than subtracts.
        return new Border
        {
            [!Border.BackgroundProperty] = owner[!BackgroundProperty],
            [!Border.BorderBrushProperty] = owner[!BorderBrushProperty],
            [!Border.BorderThicknessProperty] = owner[!BorderThicknessProperty],
            Child = new Panel { Children = { content, right } },
        };
    });

    private Window? _window;
    private ContentPresenter? _rightPresenter;

    public AlloyTitleBar()
    {
        MinHeight = 40;
        // Null background = no hit-test = no drag from empty space. Consumers
        // restyle with their own brush; transparent keeps the contract honest.
        Background = Brushes.Transparent;
        // Room for the native controls on the content side: macOS traffic
        // lights cluster left (~100 in the thick unified titlebar), other
        // platforms only need a small gutter (their buttons live on the right,
        // which RightContent handles by measurement).
        Padding = OperatingSystem.IsMacOS() ? new Thickness(100, 0, 0, 0) : new Thickness(14, 0, 0, 0);
        Template = s_template;
        // The toggle rides Avalonia's gesture recogniser, which applies the platform's own
        // double-click interval and distance, rather than a click count read here.
        AddHandler(DoubleTappedEvent, OnDoubleTapped);
    }

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _rightPresenter = e.NameScope.Find<ContentPresenter>("PART_RightContentPresenter");
        UpdateRightContentInset();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is { } w)
        {
            w.PropertyChanged += OnWindowPropertyChanged;
            // The DWM query needs the native handle; re-measure once the
            // window has actually opened, not just when we attach.
            w.Opened += OnWindowOpened;
        }
        UpdateRightContentInset();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_window is { } w)
        {
            w.PropertyChanged -= OnWindowPropertyChanged;
            w.Opened -= OnWindowOpened;
        }
        _window = null;
    }

    private void OnWindowOpened(object? sender, System.EventArgs e) => UpdateRightContentInset();

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // The DWM caption strip moves/resizes when the window is restored vs
        // maximized and when the per-monitor scale changes, so re-measure on
        // those too — not just the decoration-margin notifications.
        if (e.Property == Window.WindowDecorationMarginProperty
            || e.Property == Window.OffScreenMarginProperty
            || e.Property == Window.ClientSizeProperty
            || e.Property == Window.WindowStateProperty)
            UpdateRightContentInset();
    }

    // ── Pointer: drag on a press, toggle the window state on a double-tap ──
    //
    // Two guards on top of the primitives. A press that lands on an interactive child is left
    // alone: a MenuItem only handles PointerReleased, so a drag started on its press would take
    // the selection with it. And the second press of a double-click does not start a drag,
    // because on Windows BeginMoveDrag is a modal move loop that would run to completion before
    // the gesture fires, leaving the toggle to land after an empty drag.

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled || !CanDrag || e.ClickCount != 1)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !IsBareBarPress(e.Source))
            return;
        if (_window is not { } window || window.WindowState == WindowState.FullScreen)
            return;

        // Handed to the OS as a window move, so it behaves like the native caption on each
        // platform: snap layouts and edge tiling on Windows, Spaces on macOS, tiling on Linux.
        window.BeginMoveDrag(e);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Handled || !CanToggleWindowStateOnDoubleTapped || !IsBareBarPress(e.Source))
            return;
        // CanMaximize is the window's own answer, the one its native caption button obeys.
        if (_window is not { } window || window.WindowState == WindowState.FullScreen || !window.CanMaximize)
            return;

        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        e.Handled = true;
    }

    /// <summary>
    /// Whether a press at <paramref name="source"/> is on the bar itself rather than on a control
    /// hosted in it. Focusable is the test: a Button, MenuItem or TextBox is; a TextBlock, a
    /// presenter or a panel is not.
    /// </summary>
    private bool IsBareBarPress(object? source)
    {
        for (var visual = source as Visual; visual is not null && visual != this; visual = visual.GetVisualParent())
        {
            if (visual is InputElement { Focusable: true })
                return false;
        }
        return true;
    }

    // ── Caption-button measurement (native inset query per platform) ──

    private void UpdateRightContentInset()
    {
        if (_rightPresenter is not { } presenter || _window is not { } window) return;

        // Avalonia 12 reports WindowDecorationMargin as (0, titleHeight, 0, 0)
        // on Windows extended-client-area — only the top is populated, not the
        // caption-button strip. macOS traffic lights sit on the left, so no
        // right reservation is needed there.
        double right = window.WindowDecorationMargin.Right;
        if (right <= 0 && OperatingSystem.IsWindows())
        {
            // Ask DWM for the exact caption-button bounds so the cluster sits
            // flush against the real buttons whether the build shows 3 or 4 of
            // them. Falls back to the 4-button worst case if the query fails
            // (e.g. before the native window exists).
            right = TryGetCaptionBounds(window, out double w) ? w : 190;
        }
        else if (right <= 0 && OperatingSystem.IsLinux())
        {
            right = 190; // ~46 px × 4 buttons + a few px padding
        }
        presenter.Margin = new Thickness(0, 0, right, 0);

        // The native caption strip sits higher than the bar's vertical center,
        // and layout alignment can't reach past the presenter's centering — a
        // render-transform nudge moves the rendered cluster up onto the OS
        // caption-glyph line. macOS centers cleanly (lights on the left).
        double upNudge = OperatingSystem.IsWindows() || OperatingSystem.IsLinux() ? 8 : 0;
        presenter.RenderTransform = upNudge > 0 ? new TranslateTransform(0, -upNudge) : null;
    }

    // DWMWA_CAPTION_BUTTON_BOUNDS — bounding rect of the min/max/close strip in
    // physical client-area pixels. Lets us reserve exactly the strip's width
    // (DPI-aware, count-aware) instead of hardcoding a pixel guess.
    private const int DWMWA_CAPTION_BUTTON_BOUNDS = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attr, out RECT value, int size);

    private static bool TryGetCaptionBounds(Window window, out double widthDip)
    {
        widthDip = 0;
        if (window.TryGetPlatformHandle() is not { Handle: var hwnd } || hwnd == nint.Zero)
            return false;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CAPTION_BUTTON_BOUNDS, out var r, Marshal.SizeOf<RECT>()) != 0)
            return false;
        int widthPx = r.Right - r.Left;
        if (widthPx <= 0) return false;
        // Bounds are physical px; RenderScaling is px-per-DIP. Add a small
        // gutter so the cluster doesn't kiss the minimize button.
        widthDip = widthPx / window.RenderScaling + 4;
        return true;
    }
}
