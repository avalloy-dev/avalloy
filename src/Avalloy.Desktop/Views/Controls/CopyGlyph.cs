using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Avalloy.Icons;

namespace Avalloy.Views.Controls;

/// <summary>The one copy-to-clipboard affordance: a Copy glyph that flips to an <c>Ok</c>-tinted Check for a
/// beat after a successful copy, then flips back. The flash IS the receipt — no toast, and no layout shift
/// (both glyphs draw at the same <see cref="Size"/> in the same box), so it drops into a dense inline row or a
/// hover-fill icon button without moving anything around it.
///
/// Chrome-free on purpose: this control is only the glyph, so the host supplies whatever hit target it already
/// has (an icon-button Border, a bare Button wrapping a label plus the glyph). Two ways to drive it:
///
///   • Host-driven — the outer Border/Button owns the tap, because the hover fill and tooltip live there: its
///     handler calls <see cref="CopyAsync"/> with the text, reaching the glyph via <see cref="Find"/> off the
///     sender. Nothing is copied until the host asks.
///   • Self-driven — set <see cref="Text"/> and the glyph handles its own tap.
///
/// A failed copy (no clipboard on the top level, a platform refusal) leaves the glyph alone: an unchanged Copy
/// icon reads as "that didn't take", where a check would lie.</summary>
public sealed class CopyGlyph : Decorator
{
    /// <summary>How long the check sits before flipping back. Long enough to catch out of the corner of an
    /// eye, short enough that a second copy isn't waiting on it.</summary>
    private static readonly TimeSpan CheckHold = TimeSpan.FromSeconds(1.3);

    /// <summary>Optional payload for the self-driven case: set it and tapping the glyph copies it. Leave it
    /// null when the host owns the tap and hands text to <see cref="CopyAsync"/>.</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<CopyGlyph, string?>(nameof(Text));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<CopyGlyph, double>(nameof(Size), 14);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<CopyGlyph>();

    /// <summary>Tint for the check. Defaults to the <c>Ok</c> token, resolved from the nearest scope at flash
    /// time so it follows the theme; set it only to say something other than "that worked".</summary>
    public static readonly StyledProperty<IBrush?> CopiedForegroundProperty =
        AvaloniaProperty.Register<CopyGlyph, IBrush?>(nameof(CopiedForeground));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush? CopiedForeground
    {
        get => GetValue(CopiedForegroundProperty);
        set => SetValue(CopiedForegroundProperty, value);
    }

    private readonly GlyphIcon _icon;
    private DispatcherTimer? _timer;

    public CopyGlyph()
    {
        _icon = new GlyphIcon
        {
            Glyph = "Copy",
            Size = Size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Child = _icon;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SizeProperty)
            _icon.Size = change.GetNewValue<double>();
        // Push the resting tint only while resting: mid-flash the check owns the brush, and the timer restores
        // whatever Foreground says when it fires.
        else if (change.Property == ForegroundProperty && _timer is null)
            _icon.Foreground = change.GetNewValue<IBrush?>();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_timer is null)
            _icon.Foreground = Foreground;
    }

    /// <summary>The self-driven path. Host-driven sites leave <see cref="Text"/> null, so this no-ops and the
    /// tap bubbles on to whichever Border/Button wraps the glyph.</summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;
        if (Text is not { Length: > 0 } text)
            return;
        e.Handled = true;
        _ = CopyAsync(text);
    }

    /// <summary>Put <paramref name="text"/> on the clipboard, then flash the check. Empty text and a missing
    /// clipboard both leave the glyph untouched — it never claims a copy that didn't happen.</summary>
    public async Task CopyAsync(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;
        try
        {
            await clipboard.SetTextAsync(text);
        }
        catch
        {
            return; // platform said no — leave the Copy glyph standing rather than fake a receipt
        }
        Flash();
    }

    /// <summary>Show the check for a beat without touching the clipboard — for a host that already copied, or
    /// copied something a plain string can't carry (an image, a file list).</summary>
    public void Flash()
    {
        _icon.Glyph = "Check";
        _icon.Foreground =
            CopiedForeground
            ?? (this.TryFindResource("Ok", out var ok) ? ok as IBrush : null)
            ?? Foreground;

        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = CheckHold };
        _timer.Tick += (_, _) =>
        {
            _timer?.Stop();
            _timer = null;
            _icon.Glyph = "Copy";
            _icon.Foreground = Foreground;
        };
        _timer.Start();
    }

    /// <summary>The glyph inside <paramref name="host"/> (typically the tapped Border/Button), or null. Saves
    /// hosts a FindControl: a glyph nested in a popup's content lives in its own name scope, and views loaded
    /// via AvaloniaXamlLoader don't get their x:Name fields populated at all.</summary>
    public static CopyGlyph? Find(object? host) =>
        host is Visual v ? v as CopyGlyph ?? v.GetVisualDescendants().OfType<CopyGlyph>().FirstOrDefault() : null;
}
