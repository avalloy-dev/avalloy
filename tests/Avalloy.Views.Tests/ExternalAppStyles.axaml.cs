using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Avalloy.Views.Tests;

/// <summary>ExternalAppStyles.axaml as a class: an app's sheet that includes SlimScrollBars from outside its assembly.</summary>
public sealed partial class ExternalAppStyles : Styles
{
    public ExternalAppStyles() => AvaloniaXamlLoader.Load(this);
}
