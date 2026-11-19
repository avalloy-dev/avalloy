using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Avalloy.Themes;

/// <summary>Slim scrollbars over Fluent's own ScrollBar (SlimScrollBars.axaml), as a compiled class, so an app can
/// add them without loading XAML by URI at runtime, which trimming and Native AOT cannot follow. Add it by this class:
/// <c>&lt;themes:SlimScrollBars/&gt;</c> in XAML or <c>Styles.Add(new SlimScrollBars())</c>. A StyleInclude by
/// avares:// URI does not compile from another assembly, because the sheet is x:Class.</summary>
public sealed partial class SlimScrollBars : Styles
{
    public SlimScrollBars() => AvaloniaXamlLoader.Load(this);
}
