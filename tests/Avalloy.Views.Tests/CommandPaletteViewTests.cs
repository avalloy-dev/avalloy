using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalloy.Commands;
using Avalloy.Icons;
using Avalonia.Media;
using Avalloy.Views.Controls;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Views.Tests;

/// <summary>
/// <see cref="CommandPaletteView"/> laid out in a real (headless) window: what only exists once the control
/// is arranged, focused and keyed. What a key DOES is the view model's (CommandPaletteViewModelTests in
/// Avalloy.Base.Tests); here it is that the key reaches it from wherever focus is.
/// </summary>
[TestClass]
public class CommandPaletteViewTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Get();

    private sealed record Shown(Window Window, CommandPaletteView View, CommandPaletteViewModel Vm, List<string> Ran)
    {
        public ListBox List => View.FindControl<ListBox>("List")!;
        public TextBox Search => View.FindControl<TextBox>("Search")!;
        public Control Card => View.GetVisualDescendants().OfType<OverlayCard>().Single();
        public ScrollViewer Scroller => List.GetVisualDescendants().OfType<ScrollViewer>().First();

        public void Key(PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.KeyPressQwerty(physical, modifiers);
            Window.KeyReleaseQwerty(physical, modifiers);
            HeadlessSession.Settle(Window);
        }

        public ListBoxItem Row(int index) => (ListBoxItem)List.ContainerFromIndex(index)!;
        public GlyphIcon GlyphOf(int index) => Row(index).GetVisualDescendants().OfType<GlyphIcon>().Single();
        public TextBlock HintOf(int index) =>
            Row(index).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("mono"));
    }

    private static Shown Show(double width, IEnumerable<PaletteItem>? items = null, int rows = 3,
        PaletteOptions? options = null, Action<CommandPaletteView>? configure = null, Action<Window>? window = null)
    {
        var ran = new List<string>();
        var list = (items ?? Enumerable.Range(0, rows).Select(i => new PaletteItem { Label = $"Row {i}", Run = () => { } }))
            .Select(i => new PaletteItem
            {
                Label = i.Label, IsEnabled = i.IsEnabled, Note = i.Note, Hints = i.Hints,
                Run = () => ran.Add(i.Label),
            })
            .ToList();
        var vm = CommandPaletteViewModel.Create(list, () => { }, options ?? new PaletteOptions { MaxResults = 1000 });
        var view = new CommandPaletteView { DataContext = vm };
        configure?.Invoke(view);
        var host = new Window { Width = width, Height = 900, Content = view };
        window?.Invoke(host);
        host.Show();
        HeadlessSession.Settle(host);
        return new Shown(host, view, vm, ran);
    }

    private static Rect BoundsIn(Visual visual, Visual ancestor) =>
        new(visual.TranslatePoint(default, ancestor)!.Value, visual.Bounds.Size);

    // ── fit to width ─────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public Task In_A_Wide_Window_The_Card_Is_640_Wide_And_Centred() => Session.Dispatch(() =>
    {
        var s = Show(width: 1000);

        var card = BoundsIn(s.Card, s.Window);
        card.Width.Should().Be(640, "as wide as it always was");
        card.X.Should().Be(180, "centred: (1000 - 640) / 2");
        s.Window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task In_A_Narrow_Window_The_Card_Shrinks_Inside_It() => Session.Dispatch(() =>
    {
        var s = Show(width: 400);

        var card = BoundsIn(s.Card, s.Window);
        card.Width.Should().Be(352, "the window less a 24px margin each side, rather than 640 spilling past both edges");
        card.X.Should().Be(24);
        s.Window.Close();
    }, CancellationToken.None);

    // ── keys from wherever focus is ──────────────────────────────────────────────────────────────

    [TestMethod]
    public Task Arrows_From_The_Search_Box_Move_The_Selection() => Session.Dispatch(() =>
    {
        var s = Show(width: 1000);
        s.Search.IsFocused.Should().BeTrue("the palette lands in the search box");

        s.Key(PhysicalKey.ArrowDown);
        s.Vm.SelectedIndex.Should().Be(1);
        s.Key(PhysicalKey.ArrowUp);
        s.Key(PhysicalKey.ArrowUp);
        s.Vm.SelectedIndex.Should().Be(2, "↑ from the top wraps");
        s.Window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task Arrows_Keep_Working_Once_Focus_Is_In_The_List_And_Still_Wrap() => Session.Dispatch(() =>
    {
        var s = Show(width: 1000);
        s.Vm.Move(+2);
        HeadlessSession.Settle(s.Window);
        s.List.ContainerFromIndex(2)!.Focus();
        HeadlessSession.Settle(s.Window);
        s.Search.IsFocused.Should().BeFalse("focus has left the search box for the list");

        s.Key(PhysicalKey.ArrowDown);

        s.Vm.SelectedIndex.Should().Be(0, "the palette wraps; the ListBox's own ↓ would stop on the last row");
        s.Window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task Arrows_In_The_List_Step_Over_Disabled_Rows() => Session.Dispatch(() =>
    {
        var s = Show(width: 1000, items:
        [
            new PaletteItem { Label = "go one", Run = () => { } },
            new PaletteItem { Label = "go two", Run = () => { } },
            new PaletteItem { Label = "off", IsEnabled = false, Run = () => { } },
        ]);
        s.Vm.Move(+1);
        HeadlessSession.Settle(s.Window);
        s.List.ContainerFromIndex(1)!.Focus();
        HeadlessSession.Settle(s.Window);

        s.Key(PhysicalKey.ArrowDown);

        s.Vm.SelectedIndex.Should().Be(0, "the disabled last row is stepped over, back round to the top");
        s.Window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task Enter_In_The_List_Runs_The_Selected_Row() => Session.Dispatch(() =>
    {
        var s = Show(width: 1000);
        s.Vm.Move(+1);
        HeadlessSession.Settle(s.Window);
        s.List.ContainerFromIndex(1)!.Focus();
        HeadlessSession.Settle(s.Window);

        s.Key(PhysicalKey.Enter);

        s.Ran.Should().Equal(["Row 1"]);
        s.Window.Close();
    }, CancellationToken.None);

    // ── scroll into view ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public Task Moving_The_Selection_Past_The_Bottom_Scrolls_It_Into_View() => Session.Dispatch(() =>
    {
        var s = Show(width: 1000, rows: 40);
        s.Scroller.Offset.Y.Should().Be(0);

        s.Key(PhysicalKey.ArrowUp); // wraps to the last of 40 rows, far below a 420px list

        s.Vm.SelectedIndex.Should().Be(39);
        s.Scroller.Offset.Y.Should().BeGreaterThan(0);
        var row = BoundsIn(s.List.ContainerFromIndex(39)!, s.Scroller);
        row.Top.Should().BeGreaterThanOrEqualTo(-0.5);
        row.Bottom.Should().BeLessThanOrEqualTo(s.Scroller.Viewport.Height + 0.5, "the selected row is on screen");
        s.Window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task Narrowing_The_Query_Brings_The_Top_Row_Back_Into_View() => Session.Dispatch(() =>
    {
        var s = Show(width: 1000, rows: 40);
        s.Key(PhysicalKey.ArrowUp);
        s.Scroller.Offset.Y.Should().BeGreaterThan(0);

        s.Vm.Query = "Row";
        HeadlessSession.Settle(s.Window);

        s.Vm.SelectedIndex.Should().Be(0);
        // Back near the top, not still ~40 rows down. (The row is brought to the viewport's edge, so the
        // list's few pixels of top padding stay scrolled off: offset 7, not 0.)
        var row = BoundsIn(s.List.ContainerFromIndex(0)!, s.Scroller);
        row.Top.Should().BeGreaterThanOrEqualTo(-0.5, "the selection went back to the first row, so the view follows it");
        row.Bottom.Should().BeLessThanOrEqualTo(s.Scroller.Viewport.Height + 0.5);
        s.Window.Close();
    }, CancellationToken.None);

    // ── accessible names ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public Task The_Search_Box_And_The_List_Have_Names() => Session.Dispatch(() =>
    {
        var s = Show(width: 1000);

        AutomationProperties.GetName(s.Search).Should().Be(CommandPaletteViewModel.GlobalPlaceholder,
            "the box is named for what it searches, which is what the placeholder says");
        ControlAutomationPeer.CreatePeerForElement(s.List).GetName().Should().Be("Results");
        s.Window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task A_Row_Is_Read_By_Its_Accessible_Name_And_A_Disabled_One_As_Unavailable() => Session.Dispatch(() =>
    {
        var s = Show(width: 1000, items:
        [
            new PaletteItem { Label = "Split", Hints = ["⌘D", "⌃A H", "⌃A V"], Run = () => { } },
            new PaletteItem { Label = "Detach", IsEnabled = false, Note = "not yet", Run = () => { } },
        ]);

        var split = ControlAutomationPeer.CreatePeerForElement(s.List.ContainerFromIndex(0)!);
        var detach = ControlAutomationPeer.CreatePeerForElement(s.List.ContainerFromIndex(1)!);

        split.GetName().Should().Be("Split, ⌘D, ⌃A H, ⌃A V");
        split.IsEnabled().Should().BeTrue();
        detach.GetName().Should().Be("Detach, not yet");
        detach.IsEnabled().Should().BeFalse();
        s.Window.Close();
    }, CancellationToken.None);

    // ── look and naming options ──────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<PaletteItem> Hinted =
        [new PaletteItem { Label = "Split", Hints = ["⌘D"], Run = () => { } }];

    [TestMethod]
    public Task By_Default_A_Row_Looks_As_It_Always_Has() => Session.Dispatch(() =>
    {
        var ghost = new SolidColorBrush(Colors.Gray);
        var s = Show(width: 1000, items: Hinted, window: w => w.Resources["Text.Ghost"] = ghost);

        s.GlyphOf(0).IsEffectivelyVisible.Should().BeTrue();
        s.Row(0).Bounds.Height.Should().Be(40);
        s.HintOf(0).FontSize.Should().Be(11);
        s.HintOf(0).Foreground.Should().BeSameAs(ghost, "the hint follows the theme's Text.Ghost token");
        ControlAutomationPeer.CreatePeerForElement(s.List).GetName().Should().Be("Results");
        s.Window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task A_Long_Label_Stops_A_Gap_Short_Of_The_Hints() => Session.Dispatch(() =>
    {
        var s = Show(width: 400, items:
        [
            new PaletteItem
            {
                Label = "A label far too long to fit beside its key hints in a narrow window",
                Hints = ["⌘D"], Run = () => { },
            },
        ]);

        var label = s.Row(0).GetVisualDescendants().OfType<TextBlock>().Single(t => t.TextTrimming == TextTrimming.CharacterEllipsis);
        var hint = BoundsIn(s.HintOf(0), s.Row(0));
        BoundsIn(label, s.Row(0)).Right.Should().BeLessThanOrEqualTo(hint.Left - 12,
            "the trimmed label's ellipsis keeps the same 12px from the hints that the note gets");
        s.Window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task A_Row_Without_Hints_Gives_The_Label_The_Whole_Width() => Session.Dispatch(() =>
    {
        var s = Show(width: 1000, items: [new PaletteItem { Label = "Plain", Run = () => { } }]);

        s.HintOf(0).IsVisible.Should().BeFalse("an empty hint takes no room, so its gap is not left behind");
        s.Window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task Glyphs_Can_Be_Hidden() => Session.Dispatch(() =>
    {
        var s = Show(width: 1000, items: Hinted, configure: v => v.ShowGlyphs = false);

        s.GlyphOf(0).IsEffectivelyVisible.Should().BeFalse();
        s.Window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task The_Row_Height_Can_Be_Set() => Session.Dispatch(() =>
    {
        var s = Show(width: 1000, items: Hinted, configure: v => v.RowHeight = 36);

        s.Row(0).Bounds.Height.Should().Be(36);
        s.Window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task The_Hint_Size_And_Brush_Can_Be_Set() => Session.Dispatch(() =>
    {
        var muted = new SolidColorBrush(Colors.Red);
        var s = Show(width: 1000, items: Hinted, configure: v =>
        {
            v.HintFontSize = 12;
            v.HintForeground = muted;
        }, window: w => w.Resources["Text.Ghost"] = Brushes.Gray);

        s.HintOf(0).FontSize.Should().Be(12);
        s.HintOf(0).Foreground.Should().BeSameAs(muted, "a local value beats the default style's token");
        s.Window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task The_Hint_Brush_Can_Follow_Another_Theme_Token() => Session.Dispatch(() =>
    {
        var muted = new SolidColorBrush(Colors.Blue);
        var s = Show(width: 1000, items: Hinted,
            configure: v => v.Bind(CommandPaletteView.HintForegroundProperty, v.GetResourceObservable("Text.Muted")),
            window: w =>
            {
                w.Resources["Text.Ghost"] = Brushes.Gray;
                w.Resources["Text.Muted"] = muted;
            });

        s.HintOf(0).Foreground.Should().BeSameAs(muted);
        s.Window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task The_List_Can_Be_Named_For_What_It_Lists() => Session.Dispatch(() =>
    {
        var s = Show(width: 1000, configure: v => v.ListName = "Commands");

        ControlAutomationPeer.CreatePeerForElement(s.List).GetName().Should().Be("Commands");
        s.Window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task A_Modified_Arrow_Moves_The_Selection_By_Default() => Session.Dispatch(() =>
    {
        var s = Show(width: 1000);

        s.Key(PhysicalKey.ArrowDown, RawInputModifiers.Shift);

        s.Vm.SelectedIndex.Should().Be(1, "the palette has always claimed ↑↓ whatever modifiers are held");
        s.Window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task With_IgnoreModifiedKeys_A_Modified_Arrow_Reaches_The_Search_Box() => Session.Dispatch(() =>
    {
        var s = Show(width: 1000, options: new PaletteOptions { IgnoreModifiedKeys = true });

        s.Key(PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        s.Vm.SelectedIndex.Should().Be(0, "Shift+↓ is the text box's, not the palette's");
        s.Key(PhysicalKey.ArrowDown);
        s.Vm.SelectedIndex.Should().Be(1, "a plain ↓ still moves the selection");
        s.Window.Close();
    }, CancellationToken.None);
}
