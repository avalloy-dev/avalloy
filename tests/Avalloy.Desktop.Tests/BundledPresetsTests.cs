using AwesomeAssertions;
using Avalloy.Preferences;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Desktop.Tests;

/// <summary>
/// The bundled presets stay in Avalloy.Desktop while <see cref="PresetCatalog"/> moved to Avalloy.Base, so the
/// assembly an app names is the one that holds the JSON, not the one that reads it. A catalog pointed at the wrong
/// one is not an error, only empty: the app would start with no theme and nothing would say why.
/// </summary>
[TestClass]
public class BundledPresetsTests
{
    [TestMethod]
    public void The_Bundled_Themes_And_Densities_Load_From_The_Desktop_Assembly()
    {
        var themes = PresetCatalog.Load(typeof(AvalloyApp).Assembly, "Avalloy.Presets.themes.");
        var densities = PresetCatalog.Load(typeof(AvalloyApp).Assembly, "Avalloy.Presets.densities.");

        themes.GetTheme("avalloy-light").Should().NotBeNull();
        themes.GetTheme("avalloy-dark").Should().NotBeNull();
        densities.GetDensity("compact").Should().NotBeNull();
        densities.GetDensity("normal").Should().NotBeNull();
        densities.GetDensity("spacious").Should().NotBeNull();
    }
}
