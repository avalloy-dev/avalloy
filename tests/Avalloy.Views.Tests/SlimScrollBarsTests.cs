using Avalloy.Themes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Views.Tests;

/// <summary>
/// <see cref="SlimScrollBars"/> over Fluent's ScrollBar: the steppers are gone and the thumb keeps its slim
/// transform even when the bar is expanded (always-expanded, or an auto-hide bar the pointer expanded), and the two
/// ScrollViewer classes reach the bars inside its template.
/// </summary>
[TestClass]
public class SlimScrollBarsTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Get();

    private static (Window Window, ScrollViewer Scroller) Show(params string[] classes) =>
        Show(new SlimScrollBars(), classes);

    private static (Window Window, ScrollViewer Scroller) Show(Styles sheet, params string[] classes)
    {
        var scroller = new ScrollViewer
        {
            Width = 200, Height = 100,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Width = 1000, Height = 1000 },
        };
        scroller.Classes.AddRange(classes);
        var window = new Window { Width = 300, Height = 200, Content = scroller };
        window.Styles.Add(sheet);
        // Stock Fluent gives the strip the same transparent brush at rest and under the pointer, so the rule that
        // holds the resting one would change nothing to see. A theme that colors the pointer-over strip is what the
        // rule is for; this stands in for one.
        window.Resources["ScrollBarBackgroundPointerOver"] = Brushes.Red;
        window.Show();
        HeadlessSession.Settle(window);
        return (window, scroller);
    }

    private static ScrollBar Bar(ScrollViewer scroller, Orientation orientation) =>
        scroller.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == orientation);

    // Fluent animates the thumb's RenderTransform and the track's Opacity, and the headless clock never runs an
    // animation, so a part left with its transitions would still read its resting value whatever the expanded
    // styles said.
    private static void StopAnimations(ScrollViewer scroller)
    {
        foreach (var t in scroller.GetVisualDescendants().OfType<Thumb>()) t.Transitions = null;
        foreach (var r in scroller.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>()) r.Transitions = null;
    }

    /// <summary>What the slim sheet holds an expanded bar to: Fluent's RESTING values, where its own expanded
    /// styles would put steppers, a pointer-over strip, a filled track and a full-width thumb.</summary>
    private static void ShouldWearTheSlimDressing(Window window, ScrollBar bar, Orientation orientation)
    {
        bar.IsExpanded.Should().BeTrue("the dressing under test is the expanded one");

        bar.GetVisualDescendants().OfType<RepeatButton>()
            .Where(b => b.Name is "PART_LineUpButton" or "PART_LineDownButton")
            .Should().NotBeEmpty().And.OnlyContain(b => !b.IsVisible, "the steppers never show");

        window.TryFindResource("ScrollBarBackground", window.ActualThemeVariant, out var rest).Should().BeTrue();
        var root = bar.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "Root");
        ((ISolidColorBrush)root.Background!).Color.Should().Be(((ISolidColorBrush)rest!).Color,
            "the strip keeps its resting background, not Fluent's pointer-over one");
        bar.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>().Single(r => r.Name == "TrackRect")
            .Opacity.Should().Be(0, "the track stays unfilled");

        var key = orientation == Orientation.Vertical
            ? "VerticalSmallScrollThumbScaleTransform" : "HorizontalSmallScrollThumbScaleTransform";
        window.TryFindResource(key, window.ActualThemeVariant, out var slim).Should().BeTrue();
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        thumb.RenderTransform.Should().NotBeNull();
        thumb.RenderTransform!.Value.Should().Be(((ITransform)slim!).Value, "the thumb keeps Fluent's small-thumb scale");
    }

    [TestMethod]
    public Task An_Expanded_Bar_Keeps_The_Slim_Dressing() => Session.Dispatch(() =>
    {
        // IsExpanded is read-only. With auto-hide off, the bar is always expanded.
        var (window, scroller) = Show();
        StopAnimations(scroller);
        scroller.AllowAutoHide = false;
        HeadlessSession.Settle(window);

        foreach (var orientation in new[] { Orientation.Vertical, Orientation.Horizontal })
            ShouldWearTheSlimDressing(window, Bar(scroller, orientation), orientation);
        window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task An_Autohide_Bar_The_Pointer_Expands_Keeps_The_Slim_Dressing() => Session.Dispatch(() =>
    {
        // The owner says no, so the bars auto-hide only because of the class.
        var (window, scroller) = Show("autohide");
        StopAnimations(scroller);
        scroller.AllowAutoHide = false;
        HeadlessSession.Settle(window);

        foreach (var orientation in new[] { Orientation.Vertical, Orientation.Horizontal })
        {
            var bar = Bar(scroller, orientation);
            bar.AllowAutoHide.Should().BeTrue();
            bar.IsExpanded.Should().BeFalse("an auto-hide bar the pointer is not on rests collapsed");

            // Onto the thumb's DRAWN center: at rest it is scaled to a sliver, and hit testing follows the transform.
            var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
            window.MouseMove(thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value);
            HeadlessSession.Settle(window);
            bar.IsPointerOver.Should().BeTrue();

            // A hovered auto-hide bar expands from Fluent's ShowDelay DispatcherTimer, on real time. Making the bar
            // re-read AllowAutoHide lands in the same state now (ScrollBar.UpdateIsExpandedState sets IsExpanded to
            // IsPointerOver); clearing the local value hands AllowAutoHide back to the autohide class.
            bar.AllowAutoHide = false;
            bar.ClearValue(ScrollBar.AllowAutoHideProperty);
            HeadlessSession.Settle(window);
            bar.AllowAutoHide.Should().BeTrue("the class still says auto-hide");

            ShouldWearTheSlimDressing(window, bar, orientation);
        }
        window.Close();
    }, CancellationToken.None);

    /// <summary>The way the docs give for an app: by the class, from the app's own compiled XAML
    /// (ExternalAppStyles.axaml, in this assembly). That file compiling at all is half the check; the sheet it builds
    /// then has to reach a ScrollViewer's bars.</summary>
    [TestMethod]
    public Task Included_By_Class_From_Another_Assemblys_Xaml_It_Applies() => Session.Dispatch(() =>
    {
        var (window, scroller) = Show(new ExternalAppStyles(), "autohide");
        scroller.AllowAutoHide = false;
        HeadlessSession.Settle(window);

        Bar(scroller, Orientation.Vertical).AllowAutoHide.Should().BeTrue("the sheet the app's XAML included is live");
        window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task Autohide_Sets_AllowAutoHide_On_The_Bars_Themselves() => Session.Dispatch(() =>
    {
        // The owner says no, so only the class can be what turns it on.
        var (window, scroller) = Show("autohide");
        scroller.AllowAutoHide = false;
        HeadlessSession.Settle(window);

        Bar(scroller, Orientation.Vertical).AllowAutoHide.Should().BeTrue();
        Bar(scroller, Orientation.Horizontal).AllowAutoHide.Should().BeTrue();
        window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task Hiddenbars_Keeps_The_Bar_Logically_There_But_Invisible_And_Clickthrough() => Session.Dispatch(() =>
    {
        var (window, scroller) = Show("hiddenbars");

        var bar = Bar(scroller, Orientation.Vertical);
        bar.IsVisible.Should().BeTrue("Hidden visibility is what stalls frames; the bar stays Auto");
        bar.Opacity.Should().Be(0);
        bar.IsHitTestVisible.Should().BeFalse();
        window.Close();
    }, CancellationToken.None);
}
