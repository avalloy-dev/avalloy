using AwesomeAssertions;
using Avalloy.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Base.Tests;

/// <summary>
/// The palette's filtering, selection and activation contract. Avalonia-free: the view model owns every
/// decision the user can observe, and the view is three handlers that delegate to it.
/// </summary>
[TestClass]
public class CommandPaletteViewModelTests
{
    private readonly List<string> _ran = [];
    private int _closes;

    private PaletteItem Item(string label, string hint = "", int group = 0, string glyph = "Enter") =>
        new() { Label = label, Hint = hint, Group = group, Glyph = glyph, Run = () => _ran.Add(label) };

    private CommandPaletteViewModel Palette(params PaletteItem[] items) =>
        new(items, () => _closes++);

    // ── the unfiltered list ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void An_Empty_Query_Shows_Everything_In_Source_Order()
    {
        var vm = Palette(Item("Zebra"), Item("Apple"), Item("Mango"));

        vm.Results.Select(r => r.Label).Should().Equal(["Zebra", "Apple", "Mango"],
            "an empty query is not a search, so the caller's ordering is the answer");
    }

    [TestMethod]
    public void The_First_Row_Is_Selected_So_Enter_Works_Without_Arrowing()
        => Palette(Item("One"), Item("Two")).SelectedIndex.Should().Be(0);

    [TestMethod]
    public void No_Rows_Means_No_Selection()
        => Palette().SelectedIndex.Should().Be(-1);

    // ── matching ─────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Matching_Is_Case_Insensitive()
    {
        var vm = Palette(Item("Toggle Sidebar"));
        vm.Query = "SIDEBAR";
        vm.Results.Should().ContainSingle();
    }

    [TestMethod]
    public void Matching_Is_A_Subsequence_Not_A_Substring()
    {
        var vm = Palette(Item("oauth.ts"), Item("readme.md"));

        vm.Query = "ots";

        vm.Results.Select(r => r.Label).Should().Equal(["oauth.ts"],
            "'ots' appears in oauth.ts only as scattered letters — the whole point is that a user " +
            "who half-remembers a name still finds it");
    }

    [TestMethod]
    public void A_Subsequence_Must_Be_In_Order()
    {
        var vm = Palette(Item("oauth.ts"));
        vm.Query = "sto";
        vm.Results.Should().BeEmpty("the letters are all present but not in that sequence");
    }

    [TestMethod]
    public void The_Hint_Is_Searched_Too()
    {
        var vm = Palette(Item("index.ts", hint: "src/server"));
        vm.Query = "server";
        vm.Results.Should().ContainSingle("typing a folder name is how you find a file inside it");
    }

    [TestMethod]
    public void A_Whitespace_Only_Query_Is_Not_A_Filter()
    {
        var vm = Palette(Item("One"), Item("Two"));
        vm.Query = "   ";
        vm.Results.Should().HaveCount(2);
    }

    [TestMethod]
    public void A_Query_That_Matches_Nothing_Clears_The_Selection()
    {
        var vm = Palette(Item("One"));
        vm.Query = "zzz";

        vm.Results.Should().BeEmpty();
        vm.SelectedIndex.Should().Be(-1, "Enter on an empty list must not run the row that used to be there");
    }

    [TestMethod]
    public void Narrowing_Then_Widening_Restores_The_Full_List()
    {
        var vm = Palette(Item("One"), Item("Two"));
        vm.Query = "One";
        vm.Results.Should().ContainSingle();

        vm.Query = "";

        vm.Results.Should().HaveCount(2, "the source list is retained, not consumed");
    }

    // ── ranking ──────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Lower_Groups_Rank_First_When_Filtered()
    {
        var vm = Palette(Item("open file", group: 1), Item("open pane", group: 0));

        vm.Query = "open";

        vm.Results.Select(r => r.Label).Should().Equal(["open pane", "open file"],
            "group is the caller's ranking of KINDS — fixed commands above files");
    }

    [TestMethod]
    public void Within_A_Group_A_Prefix_Match_Beats_A_Scattered_One()
    {
        var vm = Palette(Item("unformatted"), Item("format document"));

        vm.Query = "format";

        vm.Results.Select(r => r.Label).Should().Equal(["format document", "unformatted"],
            "a name that STARTS with what you typed is what you meant");
    }

    [TestMethod]
    public void Ranking_Only_Applies_Once_There_Is_A_Query()
    {
        var vm = Palette(Item("b", group: 1), Item("a", group: 0));

        vm.Results.Select(r => r.Label).Should().Equal(["b", "a"],
            "sorting an unfiltered list would reorder the caller's own arrangement behind its back");
    }

    // ── the cap ──────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void The_Result_List_Is_Capped()
    {
        var vm = Palette([.. Enumerable.Range(0, 200).Select(i => Item($"item{i}"))]);

        vm.Results.Should().HaveCount(40, "the palette is a picker, not a browser");
    }

    [TestMethod]
    public void The_Cap_Applies_To_A_Filtered_List_Too()
    {
        var vm = Palette([.. Enumerable.Range(0, 200).Select(i => Item($"item{i}"))]);
        vm.Query = "item";
        vm.Results.Should().HaveCount(40);
    }

    // ── moving ───────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Down_Advances_The_Selection()
    {
        var vm = Palette(Item("One"), Item("Two"));
        vm.Move(+1);
        vm.SelectedIndex.Should().Be(1);
    }

    [TestMethod]
    public void Down_Past_The_End_Wraps_To_The_Top()
    {
        var vm = Palette(Item("One"), Item("Two"));
        vm.Move(+1);
        vm.Move(+1);
        vm.SelectedIndex.Should().Be(0);
    }

