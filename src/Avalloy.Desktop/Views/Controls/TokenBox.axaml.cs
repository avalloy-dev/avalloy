using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;
using Avalloy.Text;

namespace Avalloy.Views.Controls;

/// <summary>
/// A text field with trigger-char token autocomplete. Wraps an
/// <see cref="AutoCompleteBox"/> whose filter/selector are driven by
/// <see cref="TokenCompletion"/>: a dropdown of matching tokens appears once the
/// user types <see cref="TriggerChar"/> (default <c>$</c>), and picking one
/// splices <c>{trigger}TOKEN{suffix}</c> over the typed fragment.
///
/// Reusable across contexts: path fields pass <see cref="InsertSuffix"/> = "/",
/// other consumers (e.g. prompt templates) leave it empty and may pick a
/// different <see cref="TriggerChar"/>. The bare token-name list is injected via
/// <see cref="TokenItems"/>; this control owns no token source of its own.
/// </summary>
public partial class TokenBox : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<TokenBox, string?>(
            nameof(Text), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<IEnumerable?> TokenItemsProperty =
        AvaloniaProperty.Register<TokenBox, IEnumerable?>(nameof(TokenItems));

    public static readonly StyledProperty<string?> PlaceholderProperty =
        AvaloniaProperty.Register<TokenBox, string?>(nameof(Placeholder));

    /// <summary>The character that opens the token dropdown when typed. Default <c>$</c>.</summary>
    public static readonly StyledProperty<char> TriggerCharProperty =
        AvaloniaProperty.Register<TokenBox, char>(nameof(TriggerChar), defaultValue: '$');

    /// <summary>Text appended after the spliced token on pick. Path fields use "/";
    /// default is empty.</summary>
    public static readonly StyledProperty<string> InsertSuffixProperty =
        AvaloniaProperty.Register<TokenBox, string>(nameof(InsertSuffix), defaultValue: "");

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public IEnumerable? TokenItems { get => GetValue(TokenItemsProperty); set => SetValue(TokenItemsProperty, value); }
    public string? Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public char TriggerChar { get => GetValue(TriggerCharProperty); set => SetValue(TriggerCharProperty, value); }
    public string InsertSuffix { get => GetValue(InsertSuffixProperty); set => SetValue(InsertSuffixProperty, value); }

    public TokenBox()
    {
        AvaloniaXamlLoader.Load(this);
        var inner = this.FindControl<AutoCompleteBox>("Inner")!;
        // Show a token only while a trigger-fragment is being typed and it prefix-matches.
        inner.ItemFilter = (search, item) => item is string s && TokenCompletion.ItemMatches(search, s, TriggerChar);
        // On pick, splice "{trigger}token{suffix}" over the typed fragment.
        inner.TextSelector = (search, item) =>
            TokenCompletion.Apply(search, item as string ?? string.Empty, TriggerChar, InsertSuffix);
        // Display each item with the live trigger prefix (e.g. "$HOME").
        inner.ItemTemplate = new FuncDataTemplate<string>(
            (name, _) => new TextBlock { Text = TriggerChar + (name ?? string.Empty) },
            supportsRecycling: false);
    }
}
