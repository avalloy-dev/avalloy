using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Avalloy.Views.Controls;

/// <summary>
/// The labeled multi-line field (see AlloyTextArea.axaml): optional Title + Description over an
/// <see cref="AlloyTextBox"/> in its <c>area</c> variant, so every textarea shares the one v7 field
/// treatment. Bind <see cref="Text"/> two-way; attach a commit handler to this control's bubbled
/// <c>LostFocus</c> to persist on blur. <see cref="Mono"/> = line-oriented lists (mono font, no
/// wrapping) — allow/deny patterns, path lists; leave off for prose.
/// </summary>
public partial class AlloyTextArea : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<AlloyTextArea, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<AlloyTextArea, string?>(nameof(Description));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<AlloyTextArea, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> PlaceholderProperty =
        AvaloniaProperty.Register<AlloyTextArea, string?>(nameof(Placeholder));

    /// <summary>Mono value font + no wrapping — for one-entry-per-line lists.</summary>
    public static readonly StyledProperty<bool> MonoProperty =
        AvaloniaProperty.Register<AlloyTextArea, bool>(nameof(Mono));

    public static readonly StyledProperty<double> FieldMinHeightProperty =
        AvaloniaProperty.Register<AlloyTextArea, double>(nameof(FieldMinHeight), 64);

    public static readonly StyledProperty<double> FieldMaxHeightProperty =
        AvaloniaProperty.Register<AlloyTextArea, double>(nameof(FieldMaxHeight), double.PositiveInfinity);

    /// <summary>Bold field label above (e.g. "Always allow"); empty collapses the row.</summary>
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>Muted helper line under the title; empty collapses the row.</summary>
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    /// <summary>The field text (two-way).</summary>
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <summary>Placeholder shown when empty.</summary>
    public string? Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    public bool Mono { get => GetValue(MonoProperty); set => SetValue(MonoProperty, value); }

    /// <summary>The inner field's min height (the label rows don't count).</summary>
    public double FieldMinHeight { get => GetValue(FieldMinHeightProperty); set => SetValue(FieldMinHeightProperty, value); }

    /// <summary>The inner field's max height — it scrolls beyond this.</summary>
    public double FieldMaxHeight { get => GetValue(FieldMaxHeightProperty); set => SetValue(FieldMaxHeightProperty, value); }

    private readonly AlloyTextBox? _field;

    public AlloyTextArea()
    {
        AvaloniaXamlLoader.Load(this);
        _field = this.FindControl<AlloyTextBox>("Field");
        SyncMono();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MonoProperty) SyncMono();
    }

    private void SyncMono()
    {
        if (_field is not { } field) return;
        if (Mono) { if (!field.Classes.Contains("monolines")) field.Classes.Add("monolines"); }
        else field.Classes.Remove("monolines");
    }
}
