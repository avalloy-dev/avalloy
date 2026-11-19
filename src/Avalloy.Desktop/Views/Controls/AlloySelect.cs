using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalloy.Icons;


namespace Avalloy.Views.Controls;

/// <summary>The standard simple combobox — a restyled NATIVE <see cref="ComboBox"/>, so arrow/Home/End
/// navigation, typeahead, F4/Alt+Down, Escape, focus handling and the automation peer all come from the
/// primitive; AlloyControls.axaml swaps the Fluent template for the actionbtn-style chip +
/// <c>popoverframe</c> popover with check rows. For plain "pick one of these" cases — base branch,
/// reviewer, any prefs option list. Bind <see cref="ItemsControl.ItemsSource"/> + SelectedItem (TwoWay);
/// items display via <see cref="LabelPath"/> (a property name, wired to DisplayMemberBinding) or
/// <c>ToString()</c>. <see cref="TriggerText"/> overrides the chip label when it isn't just the selected
/// item's label (e.g. "from main"). Rich pickers (search/sections/trees) compose AlloyDropDown instead.
/// <para>The <c>*Path</c> properties name item properties, which are read by reflection: in a trimmed or
/// Native AOT app, give the items <see cref="IAlloySelectItem"/> instead, and they are read directly.</para></summary>
public class AlloySelect : ComboBox
{
    protected override Type StyleKeyOverride => typeof(AlloySelect);

    /// <summary>Property name to display per item; null = <c>ToString()</c>.</summary>
    public static readonly StyledProperty<string?> LabelPathProperty =
        AvaloniaProperty.Register<AlloySelect, string?>(nameof(LabelPath));

    /// <summary>Optional property name for a second, muted caption line under each row's label
    /// (e.g. the worktrees-mode explanations). Null = single-line rows.</summary>
    public static readonly StyledProperty<string?> DescriptionPathProperty =
        AvaloniaProperty.Register<AlloySelect, string?>(nameof(DescriptionPath));

    /// <summary>Optional property name for the leading 9×9 status dot — the item property must return an
    /// <see cref="IBrush"/> (a Status.* theme brush). Drives the row dots AND the trigger's value dot;
    /// while nothing is selected the trigger shows the placeholder ring instead.</summary>
    public static readonly StyledProperty<string?> DotPathProperty =
        AvaloniaProperty.Register<AlloySelect, string?>(nameof(DotPath));

    /// <summary>Optional property name for a leading 15px jelly icon — the item property returns a
    /// GlyphIcon role name (e.g. "Bolt"). The canonical row's alternative to <see cref="DotPath"/>
    /// (spec: "status dot OR jelly icon"); tinted Text.Secondary by default. Mutually exclusive with DotPath.</summary>
    public static readonly StyledProperty<string?> GlyphPathProperty =
        AvaloniaProperty.Register<AlloySelect, string?>(nameof(GlyphPath));

    /// <summary>Optional property name supplying a per-item <see cref="IBrush"/> to tint the leading glyph
    /// (e.g. the approval modes' moss/gold/rust/slate tones); null = the default Text.Secondary tint.</summary>
    public static readonly StyledProperty<string?> GlyphTonePathProperty =
        AvaloniaProperty.Register<AlloySelect, string?>(nameof(GlyphTonePath));

    /// <summary>Overrides the chip label entirely (e.g. the VM's "from main" phrasing). Null = selected
    /// item's label, falling back to <see cref="ComboBox.PlaceholderText"/>.</summary>
    public static readonly StyledProperty<string?> TriggerTextProperty =
        AvaloniaProperty.Register<AlloySelect, string?>(nameof(TriggerText));

    /// <summary>Optional leading GlyphIcon role name on the chip (e.g. "Project").</summary>
    public static readonly StyledProperty<string?> TriggerGlyphProperty =
        AvaloniaProperty.Register<AlloySelect, string?>(nameof(TriggerGlyph));

    /// <summary>Render rows in the mono type ramp (branch names, refs, paths) — flips the :mono pseudo-class.</summary>
    public static readonly StyledProperty<bool> MonoItemsProperty =
        AvaloniaProperty.Register<AlloySelect, bool>(nameof(MonoItems));

    /// <summary>Fixed popover width. NaN (default) = spec behavior: min-width = trigger width, grows to content.</summary>
    public static readonly StyledProperty<double> PopoverWidthProperty =
        AvaloniaProperty.Register<AlloySelect, double>(nameof(PopoverWidth), double.NaN);

