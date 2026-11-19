using Avalonia.Media;

namespace Avalloy.Views.Controls;

/// <summary>
/// An <see cref="AlloySelect"/> item that supplies its own row content, read directly rather than
/// through the <c>*Path</c> property names — which need reflection, and so can fail in a trimmed or
/// Native AOT app.
/// </summary>
/// <remarks>
/// The label is always taken from <see cref="Label"/>. The other slots still appear only when the
/// select asks for them, as for any item: set its <c>DescriptionPath</c>, <c>DotPath</c>,
/// <c>GlyphPath</c> or <c>GlyphTonePath</c> — conventionally to the member name here
/// (<c>DotPath="Dot"</c>) — and the value comes from this interface instead of a property lookup.
/// </remarks>
public interface IAlloySelectItem
{
    /// <summary>The row's label, and the chip's while this item is selected.</summary>
    string Label { get; }

    /// <summary>The muted second line under the label.</summary>
    string? Description => null;

    /// <summary>The leading status dot's brush (a Status.* theme brush).</summary>
    IBrush? Dot => null;

    /// <summary>The leading jelly icon's GlyphIcon role name (e.g. "Bolt").</summary>
    string? Glyph => null;

    /// <summary>The leading glyph's tint; null = the default Text.Secondary.</summary>
    IBrush? GlyphTone => null;
}
