using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Views.Tests;

/// <summary>
/// The native theme's dictionaries were Avalloy.Themes' until 0.23.0 and are Avalloy.Desktop's now, so every app
/// that includes them names them by an <c>avares://Avalloy.Desktop/Themes/</c> URI, in XAML and in the
/// <c>ThemeDescriptor</c> strings <c>NativeThemeController</c> loads at runtime. A URI that stops resolving fails
/// only when an app runs, so these load each one by the URI apps are told to use.
/// </summary>
[TestClass]
public class NativeThemeXamlTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Get();

    [TestMethod]
    [DataRow("Tokens.axaml", "Font.Ui")]
    [DataRow("Template.Light.axaml", "Surface.Window.Color")]
    [DataRow("Template.Dark.axaml", "Surface.Window.Color")]
    public Task A_Theme_Dictionary_Loads_From_Desktop(string file, string key) => Session.Dispatch(() =>
    {
        var loaded = AvaloniaXamlLoader.Load(new Uri($"avares://Avalloy.Desktop/Themes/{file}"));

        loaded.Should().BeOfType<ResourceDictionary>();
        ((ResourceDictionary)loaded).ContainsKey(key).Should().BeTrue($"{file} defines {key}");
    }, CancellationToken.None);

    [TestMethod]
    public Task The_Control_Themes_Load_From_Desktop() => Session.Dispatch(() =>
    {
        var controls = AvaloniaXamlLoader.Load(new Uri("avares://Avalloy.Desktop/Themes/AlloyControls.axaml"));

        controls.Should().BeOfType<ResourceDictionary>().Which.Should().NotBeEmpty();
    }, CancellationToken.None);

    [TestMethod]
    [DataRow("Type.axaml")]
    [DataRow("ControlStyles.axaml")]
    public Task A_Style_Sheet_Loads_From_Desktop(string file) => Session.Dispatch(() =>
    {
        var loaded = AvaloniaXamlLoader.Load(new Uri($"avares://Avalloy.Desktop/Themes/{file}"));

        loaded.Should().BeOfType<Styles>().Which.Should().NotBeEmpty();
    }, CancellationToken.None);
}