    public static readonly DirectProperty<AlloySelect, string> TriggerLabelProperty =
        AvaloniaProperty.RegisterDirect<AlloySelect, string>(nameof(TriggerLabel), o => o.TriggerLabel);

    public string? LabelPath { get => GetValue(LabelPathProperty); set => SetValue(LabelPathProperty, value); }
    public string? DescriptionPath { get => GetValue(DescriptionPathProperty); set => SetValue(DescriptionPathProperty, value); }
    public string? DotPath { get => GetValue(DotPathProperty); set => SetValue(DotPathProperty, value); }
    public string? GlyphPath { get => GetValue(GlyphPathProperty); set => SetValue(GlyphPathProperty, value); }
    public string? GlyphTonePath { get => GetValue(GlyphTonePathProperty); set => SetValue(GlyphTonePathProperty, value); }
    public string? TriggerText { get => GetValue(TriggerTextProperty); set => SetValue(TriggerTextProperty, value); }
    public string? TriggerGlyph { get => GetValue(TriggerGlyphProperty); set => SetValue(TriggerGlyphProperty, value); }
    public bool MonoItems { get => GetValue(MonoItemsProperty); set => SetValue(MonoItemsProperty, value); }
    public double PopoverWidth { get => GetValue(PopoverWidthProperty); set => SetValue(PopoverWidthProperty, value); }

    private string _triggerLabel = "";
    public string TriggerLabel
    {
        get => _triggerLabel;
        private set => SetAndRaise(TriggerLabelProperty, ref _triggerLabel, value);
    }

    public static readonly DirectProperty<AlloySelect, IBrush?> TriggerDotProperty =
        AvaloniaProperty.RegisterDirect<AlloySelect, IBrush?>(nameof(TriggerDot), o => o.TriggerDot);

    /// <summary>The selected value's status-dot brush (via <see cref="DotPath"/>); null = no dot / placeholder ring.</summary>
    private IBrush? _triggerDot;
    public IBrush? TriggerDot
    {
        get => _triggerDot;
        private set => SetAndRaise(TriggerDotProperty, ref _triggerDot, value);
    }

    public static readonly DirectProperty<AlloySelect, bool> HasDotProperty =
        AvaloniaProperty.RegisterDirect<AlloySelect, bool>(nameof(HasDot), o => o.HasDot);

    /// <summary>Whether this select carries the leading-dot slot at all (DotPath set).</summary>
    private bool _hasDot;
    public bool HasDot
    {
        get => _hasDot;
        private set => SetAndRaise(HasDotProperty, ref _hasDot, value);
    }

    public static readonly DirectProperty<AlloySelect, string?> TriggerGlyphEffectiveProperty =
        AvaloniaProperty.RegisterDirect<AlloySelect, string?>(nameof(TriggerGlyphEffective), o => o.TriggerGlyphEffective);

    /// <summary>The glyph the chip actually shows: the selected item's <see cref="GlyphPath"/> role when
    /// GlyphPath is set (so the trigger always matches the picked row — no parallel glyph mapping to drift),
    /// else the fixed <see cref="TriggerGlyph"/>.</summary>
    private string? _triggerGlyphEffective;
    public string? TriggerGlyphEffective
    {
        get => _triggerGlyphEffective;
        private set => SetAndRaise(TriggerGlyphEffectiveProperty, ref _triggerGlyphEffective, value);
    }

    public static readonly DirectProperty<AlloySelect, IBrush?> TriggerGlyphBrushProperty =
        AvaloniaProperty.RegisterDirect<AlloySelect, IBrush?>(nameof(TriggerGlyphBrush), o => o.TriggerGlyphBrush);

    /// <summary>Tone tint for the chip glyph (selected item's <see cref="GlyphTonePath"/>); null = inherit.</summary>
    private IBrush? _triggerGlyphBrush;
    public IBrush? TriggerGlyphBrush
    {
        get => _triggerGlyphBrush;
        private set => SetAndRaise(TriggerGlyphBrushProperty, ref _triggerGlyphBrush, value);
    }

    /// <summary>Layout room the template's popover frame reserves for the painted Menu.Shadow (popup hosts
    /// clip rendering to their content size). Mirrors Tokens.axaml "Menu.ShadowRoom" — keep in sync.</summary>
    private const double ShadowRoom = 20;
    private Avalonia.Controls.Primitives.Popup? _popup;

