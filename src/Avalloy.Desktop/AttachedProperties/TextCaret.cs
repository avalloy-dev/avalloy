using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Avalloy.AttachedProperties;

/// <summary>
/// Replaces a <see cref="TextBox"/>'s caret with one drawn at the true text-end position.
///
/// Avalonia's own caret is drawn inside <c>TextPresenter.Render</c>, which is <c>public sealed override</c> — it
/// cannot be subclassed — and <c>TextPresenter.GetCaretPoints()</c> puts it in the wrong place twice over:
/// <code>
///   var x = Math.Floor(_caretBounds.X) + 0.5;                              // (1) fractional advance dropped
///   if (_caretBounds.X > 0 &amp;&amp; _caretBounds.X >= textLine.WidthIncludingTrailingWhitespace)
///       x -= 1;                                                            // (2) a whole DIP left, at end-of-line
/// </code>
/// (2) exists so the hairline caret is not clipped by the presenter's right edge, but it is unconditional — and
/// end-of-line is exactly where the caret sits while you are typing. Together they land the caret 1–2 DIP left of
/// where the text actually ends, i.e. on top of the last glyph. Text sizes give the ink less clearance than that
/// (Onest's <c>m</c> has 62/1000 em of right sidebearing — 0.81 DIP at 13px), so the caret visibly overstrikes the
/// final stem. It reads worst on stem-enders (m n i l) where the caret runs parallel to the stroke.
///
/// So: force the presenter's own caret transparent and paint ours on an overlay inside the box's own template instead, from the unfloored
/// <see cref="TextLayout.HitTestTextPosition"/> rect, snapped to the DEVICE pixel grid (Avalonia snaps to the DIP
/// grid, which is what loses up to a whole pixel at 2x). Width is ours to pick — Avalonia's is a fixed 1 DIP.
///
/// Correct placement alone still reads as crowded, because a font's right sidebearing is the only thing between
/// the caret and the last glyph and it is under a pixel at UI sizes — so at the end of a line, where there is
/// nothing to collide with, the caret takes a small <see cref="LeadProperty"/> into the empty space.
///
/// Turn on from a style, no per-site XAML:
/// <code>&lt;Setter Property="ap:TextCaret.Precise" Value="True"/&gt;</code>
/// The caret paints in <see cref="TextBox.CaretBrush"/>, so existing theming carries over untouched.
/// </summary>
public static class TextCaret
{
    /// <summary>Draw this box's caret ourselves, correctly positioned, instead of Avalonia's.</summary>
    public static readonly AttachedProperty<bool> PreciseProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("Precise", typeof(TextCaret));

    /// <summary>Caret width in DIP. Avalonia hard-codes 1; 1.5 holds up better against text at 13px and under.</summary>
    public static readonly AttachedProperty<double> WidthProperty =
        AvaloniaProperty.RegisterAttached<TextBox, double>("Width", typeof(TextCaret), 1.5);

    /// <summary>
    /// Extra clearance in DIP between the end of the text and the caret, applied ONLY at the end of a line.
    ///
    /// The true text-end position is correct but tight: a font's right sidebearing is all that separates the caret
    /// from the final glyph, and that is under a pixel at UI sizes (Onest's <c>m</c>: 0.81 DIP at 13px). Correct
    /// still reads as touching when a full-height caret runs alongside a vertical stem. Past the end of a line
    /// there is nothing to collide with, so we take a small lead into the empty space.
    ///
    /// Mid-text this is deliberately NOT applied — there the caret sits in the gap between two glyphs, which is
    /// only the two sidebearings wide, and leading it would just crowd the glyph on the other side instead.
    /// </summary>
    /// Half a DIP is one device pixel at 2x, so the lead snaps to exactly one pixel of extra air rather than
    /// landing between two — the smallest step that reads at all.
    public static readonly AttachedProperty<double> LeadProperty =
        AvaloniaProperty.RegisterAttached<TextBox, double>("Lead", typeof(TextCaret), 0.5);

    public static void SetPrecise(TextBox o, bool v) => o.SetValue(PreciseProperty, v);
    public static bool GetPrecise(TextBox o) => o.GetValue(PreciseProperty);
    public static void SetWidth(TextBox o, double v) => o.SetValue(WidthProperty, v);
    public static double GetWidth(TextBox o) => o.GetValue(WidthProperty);
    public static void SetLead(TextBox o, double v) => o.SetValue(LeadProperty, v);
    public static double GetLead(TextBox o) => o.GetValue(LeadProperty);

