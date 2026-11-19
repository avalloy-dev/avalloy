using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;

namespace Avalloy.Icons;

/// <summary>Renders one icon from the active pack (see <see cref="IconGlyph"/>). Set <see cref="Glyph"/>
/// to an icon id ("x", "chevron-up", …); the control resolves the <c>Icon.&lt;Glyph&gt;</c> resource from the
/// nearest scope (so it works inside a per-theme ThemeVariantScope, and pack switching is a dictionary swap)
/// and draws it tinted by <see cref="Foreground"/> (inherits the ambient text colour). Fill and/or stroke per
/// the glyph's metadata; duotone glyphs swap to their lit layers on pointer-over. One control replaces the
/// family-specific icon paths — the geometry is authored once as an SVG and flows through the icon pack
/// baker → XAML → here.</summary>
public sealed class GlyphIcon : Avalonia.Controls.Control, ICustomHitTest
{
    public static readonly StyledProperty<string?> GlyphProperty = AvaloniaProperty.Register<
        GlyphIcon,
        string?
    >(nameof(Glyph));

    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<
        GlyphIcon,
        double
    >(nameof(Size), 16);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<GlyphIcon>();

    /// <summary>Active/selected state — shows the duotone layers (same treatment as hover), so a selected
    /// control reads as "lit". No effect on non-duotone glyphs (they have no duotone).</summary>
    public static readonly StyledProperty<bool> IsActiveProperty = AvaloniaProperty.Register<
        GlyphIcon,
        bool
    >(nameof(IsActive));

    /// <summary>Rotation in degrees about the centre. A pack ships one chevron (angle-up); down/right/left
    /// are rotations (180 / 90 / 270), so a single glyph serves every direction.</summary>
    public static readonly StyledProperty<double> RotationProperty = AvaloniaProperty.Register<
        GlyphIcon,
        double
    >(nameof(Rotation));

    public string? Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
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
    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }
    public double Rotation
    {
        get => GetValue(RotationProperty);
        set => SetValue(RotationProperty, value);
    }

    /// <summary>Snap to <paramref name="from"/> then animate to <paramref name="to"/> — the tree chevrons'
    /// 160ms cubic-ease rotation, for chevrons that live in two swapped controls (terminal collapse strip ⇄
    /// band header) where a style transition has no single element to animate. Installs a local Transitions
    /// collection, so don't combine with style-driven rotation on the same icon.</summary>
    public void SpinRotation(double from, double to)
    {
        Transitions = null;                        // snap to the start pose without animating the jump
        Rotation = from;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Transitions =
            [
                new Avalonia.Animation.DoubleTransition
                {
                    Property = RotationProperty,
                    Duration = System.TimeSpan.FromMilliseconds(160),
                    Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
                },
            ];
            Rotation = to;
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    static GlyphIcon()
    {
        AffectsRender<GlyphIcon>(
            GlyphProperty,
            SizeProperty,
            ForegroundProperty,
            IsPointerOverProperty,
            IsActiveProperty,
            RotationProperty
        );
        AffectsMeasure<GlyphIcon>(SizeProperty);
    }

    protected override global::Avalonia.Size MeasureOverride(global::Avalonia.Size availableSize) =>
        new(Size, Size);

    public override void Render(DrawingContext context)
    {
        if (Glyph is not { Length: > 0 } key)
            return;
        if (!this.TryFindResource("Icon." + key, out var res) || res is not IconGlyph def)
            return;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;
        var brush = Foreground ?? Brushes.Gray;

        // Optical-size swap: below the icon's breakpoint, draw the simplified small variant (its own
        // box / fill / stroke, and its own duotone) instead of the detailed one.
        var useSmall = def.SmallGeometry is not null && Size <= def.SmallMaxSize;
        var geometry = useSmall ? def.SmallGeometry! : def.Geometry;
        var box = useSmall ? def.SmallBox : def.Box;
        var filled = useSmall ? def.SmallFilled : def.Filled;
        var strokeWidth = useSmall ? def.SmallStrokeWidth : def.StrokeWidth;
        var scale = Size / box;

        // Duotone on hover OR active: full-opacity primary over a 0.4 secondary. The small variant uses its
        // own .fill layers when it ships them; otherwise it stays flat when lit.
        var duoPrimary = useSmall ? def.SmallDuoPrimary : def.DuoPrimary;
        var duoSecondary = useSmall ? def.SmallDuoSecondary : def.DuoSecondary;
        var duo = (IsPointerOver || IsActive) && duoPrimary is not null;
        var primary = duo ? duoPrimary! : geometry;
        if (primary is null)
            return;
        var c = primary.Bounds.Center;
        var tx = w / 2 - c.X * scale;
        var ty = h / 2 - c.Y * scale;
        var m = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(tx, ty);
        if (Rotation != 0)
            m *=
                Matrix.CreateTranslation(-w / 2, -h / 2)
                * Matrix.CreateRotation(Rotation * System.Math.PI / 180)
                * Matrix.CreateTranslation(w / 2, h / 2);

        using (context.PushTransform(m))
        {
            if (duo)
            {
                if (duoSecondary is { } sec)
                    using (context.PushOpacity(0.4))
                        context.DrawGeometry(brush, null, sec);
                context.DrawGeometry(brush, null, duoPrimary!);
                return;
            }
            if (filled)
                context.DrawGeometry(brush, null, geometry!);
            if (strokeWidth > 0)
                context.DrawGeometry(
                    null,
                    new Pen(brush, strokeWidth)
                    {
                        LineCap = PenLineCap.Round,
                        LineJoin = PenLineJoin.Round,
                    },
                    geometry!
                );
        }
    }

    /// <summary>Hit-test the whole icon rectangle, not just the painted glyph pixels — so hover/active (the
    /// duotone swap) fires anywhere over the control, not only when the cursor is over a filled stroke. The point
    /// arrives in local coordinates, so test against the layout size (origin 0,0), not <see cref="Visual.Bounds"/>
    /// (which is offset within the parent).</summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);
}