    public AlloySelect()
    {
        // Rows of IAlloySelectItem items show their Label even when no *Path is ever set (which is when
        // ApplyItemPresentation runs). SetCurrentValue, not a local value, so an app style's ItemTemplate wins.
        SetCurrentValue(ItemTemplateProperty, SingleLineTemplate(labelPath: null));
        UpdateTriggerLabel();
    }

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_popup is { } old) old.Opened -= OnPopupOpened;
        _popup = e.NameScope.Find<Avalonia.Controls.Primitives.Popup>("PART_Popup");
        if (_popup is { } p) p.Opened += OnPopupOpened;
    }

    // Spec: popover min-width = trigger width. The ShadowRoom margin lives inside the popup, so widen by
    // it (a plain MinWidth binding would shrink the visible card 2×room narrower than the trigger).
    private void OnPopupOpened(object? sender, EventArgs e)
    {
        if (_popup is { } p) p.MinWidth = Bounds.Width + ShadowRoom * 2;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelPathProperty || change.Property == DescriptionPathProperty ||
            change.Property == DotPathProperty || change.Property == GlyphPathProperty ||
            change.Property == GlyphTonePathProperty)
        {
            HasDot = DotPath is { Length: > 0 };
            ApplyItemPresentation();
            UpdateTriggerLabel();
        }
        else if (change.Property == SelectedItemProperty || change.Property == TriggerTextProperty ||
                 change.Property == PlaceholderTextProperty || change.Property == TriggerGlyphProperty)
        {
            UpdateTriggerLabel();
        }
        else if (change.Property == MonoItemsProperty)
        {
            PseudoClasses.Set(":mono", MonoItems);
        }
    }

    // The native item pipeline renders rows: single-line = DisplayMemberBinding (each row's implicit
    // TextBlock) or ToString; a DescriptionPath / DotPath / GlyphPath switches to a code-built template
    // (spec row anatomy: optional leading 9×9 dot OR 15px jelly icon · label · optional 11.5 caption, gap 11).
    private void ApplyItemPresentation()
    {
        var labelPath = LabelPath;
        var descPath = DescriptionPath;
        var dotPath = DotPath;
        var glyphPath = GlyphPath;
        var glyphTonePath = GlyphTonePath;
        if (descPath is { Length: > 0 } || dotPath is { Length: > 0 } || glyphPath is { Length: > 0 })
        {
            DisplayMemberBinding = null;
            ItemTemplate = new FuncDataTemplate<object?>((item, _) =>
            {
                var label = new TextBlock { Text = LabelOf(item, labelPath) };
                Control text = label;
                var twoLine = descPath is { Length: > 0 };
                if (twoLine)
                {
                    var desc = new TextBlock
                    {
                        Text = TextOf(item, Slot.Description, descPath) ?? "",
                        FontSize = 11.5,
                        FontWeight = FontWeight.Medium,
                        // Wrap inside the bounded text column (else a long line overruns the trailing check).
                        TextWrapping = TextWrapping.Wrap,
                    };
                    desc.Classes.Add("muted");
                    text = new StackPanel { Spacing = 2, Children = { label, desc } };
                }

                // Leading slot: jelly icon (GlyphPath) takes priority over a status dot (DotPath). Two-line
                // rows top-align it (spec: align flex-start, +2px optical).
                var topAlign = twoLine ? VerticalAlignment.Top : VerticalAlignment.Center;
                var topMargin = twoLine ? new Thickness(0, 2, 0, 0) : default;
                Control? leading = null;
                if (glyphPath is { Length: > 0 })
                {
                    var icon = new GlyphIcon
                    {
                        Glyph = TextOf(item, Slot.Glyph, glyphPath),
                        Size = 15,
                        VerticalAlignment = topAlign,
                        Margin = topMargin,
                    };
                    // Per-item tone brush if supplied, else the theme-safe canonical Text.Secondary tint.
                    if (glyphTonePath is { Length: > 0 } && ValueOf(item, Slot.GlyphTone, glyphTonePath) is IBrush tone)
                        icon.Foreground = tone;
                    else
                        icon.Bind(GlyphIcon.ForegroundProperty, this.GetResourceObservable("Text.Secondary"));
                    leading = icon;
                }
                else if (dotPath is { Length: > 0 })
                {
                    leading = new Ellipse
                    {
                        Width = 9, Height = 9,
                        Fill = ValueOf(item, Slot.Dot, dotPath) as IBrush,
                        VerticalAlignment = topAlign,
                        Margin = twoLine ? new Thickness(0, 4, 0, 0) : default,
                    };
                }
                if (leading is null) return text;

                // A Grid (not a horizontal StackPanel) so the text column is width-bounded and the
                // description actually wraps instead of bleeding under the row's trailing check.
                text.Margin = new Thickness(11, 0, 0, 0);   // the gap the horizontal stack used to supply
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                Grid.SetColumn(text, 1);
                row.Children.Add(leading);
                row.Children.Add(text);
                return row;
            }, supportsRecycling: true);
        }
        else
        {
            DisplayMemberBinding = null;
            ItemTemplate = SingleLineTemplate(labelPath);
        }
    }

    /// <summary>Single-line rows: an <see cref="IAlloySelectItem"/>'s Label, else the
    /// <paramref name="labelPath"/> property, bound live as DisplayMemberBinding did. Only the items it
    /// matches are templated: anything else (no LabelPath, not an IAlloySelectItem) falls back to the
    /// default presenter — ToString, or the item itself if it is a control — exactly as it did with no
    /// DisplayMemberBinding.</summary>
    private static FuncDataTemplate<object?> SingleLineTemplate(string? labelPath) => new(
        item => item is IAlloySelectItem || labelPath is { Length: > 0 },
        (item, _) => item is IAlloySelectItem selectItem
            ? new TextBlock { Text = selectItem.Label }
            : new TextBlock { [!TextBlock.TextProperty] = LabelBinding(labelPath!) },
        supportsRecycling: true);

    private enum Slot { Description, Dot, Glyph, GlyphTone }

    /// <summary>An item's value for one slot: from <see cref="IAlloySelectItem"/> when the item is one,
    /// else the property <paramref name="path"/> names. Null when the slot is off (no path).</summary>
    private static object? ValueOf(object? item, Slot slot, string? path)
    {
        if (item is null || path is not { Length: > 0 }) return null;
        if (item is IAlloySelectItem selectItem)
        {
            return slot switch
            {
                Slot.Description => selectItem.Description,
                Slot.Dot => selectItem.Dot,
                Slot.Glyph => selectItem.Glyph,
                _ => selectItem.GlyphTone,
            };
        }
        return PropertyValue(item, path);
    }

    private static string? TextOf(object? item, Slot slot, string? path) => ValueOf(item, slot, path)?.ToString();

    /// <summary>The label for a row or the chip: <see cref="IAlloySelectItem.Label"/>, else the
    /// <paramref name="path"/> property, else <c>ToString()</c>.</summary>
    private static string LabelOf(object? item, string? path) => item switch
    {
        null => "",
        IAlloySelectItem selectItem => selectItem.Label,
        _ when path is { Length: > 0 } => PropertyValue(item, path)?.ToString() ?? item.ToString() ?? "",
        _ => item.ToString() ?? "",
    };

    // The two places a *Path name is resolved by reflection. Items that are IAlloySelectItem never get
    // here; the *Path properties document that they need reflection, and the type says which to use.
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification =
        "A *Path names an item property, which only reflection can read; IAlloySelectItem is the trim-safe alternative.")]
    private static object? PropertyValue(object item, string path) =>
        item.GetType().GetProperty(path)?.GetValue(item);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification =
        "LabelPath names an item property, which only a reflection binding can follow; IAlloySelectItem is the trim-safe alternative.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification =
        "LabelPath names an item property, which only a reflection binding can follow; IAlloySelectItem is the trim-safe alternative.")]
    private static Binding LabelBinding(string path) => new(path);

    private void UpdateTriggerLabel()
    {
        var sel = SelectedItem;
        TriggerLabel = TriggerText ?? (sel is not null ? LabelOf(sel, LabelPath) : PlaceholderText ?? "Select…");
        TriggerDot = ValueOf(sel, Slot.Dot, DotPath) as IBrush;
        // Chip glyph follows the selected row when GlyphPath drives per-item icons (no drift vs the rows),
        // else the fixed TriggerGlyph; tone from GlyphTonePath.
        if (GlyphPath is { Length: > 0 } && sel is not null)
        {
            TriggerGlyphEffective = TextOf(sel, Slot.Glyph, GlyphPath);
            TriggerGlyphBrush = ValueOf(sel, Slot.GlyphTone, GlyphTonePath) as IBrush;
        }
        else
        {
            TriggerGlyphEffective = TriggerGlyph;
            TriggerGlyphBrush = null;
        }
        // Placeholder state (spec): no value AND no custom trigger phrasing → muted label + ring dot.
        PseudoClasses.Set(":placeholder", sel is null && TriggerText is null);
    }
}