    // One overlay per box, keyed weakly so a discarded box (virtualised rows, closed overlays) takes its overlay
    // with it even if a detach event never arrives.
    private static readonly ConditionalWeakTable<TextBox, CaretOverlay> Overlays = new();

    static TextCaret()
    {
        PreciseProperty.Changed.AddClassHandler<TextBox>((tb, e) =>
        {
            if (e.GetNewValue<bool>()) Enable(tb);
            else Disable(tb);
        });

        WidthProperty.Changed.AddClassHandler<TextBox>((tb, _) =>
        {
            if (Overlays.TryGetValue(tb, out var overlay)) overlay.Invalidate();
        });

        LeadProperty.Changed.AddClassHandler<TextBox>((tb, _) =>
        {
            if (Overlays.TryGetValue(tb, out var overlay)) overlay.Invalidate();
        });
    }

    private static void Enable(TextBox box)
    {
        // Idempotent: the style setter can arrive before the template, before the visual tree, or after both, and
        // a retemplate/reattach has to rebuild the overlay against the new presenter.
        //
        // Every one-shot signal here is too early on its own — a style setter runs before the template exists,
        // AttachedToVisualTree before the template is applied, and TemplateApplied before the template's
        // panels are reachable. Miss the window and the box silently keeps Avalonia's caret, which is exactly the bug this
        // class exists to fix. So we also retry on LayoutUpdated until it takes, then stop listening.
        box.TemplateApplied -= OnRehook;
        box.TemplateApplied += OnRehook;
        box.AttachedToVisualTree -= OnAttached;
        box.AttachedToVisualTree += OnAttached;
        box.DetachedFromVisualTree -= OnDetached;
        box.DetachedFromVisualTree += OnDetached;
        Retry(box);
        Hook(box);
    }

    private static void Disable(TextBox box)
    {
        box.TemplateApplied -= OnRehook;
        box.AttachedToVisualTree -= OnAttached;
        box.DetachedFromVisualTree -= OnDetached;
        box.LayoutUpdated -= OnRetry;
        Unhook(box);
    }

    /// <summary>Listen for another chance to attach. Self-cancelling — <see cref="Hook"/> drops it on success.</summary>
    private static void Retry(TextBox box)
    {
        box.LayoutUpdated -= OnRetry;
        box.LayoutUpdated += OnRetry;
    }

    private static void OnRetry(object? sender, EventArgs e)
    {
        if (sender is TextBox box) Hook(box);
    }