    [TestMethod]
    public void Up_From_The_Top_Wraps_To_The_Bottom()
    {
        var vm = Palette(Item("One"), Item("Two"), Item("Three"));

        vm.Move(-1);

        vm.SelectedIndex.Should().Be(2, "↑ from the first row is how you reach the last one quickly");
    }

    [TestMethod]
    public void Moving_An_Empty_List_Does_Nothing()
    {
        var vm = Palette();
        vm.Move(+1);
        vm.SelectedIndex.Should().Be(-1, "and does not throw");
    }

    // ── activation ───────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Enter_Runs_The_Selected_Row()
    {
        var vm = Palette(Item("One"), Item("Two"));
        vm.Move(+1);

        vm.Activate();

        _ran.Should().Equal("Two");
    }

    [TestMethod]
    public void Enter_On_An_Empty_List_Closes_Without_Running_Anything()
    {
        var vm = Palette(Item("One"));
        vm.Query = "zzz";

        vm.Activate();

        _ran.Should().BeEmpty();
        _closes.Should().Be(1, "Esc is not the only way out of a palette showing nothing");
    }

    [TestMethod]
    public void Activation_Closes_BEFORE_It_Runs()
    {
        var order = new List<string>();
        var vm = new CommandPaletteViewModel(
            [new PaletteItem { Label = "One", Run = () => order.Add("run") }],
            () => order.Add("close"));

        vm.Activate();

        order.Should().Equal(["close", "run"],
            "a command that opens its OWN overlay must not have the palette's close tear it back down");
    }

    [TestMethod]
    public void Clicking_A_Row_Runs_That_Row_Not_The_Selected_One()
    {
        var vm = Palette(Item("One"), Item("Two"));

        vm.RunItem(vm.Results[1]);

        _ran.Should().Equal("Two");
        vm.SelectedIndex.Should().Be(0, "the pointer never moved the keyboard selection");
    }

    // ── keys ─────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Down_Up_And_Enter_Are_The_Palettes_Keys()
    {
        var vm = Palette(Item("One"), Item("Two"));

        vm.HandleKey(Avalonia.Input.Key.Down).Should().BeTrue();
        vm.SelectedIndex.Should().Be(1);
        vm.HandleKey(Avalonia.Input.Key.Up).Should().BeTrue();
        vm.SelectedIndex.Should().Be(0);
        vm.HandleKey(Avalonia.Input.Key.Enter).Should().BeTrue();
        _ran.Should().Equal("One");
    }

    [TestMethod]
    [DataRow(Avalonia.Input.Key.Left)]
    [DataRow(Avalonia.Input.Key.A)]
    [DataRow(Avalonia.Input.Key.Escape)]
    [DataRow(Avalonia.Input.Key.Tab)]
    public void Every_Other_Key_Is_Left_Alone(Avalonia.Input.Key key)
    {
        var vm = Palette(Item("One"), Item("Two"));

        vm.HandleKey(key).Should().BeFalse("typing, caret moves, Tab and the window's Esc must reach their owners");
        vm.SelectedIndex.Should().Be(0);
        _ran.Should().BeEmpty();
        _closes.Should().Be(0);
    }

    [TestMethod]
    public void Modified_Keys_Are_The_Palettes_Too_By_Default()
    {
        var vm = Palette(Item("One"), Item("Two"));

        vm.HandleKey(Avalonia.Input.Key.Down, Avalonia.Input.KeyModifiers.Shift).Should().BeTrue();
        vm.SelectedIndex.Should().Be(1, "as the palette always behaved");
    }

    [TestMethod]
    public void With_IgnoreModifiedKeys_Only_Unmodified_Keys_Are_The_Palettes()
    {
        var vm = CommandPaletteViewModel.Create([Item("One"), Item("Two")], () => _closes++,
            new PaletteOptions { IgnoreModifiedKeys = true });

        vm.HandleKey(Avalonia.Input.Key.Down, Avalonia.Input.KeyModifiers.Shift).Should().BeFalse();
        vm.HandleKey(Avalonia.Input.Key.Enter, Avalonia.Input.KeyModifiers.Meta).Should().BeFalse();
        vm.SelectedIndex.Should().Be(0);
        _ran.Should().BeEmpty();

        vm.HandleKey(Avalonia.Input.Key.Down, Avalonia.Input.KeyModifiers.None).Should().BeTrue();
        vm.SelectedIndex.Should().Be(1);
    }

    // ── placeholder ──────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void The_Global_Placeholder_Is_The_Default()
        => Palette().Placeholder.Should().Be(CommandPaletteViewModel.GlobalPlaceholder);

    [TestMethod]
    public void A_Scoped_Picker_States_What_It_Is_A_List_Of()
        => new CommandPaletteViewModel([], () => { }, "Move to which project…")
            .Placeholder.Should().Be("Move to which project…");

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void A_Blank_Placeholder_Falls_Back_Rather_Than_Showing_An_Empty_Box(string? given)
        => new CommandPaletteViewModel([], () => { }, given)
            .Placeholder.Should().Be(CommandPaletteViewModel.GlobalPlaceholder);

    // ── item defaults ────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void An_Item_Defaults_To_The_Run_Glyph_And_No_Hint()
    {
        var item = new PaletteItem { Label = "One", Run = () => { } };
        item.Glyph.Should().Be("Enter");
        item.Hint.Should().BeEmpty();
        item.Group.Should().Be(0);
    }
}
