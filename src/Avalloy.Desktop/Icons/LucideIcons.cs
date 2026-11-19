using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Avalloy.Icons;

/// <summary>The baked Lucide dictionary (<c>Assets/iconpacks/lucide/lucide.icons.axaml</c>,
/// compiled to this class by the bake), so <see cref="AvalloyIconPacks.Lucide"/> constructs
/// it instead of loading the XAML by URI at runtime.</summary>
internal sealed partial class LucideIcons : ResourceDictionary
{
    public LucideIcons() => AvaloniaXamlLoader.Load(this);
}