    private static void OnRehook(object? sender, TemplateAppliedEventArgs e)
    {
        if (sender is not TextBox box) return;
        Unhook(box);
        Hook(box);
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox box) Hook(box);
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox box) Unhook(box);
    }

    private static void Hook(TextBox box)
    {
        if (Overlays.TryGetValue(box, out _)) return;

        // Only while on screen: a detached box keeps its template, so without this the retry Unhook arms would put
        // a fresh overlay straight back into a box that has left the tree (and whose blink timer nobody will stop).
        if (TopLevel.GetTopLevel(box) is null) return;

        if (box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault() is not { } presenter)
            return;

        // The text VIEWPORT is what the caret clips to (the presenter moves when the text scrolls; the viewport stays
        // put), which makes scrolled-out-of-view carets clip away for free.
        var viewport = presenter.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        if (viewport is not null && !viewport.GetVisualAncestors().Contains(box))
            viewport = null;   // someone else's scroller (a page/list around the box) — not our viewport

        if (FindHost(box, (Visual?)viewport ?? presenter) is not { } host)
            return;

        var overlay = new CaretOverlay(box, presenter, viewport);
        if (host.Cell is { } cell)
        {
            Grid.SetRow(overlay, Grid.GetRow(cell));
            Grid.SetColumn(overlay, Grid.GetColumn(cell));
            Grid.SetRowSpan(overlay, Grid.GetRowSpan(cell));
            Grid.SetColumnSpan(overlay, Grid.GetColumnSpan(cell));
        }
        host.Panel.Children.Add(overlay);
        Overlays.Add(box, overlay);
        box.LayoutUpdated -= OnRetry;   // attached — stop watching

        // Silence Avalonia's caret. A local value on the presenter outranks the template binding that feeds it
        // from TextBox.CaretBrush, so the box's own brush stays intact for us to paint with.
        presenter.SetValue(TextPresenter.CaretBrushProperty, Brushes.Transparent);
    }

    /// <summary>
    /// Where the caret lives: the nearest panel ABOVE the text viewport, inside the box's own template, that stacks
    /// its children over one another — a plain <see cref="Panel"/>, or a <see cref="Grid"/> (then in the cell of the
    /// child that holds the viewport). Above the viewport so its clip doesn't eat an end-of-text caret.
    ///
    /// Inside the box rather than in the window's adorner layer, so the caret is an ordinary part of the field: it
    /// moves with it, is clipped by whatever clips it (a transcript it is half scrolled out of), and is covered by
    /// whatever covers it. The adorner layer was none of those — it sits above the window's overlay layer, so the
    /// caret painted through popups and modals over the field; and the compositor places an adorner live while
    /// AdornerLayer's arrange-time size and clip lag behind it, so a field that moved without a re-arrange showed a
    /// short caret riding above its text.
    /// </summary>
    private static (Panel Panel, Control? Cell)? FindHost(TextBox box, Visual from)
    {
        var child = from;
        foreach (var ancestor in from.GetVisualAncestors())
        {
            if (ancestor == box) return null;
            if (ancestor is Grid grid) return (grid, child as Control);
            if (ancestor.GetType() == typeof(Panel)) return ((Panel)ancestor, null);
            child = ancestor;
        }
        return null;
    }

    private static void Unhook(TextBox box)
    {
        if (!Overlays.TryGetValue(box, out var overlay)) return;
        Overlays.Remove(box);

        overlay.Release();
        if (overlay.Parent is Panel panel) panel.Children.Remove(overlay);

        if (box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault() is { } presenter)
            presenter.ClearValue(TextPresenter.CaretBrushProperty);

        // Still wanted, just not currently attachable (a detach, a retemplate) — go back to watching for the
        // next chance, or a recycled row would come back with Avalonia's caret.
        if (GetPrecise(box)) Retry(box);
    }

    /// <summary>
    /// The caret itself. Lives in the box's template, over the text (see <see cref="FindHost"/>), hit-test invisible,
    /// and owns its own blink because <c>TextPresenter</c>'s blink state is private.
    /// </summary>
    private sealed class CaretOverlay : Control
    {
        private readonly TextBox _box;
        private readonly TextPresenter _presenter;
        private readonly ScrollViewer? _viewport;
        private readonly DispatcherTimer _blink;
        private bool _visible = true;
        private bool _deferred;

        public CaretOverlay(TextBox box, TextPresenter presenter, ScrollViewer? viewport)
        {
            _box = box;
            _presenter = presenter;
            _viewport = viewport;
            IsHitTestVisible = false;

            _blink = new DispatcherTimer();
            _blink.Tick += (_, _) =>
            {
                _visible = !_visible;
                Invalidate();
            };

            _presenter.CaretBoundsChanged += OnCaretMoved;
            _presenter.PropertyChanged += OnPresenterPropertyChanged;
            _box.PropertyChanged += OnBoxPropertyChanged;
            if (_viewport is not null) _viewport.ScrollChanged += OnScrolled;

            if (_box.IsFocused) Restart();
        }

        public void Release()
        {
            _blink.Stop();
            _presenter.CaretBoundsChanged -= OnCaretMoved;
            _presenter.PropertyChanged -= OnPresenterPropertyChanged;
            _box.PropertyChanged -= OnBoxPropertyChanged;
            if (_viewport is not null) _viewport.ScrollChanged -= OnScrolled;
        }

        private void OnCaretMoved(object? sender, EventArgs e) => Restart();

        private void OnScrolled(object? sender, ScrollChangedEventArgs e) => Invalidate();

        // The caret is drawn relative to where the presenter sits inside the template; re-draw when that moves.
        private void OnPresenterPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == BoundsProperty) Invalidate();
        }

        /// <summary>
        /// <see cref="Visual.InvalidateVisual"/>, but legal from inside the render pass.
        ///
        /// <c>TextPresenter</c> can reach us from there. Its <c>TextLayout</c> is rebuilt LAZILY, so anything that
        /// drops it without forcing a measure first — changing the app's font is the one that bit — has the rebuild
        /// happen inside <c>Render</c>. The rebuild recomputes the caret bounds, the new font's metrics make them
        /// differ, and <c>CaretBoundsChanged</c> fires mid-render. <c>CompositingRenderer.AddDirty</c> answers an
        /// invalidation at that moment with <c>InvalidOperationException</c>, which kills the render job outright:
        /// picking a font took the whole app down.
        ///
        /// There is no public way to ASK whether a render pass is running, so the throw IS the signal — try, and on
        /// rejection re-post the invalidation to land after the pass. It costs one exception on the rare frame that
        /// rebuilds a layout during render. Deferring unconditionally instead would put a frame of lag on every
        /// caret move, which is the one thing this class exists not to do.
        /// </summary>
        internal void Invalidate()
        {
            try
            {
                InvalidateVisual();
            }
            catch (InvalidOperationException)
            {
                if (_deferred) return;
                _deferred = true;
                Dispatcher.UIThread.Post(() =>
                {
                    _deferred = false;
                    InvalidateVisual();
                }, DispatcherPriority.Render);
            }
        }

        private void OnBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == InputElement.IsFocusedProperty)
            {
                if (e.GetNewValue<bool>())
                {
                    Restart();
                }
                else
                {
                    _blink.Stop();
                    _visible = false;
                    Invalidate();
                }
            }
            else if (e.Property == TextBox.CaretIndexProperty || e.Property == TextBox.TextProperty ||
                     e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty)
            {
                // Typing/moving restarts the phase so the caret is solid under the cursor, never mid-blink-off
                // on the frame you land — the same courtesy Avalonia's ResetCaretTimer does.
                Restart();
            }
            else if (e.Property == TextBox.CaretBrushProperty)
            {
                Invalidate();   // theme swap
            }
        }

        private void Restart()
        {
            _visible = true;
            _blink.Stop();

            // A zero/negative interval is the platform's "don't blink" (accessibility setting) — hold it solid.
            var interval = _box.CaretBlinkInterval;
            if (interval > TimeSpan.Zero && _box.IsFocused)
            {
                _blink.Interval = interval;
                _blink.Start();
            }

            Invalidate();
        }

        public override void Render(DrawingContext context)
        {
            if (!_visible || !_box.IsFocused) return;
            if (_box.SelectionStart != _box.SelectionEnd) return;    // Avalonia hides the caret mid-selection too
            if (_box.CaretBrush is not { } brush) return;

            // The unfloored, unshifted caret rect — the value Avalonia keeps in _caretBounds before GetCaretPoints
            // mangles it. Presenter-local, and built from the presenter's OWN layout, so password masking, wrapping
            // and inline hits all come out right.
            var layout = _presenter.TextLayout;
            var index = Math.Clamp(_presenter.CaretIndex, 0, _presenter.Text?.Length ?? 0);
            var caret = layout.HitTestTextPosition(index);

            if (_presenter.TranslatePoint(caret.Position, this) is not { } origin) return;
            var clipTo = (Visual?)_viewport ?? _presenter;
            if (clipTo.TranslatePoint(default, this) is not { } clipOrigin) return;

            // Past the end of a line there is empty space, so take the lead into it rather than sitting a
            // sub-pixel sidebearing away from the final stem. The `caret.X > 0` guard keeps an empty box's caret
            // pinned to the text origin, so it doesn't visibly jump when the first character lands.
            var line = layout.TextLines[layout.GetLineIndexFromCharacterIndex(index, true)];
            var atLineEnd = caret.X > 0 && caret.X >= line.WidthIncludingTrailingWhitespace;
            var lead = atLineEnd ? GetLead(_box) : 0;

            // Snap to DEVICE pixels, not DIPs: at 2x a DIP-grid snap throws away up to two device pixels, which is
            // most of the sidebearing we are trying to preserve. Round (nearest) rather than floor — the error is
            // then ±half a device pixel and unbiased, instead of always leftward.
            var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            var width = Math.Max(1 / scale, Snap(GetWidth(_box), scale));
            var x = Snap(origin.X + lead, scale);
            var y = Math.Floor(origin.Y * scale) / scale;
            var height = Math.Ceiling(caret.Height * scale) / scale;

            var clip = new Rect(clipOrigin, clipTo.Bounds.Size).Inflate(new Thickness(0, 0, width + lead, 0));

            using (context.PushClip(clip))
                context.FillRectangle(brush, new Rect(x, y, width, height));
        }

        private static double Snap(double value, double scale) =>
            Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
    }
}
