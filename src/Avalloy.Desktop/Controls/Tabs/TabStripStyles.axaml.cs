using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Avalloy.Controls.Tabs;

/// <summary>The TabStrip's styles as a compiled class, so the drag ghost can attach them
/// without loading XAML by URI at runtime, which trimming and Native AOT cannot follow.</summary>
internal sealed partial class TabStripStyles : Styles
{
    public TabStripStyles() => AvaloniaXamlLoader.Load(this);
}
