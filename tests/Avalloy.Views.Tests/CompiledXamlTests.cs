using Avalonia.Headless;
using Avalloy.Controls.Tabs;
using Avalloy.Icons;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Views.Tests;

/// <summary>
/// XAML that Avalloy now constructs as a compiled class instead of loading by URI at runtime — which trimming
/// and Native AOT cannot follow. These build each one on the headless platform (its glyph geometry needs a
/// render interface, which Avalloy.Desktop.Tests deliberately has none of) and check it holds what the URI did.
/// </summary>
[TestClass]
public class CompiledXamlTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Get();

    [TestMethod]
    public Task The_Lucide_Pack_Builds_Its_Glyphs_Without_A_Uri() => Session.Dispatch(() =>
    {
        var icons = AvalloyIconPacks.Lucide.CompiledResources!("lucide");

        icons.Should().NotBeNull("the default pack must not need its URI");
        icons!.TryGetResource("Icon.check", null, out var slug).Should().BeTrue("a slug baked from the SVGs");
        ((IconGlyph)slug!).Geometry.Should().NotBeNull();
        icons.TryGetResource("Icon.Chevron", null, out var role).Should().BeTrue("a curated role alias");
        ((IconGlyph)role!).Geometry.Should().NotBeNull();
        AvalloyIconPacks.Lucide.CompiledResources!("other").Should().BeNull("any other id falls back to its URI");
    }, CancellationToken.None);

    [TestMethod]
    public Task The_Tab_Strip_Styles_Build_Without_A_Uri() => Session.Dispatch(() =>
    {
        // What the drag ghost attaches, since it is parented outside the strip's own style scope.
        var styles = new TabStripStyles();

        styles.Should().NotBeEmpty("the strip's styles, compiled from TabStripStyles.axaml");
    }, CancellationToken.None);
}
