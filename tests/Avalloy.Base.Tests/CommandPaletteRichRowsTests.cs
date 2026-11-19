using AwesomeAssertions;
using Avalloy.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Base.Tests;

/// <summary>
/// The palette's opt-in behaviour: disabled rows, several hints per row, app-supplied rows, recents and
/// scored ranking. <see cref="CommandPaletteViewModelTests"/> is the default behaviour and is untouched —
/// it passing unchanged is the backward-compatibility half of this.
/// </summary>
[TestClass]
public class CommandPaletteRichRowsTests
{
    private readonly List<string> _ran = [];
    private int _closes;

    private PaletteItem Item(string label, bool enabled = true, string note = "", string hint = "",
        IReadOnlyList<string>? hints = null, string? key = null, int group = 0) =>
        new()
        {
            Label = label, IsEnabled = enabled, Note = note, Hint = hint, Hints = hints ?? [], Key = key,
            Group = group, Run = () => _ran.Add(label),
        };

    private CommandPaletteViewModel Palette(PaletteOptions options, params PaletteItem[] items) =>
        CommandPaletteViewModel.Create(items, () => _closes++, options);

    private CommandPaletteViewModel Palette(params PaletteItem[] items) => Palette(new PaletteOptions(), items);

    private static string[] Labels(CommandPaletteViewModel vm) => [.. vm.Results.Select(r => r.Label)];

    // ── disabled rows ────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void A_Disabled_Row_Is_Listed()
    {
        var vm = Palette(Item("Split Right"), Item("Detach Tab", enabled: false, note: "not available yet"));

        Labels(vm).Should().Equal(["Split Right", "Detach Tab"],
            "the point of keeping it is that the user can see the command exists");
    }

    [TestMethod]
    public void Selection_Starts_On_The_First_ENABLED_Row()
    {
        var vm = Palette(new PaletteOptions { Recent = ["Off"] }, Item("Off", enabled: false), Item("On"));

        vm.Results[0].Label.Should().Be("On", "disabled rows sink below enabled ones even when recent");
        vm.SelectedIndex.Should().Be(0);
    }

    [TestMethod]
    public void Disabled_Rows_Sink_Below_Enabled_Ones()
    {
        var vm = Palette(Item("A", enabled: false), Item("B"), Item("C", enabled: false), Item("D"));

        Labels(vm).Should().Equal(["B", "D", "A", "C"]);

        vm.Query = "a";
        vm.Results.Should().ContainSingle();
        vm.SelectedIndex.Should().Be(-1, "the only match cannot run, so there is nothing for Enter to do");
    }

    [TestMethod]
    public void Arrowing_Steps_Over_Disabled_Rows_And_Wraps_Past_Them()
    {
        // Disabled rows sort last, so put the enabled ones in different groups and filter, which keeps
        // a disabled row BETWEEN two enabled ones: group 0 sorts before group 1.
        var vm = Palette(
            Item("go one", group: 0),
            Item("go two", enabled: false, group: 0),
            Item("go three", group: 1),
            Item("go four", enabled: false, group: 1));
        vm.Query = "go";
        Labels(vm).Should().Equal(["go one", "go two", "go three", "go four"]);
        vm.SelectedIndex.Should().Be(0);

        vm.Move(+1);
        vm.SelectedIndex.Should().Be(2, "↓ skips the disabled row in between");
        vm.Move(+1);
        vm.SelectedIndex.Should().Be(0, "and wraps past the disabled tail");
        vm.Move(-1);
        vm.SelectedIndex.Should().Be(2, "↑ wraps the same way");
        vm.Move(+2);
        vm.SelectedIndex.Should().Be(2, "a delta of two counts enabled rows: there are two, so it comes back");
    }

    [TestMethod]
    public void A_Big_Move_Lands_Where_Stepping_One_At_A_Time_Would()
    {
        var vm = Palette(Item("a"), Item("b"), Item("c"));

        vm.Move(4);
        vm.SelectedIndex.Should().Be(1, "4 steps round 3 rows: (0 + 4) mod 3, as the original arithmetic gave");
        vm.Move(-3_000_000);
        vm.SelectedIndex.Should().Be(1, "a whole number of laps lands back on the same row");
        var extreme = () => vm.Move(int.MinValue);
        extreme.Should().NotThrow();
    }

    [TestMethod]
    public void With_Every_Row_Disabled_Nothing_Is_Selected_And_Arrowing_Does_Nothing()
    {
        var vm = Palette(Item("A", enabled: false), Item("B", enabled: false));

        vm.SelectedIndex.Should().Be(-1);
        vm.Move(+1);
        vm.SelectedIndex.Should().Be(-1);
    }

    [TestMethod]
    public void Clicking_A_Disabled_Row_Neither_Runs_It_Nor_Closes_The_Palette()
    {
        var vm = Palette(Item("On"), Item("Off", enabled: false));

        vm.RunItem(vm.Results[1]);

        _ran.Should().BeEmpty();
        _closes.Should().Be(0, "a click on a row that cannot run is not a reason to throw the palette away");
    }

    [TestMethod]
    public void Enter_When_Every_Listed_Row_Is_Disabled_Does_Nothing()
    {
        var vm = Palette(Item("Detach Tab", enabled: false), Item("Split", enabled: true));
        vm.Query = "detach";
        vm.Results.Should().ContainSingle();
        vm.SelectedIndex.Should().Be(-1);

        vm.Activate();

        _ran.Should().BeEmpty();
        _closes.Should().Be(0, "the user can see the row they asked for; Enter on it must not throw the palette away");
    }

    [TestMethod]
    public void Enter_With_No_Selection_But_Runnable_Rows_Still_Closes_As_Before()
    {
        // Only reachable when the list's own selection is cleared (the two-way SelectedIndex binding):
        // the original Activate closed without running anything, and it still does.
        var vm = Palette(Item("One"), Item("Two", enabled: false));
        vm.SelectedIndex = -1;

        vm.Activate();

        _ran.Should().BeEmpty();
        _closes.Should().Be(1);
    }

    [TestMethod]
    public void A_Disabled_Row_Is_Read_Out_With_Why()
    {
        var row = Item("Detach Tab", enabled: false, note: "not available yet", hints: ["⌘⇧D"]);

        row.AccessibleName.Should().Be("Detach Tab, ⌘⇧D, not available yet");
        row.RowOpacity.Should().BeLessThan(1);
    }

    // ── several hints ────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void One_Or_Two_Hints_Are_Printed_In_Full_With_No_Tooltip()
    {
        Item("x", hints: ["⌘D"]).HintText.Should().Be("⌘D");
        var two = Item("x", hints: ["⌘D", "⌃A H"]);
        two.HintText.Should().Be("⌘D  ⌃A H");
        two.HintTip.Should().BeNull("everything is already on the row");
    }

    [TestMethod]
    public void Past_Two_Hints_The_Rest_Are_Counted_And_All_Go_In_The_Tooltip()
    {
        var row = Item("New Split", hints: ["⌘D", "⌃A H", "⌃A V", "F5"]);

        row.HintText.Should().Be("⌘D  ⌃A H  +2");
        row.HintTip.Should().Be("⌘D\n⌃A H\n⌃A V\nF5");
    }

    [TestMethod]
    public void Every_Hint_Is_In_The_Accessible_Name_Not_Just_The_Shown_Ones()
        => Item("New Split", hints: ["⌘D", "⌃A H", "⌃A V"]).AccessibleName
            .Should().Be("New Split, ⌘D, ⌃A H, ⌃A V");

    [TestMethod]
    public void Hints_Replace_The_Single_Hint_On_Screen()
    {
        var row = Item("x", hint: "Mod+S", hints: ["⌘S", "⌃S"]);
        row.HintText.Should().Be("⌘S  ⌃S");
        Item("x", hint: "Mod+S").HintText.Should().Be("Mod+S", "no Hints is the original single hint");
    }

    [TestMethod]
    public void Hints_Are_Not_Searched()
    {
        var vm = Palette(Item("Split Right", hints: ["⌘D"]), Item("Close", hint: "path/d"));

        vm.Query = "d";

        Labels(vm).Should().Equal(["Close"],
            "a key label is not a name: 'd' must not find every row bound to ⌘D (the Hint string still is searched)");
    }

    [TestMethod]
    public void Detail_Shows_After_The_Label_And_Is_Read_But_Not_Searched()
    {
        var row = new PaletteItem { Label = "Copy", Detail = "Clipboard", Run = () => { } };
        row.DetailSuffix.Should().Be("  Clipboard");
        row.AccessibleName.Should().Be("Copy, Clipboard");
        new PaletteItem { Label = "Copy", Run = () => { } }.DetailSuffix.Should().BeEmpty();

        var vm = new CommandPaletteViewModel([row], () => { });
        vm.Query = "board";
        vm.Results.Should().BeEmpty();
    }

    // ── app-supplied rows ────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void One_Row_Per_Parameter_Each_Runs_Its_Own_Action()
    {
        var ran = new List<int>();
        var rows = Enumerable.Range(1, 9).Select(n => new PaletteItem
        {
            Label = $"Go to Tab {n}", Key = $"goto_tab:{n}", Run = () => ran.Add(n),
        }).ToArray();
        var vm = CommandPaletteViewModel.Create(rows, () => { }, new PaletteOptions { Matching = PaletteMatching.Scored });

        vm.Query = "tab 7";
        vm.Activate();

        ran.Should().Equal([7], "each row closes over its own parameter");
    }

    [TestMethod]
    public void App_Rows_And_Registry_Rows_Share_One_List_With_The_Same_Rules()
    {
        // Registry rows are just PaletteItems: an app's own rows sit beside them and get the same
        // disabled / recents / ranking treatment. (FromRegistry itself is covered in CommandPaletteRegistrySourceTests.)
        var vm = Palette(
            new PaletteOptions { Recent = ["goto_tab:2"] },
            Item("Save", key: "save"),
            Item("Go to Tab 1", key: "goto_tab:1"),
            Item("Go to Tab 2", key: "goto_tab:2"),
            Item("Reload Config", enabled: false, key: "reload_config"));

        Labels(vm).Should().Equal(["Go to Tab 2", "Save", "Go to Tab 1", "Reload Config"]);
    }

    // ── recents ──────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void With_An_Empty_Query_Recent_Rows_Float_Up_In_Recency_Order()
    {
        var vm = Palette(new PaletteOptions { Recent = ["c", "a"] }, Item("a"), Item("b"), Item("c"), Item("d"));

        Labels(vm).Should().Equal(["c", "a", "b", "d"], "most recent first, then the rest in the caller's order");
    }

    [TestMethod]
    public void Recents_Match_On_Key_Not_Label()
    {
        var vm = Palette(new PaletteOptions { Recent = ["split"] }, Item("Split Down", key: "split:down"), Item("Split Right", key: "split"));

        Labels(vm).Should().Equal(["Split Right", "Split Down"]);
        Item("No Key").RecentKey.Should().Be("No Key", "without a Key the label is the identity");
    }

    [TestMethod]
    public void With_A_Query_Recency_Breaks_Ties_But_Does_Not_Beat_A_Better_Match()
    {
        var options = new PaletteOptions { Matching = PaletteMatching.Scored, Recent = ["Close Window", "Toggle Clipboard History"] };
        var vm = Palette(options, Item("Clear Screen"), Item("Close Window"), Item("Toggle Clipboard History"));

        vm.Query = "c";
        Labels(vm).Should().Equal(["Close Window", "Clear Screen", "Toggle Clipboard History"],
            "Clear Screen and Close Window score the same, so the recent one goes first; the clipboard row is " +
            "the most recent of all but its c is a weaker match, so it stays last");
    }

    [TestMethod]
    public void Recency_Breaks_Ties_In_The_Default_Matching_Too()
    {
        var vm = Palette(new PaletteOptions { Recent = ["beta format"] }, Item("alpha format"), Item("beta format"));
        vm.Query = "fmt";
        Labels(vm).Should().Equal(["beta format", "alpha format"]);
    }

    [TestMethod]
    public void Running_A_Row_Reports_It_After_Closing_And_Before_Running()
    {
        var order = new List<string>();
        var row = new PaletteItem { Label = "One", Key = "one", Run = () => order.Add("run") };
        var vm = CommandPaletteViewModel.Create([row], () => order.Add("close"),
            new PaletteOptions { Used = item => order.Add($"used:{item.RecentKey}") });

        vm.Activate();

        order.Should().Equal(["close", "used:one", "run"]);
    }

    [TestMethod]
    public void Closing_On_An_Empty_List_Reports_Nothing_Used()
    {
        var used = 0;
        var vm = CommandPaletteViewModel.Create([], () => { }, new PaletteOptions { Used = _ => used++ });
        vm.Activate();
        used.Should().Be(0);
    }

    [TestMethod]
    public void Recents_Move_A_Use_To_The_Front_And_Cap()
    {
        var recents = new PaletteRecents(capacity: 2);
        var changes = 0;
        recents.Changed += (_, _) => changes++;

        recents.Use("a");
        recents.Use("b");
        recents.Use("a");
        recents.Keys.Should().Equal(["a", "b"]);
        recents.Use("c");
        recents.Keys.Should().Equal(["c", "a"]);
        recents.Use("c");
        changes.Should().Be(4, "using what is already most recent changes nothing, so nothing to save");
    }

    [TestMethod]
    public void Recents_Seed_From_What_The_App_Saved_Deduplicated_And_Capped()
        => new PaletteRecents(capacity: 2, seed: ["a", "a", "b", "c"]).Keys.Should().Equal(["a", "b"]);

    [TestMethod]
    public void Recents_Plug_Straight_Into_The_Palette()
    {
        var recents = new PaletteRecents();
        PaletteItem[] rows = [Item("a"), Item("b", key: "bee")];

        CommandPaletteViewModel.Create(rows, () => { }, new PaletteOptions { Used = recents.Use }).RunItem(rows[1]);
        var next = CommandPaletteViewModel.Create(rows, () => { }, new PaletteOptions { Recent = recents.Keys });

        recents.Keys.Should().Equal(["bee"]);
        Labels(next).Should().Equal(["b", "a"]);
    }

    // ── ranking (scored) ─────────────────────────────────────────────────────────────────────────

    private CommandPaletteViewModel Scored(params PaletteItem[] items) =>
        Palette(new PaletteOptions { Matching = PaletteMatching.Scored }, items);

    [TestMethod]
    public void Scored_A_Prefix_Beats_A_Word_Start_Beats_Mid_Word()
    {
        var vm = Scored(Item("Stable"), Item("New Tab"), Item("Tab Bar"));
        vm.Query = "tab";
        Labels(vm).Should().Equal(["Tab Bar", "New Tab", "Stable"]);
    }

    [TestMethod]
    public void Scored_A_Contiguous_Run_Beats_Scattered_Letters()
    {
        // No word starts in either: "ash" runs together in Rehashed and is split up in Parish. Parish is
        // listed first and is shorter, so without the run bonus it would win.
        var vm = Scored(Item("Parish"), Item("Rehashed"));
        vm.Query = "ash";
        Labels(vm).Should().Equal(["Rehashed", "Parish"]);
    }

    [TestMethod]
    public void Scored_A_Shorter_Label_Wins_A_Tie()
    {
        var vm = Scored(Item("Copy URL to Clipboard"), Item("Copy"));
        vm.Query = "copy";
        Labels(vm).Should().Equal(["Copy", "Copy URL to Clipboard"]);
    }

    [TestMethod]
    public void Scored_A_Label_Match_Beats_A_Hint_Only_Match()
    {
        var vm = Scored(Item("index.ts", hint: "src/server"), Item("Restart Server"));
        vm.Query = "server";
        Labels(vm).Should().Equal(["Restart Server", "index.ts"],
            "the hint is still searched, so the file is found, but a row NAMED for the query is what was meant");
    }

    [TestMethod]
    public void Scored_Groups_Still_Come_First()
    {
        var vm = Scored(Item("Tab Bar", group: 1), Item("Stable", group: 0));
        vm.Query = "tab";
        Labels(vm).Should().Equal(["Stable", "Tab Bar"], "group is the caller's ranking of kinds, above match quality");
    }

    [TestMethod]
    public void Scored_The_Cap_Keeps_The_Best_Matches_Not_The_First_Ones()
    {
        var items = Enumerable.Range(0, 50).Select(i => Item($"z{i}iztzezm")).Append(Item("item")).ToArray();
        var vm = Scored(items);

        vm.Query = "item";

        vm.Results.Should().HaveCount(PaletteOptions.DefaultMaxResults);
        vm.Results[0].Label.Should().Be("item", "it is the 51st row in the source but the best match");
    }

    [TestMethod]
    public void The_Cap_Is_The_Callers_To_Change()
    {
        var items = Enumerable.Range(0, 60).Select(i => Item($"row {i}")).ToArray();
        Palette(new PaletteOptions { MaxResults = 100 }, items).Results.Should().HaveCount(60);
        Palette(new PaletteOptions { MaxResults = 3 }, items).Results.Should().HaveCount(3);
    }

    [TestMethod]
    public void The_Default_Matching_Does_Not_Score()
    {
        var vm = Palette(Item("Parish"), Item("Rehashed"));
        vm.Query = "ash";
        Labels(vm).Should().Equal(["Parish", "Rehashed"],
            "without opting in, a match keeps the caller's order: the original behaviour");
    }

    [TestMethod]
    public void Default_Options_Are_The_Original_Palette()
    {
        var options = new PaletteOptions();
        options.Matching.Should().Be(PaletteMatching.Subsequence);
        options.MaxResults.Should().Be(40);
        options.Recent.Should().BeEmpty();
        options.Used.Should().BeNull();
        options.IgnoreModifiedKeys.Should().BeFalse();

        var item = new PaletteItem { Label = "One", Run = () => { } };
        item.IsEnabled.Should().BeTrue();
        item.Hints.Should().BeEmpty();
        item.Note.Should().BeEmpty();
        item.Detail.Should().BeEmpty();
        item.Key.Should().BeNull();
        item.AccessibleName.Should().Be("One");
    }
}

/// <summary><see cref="PaletteSearch.Score"/> on its own — the numbers <see cref="PaletteMatching.Scored"/> sorts by.</summary>
[TestClass]
public class PaletteSearchScoreTests
{
    [TestMethod]
    public void A_Subsequence_Scores_And_Anything_Else_Does_Not()
    {
        PaletteSearch.Score("cs", "Clear Screen").Should().NotBeNull();
        PaletteSearch.Score("sc", "Clear Screen").Should().NotBeNull();
        PaletteSearch.Score("xyz", "Clear Screen").Should().BeNull();
        PaletteSearch.Score("ss", "s").Should().BeNull("one s cannot match twice");
        PaletteSearch.Score("CLEAR", "clear").Should().NotBeNull("case does not matter");
    }

    [TestMethod]
    public void An_Empty_Or_Blank_Query_Scores_Zero()
    {
        PaletteSearch.Score("", "Anything").Should().Be(0);
        PaletteSearch.Score("  ", "Anything").Should().Be(0);
    }

    [TestMethod]
    public void Spaces_In_The_Query_Are_Skipped()
        => PaletteSearch.Score("go 7", "Go to Tab 7").Should().NotBeNull();

    [TestMethod]
    public void Word_Starts_Beat_Letters_Inside_Words()
        => PaletteSearch.Score("cs", "Clear Screen").Should().BeGreaterThan(PaletteSearch.Score("cs", "Close Window")!.Value);

    [TestMethod]
    public void A_CamelCase_Hump_Is_A_Word_Start()
        => PaletteSearch.Score("w", "OpenWindow").Should().BeGreaterThan(PaletteSearch.Score("w", "Openwindow")!.Value);

    [TestMethod]
    public void The_Best_Alignment_Is_Scored_Not_The_First_One_Found()
    {
        // Matching greedily, "tb" takes the b inside "Tab" and both rows score the same, so the shorter one
        // wins. Scored over every alignment, the B of "Bar" is a word start and Toggle Tab Bar wins.
        PaletteSearch.Score("tb", "Toggle Tab Bar").Should().BeGreaterThan(PaletteSearch.Score("tb", "Toggle Tabs")!.Value);
    }

    [TestMethod]
    public void The_Score_Is_The_Documented_Arithmetic()
    {
        // C at 0: 1 + 8 start; S at 6: 1 + 6 word start. (9 + 7) * 100 - 12 characters.
        PaletteSearch.Score("cs", "Clear Screen").Should().Be(1588);
        // c, o, p, y contiguous from 0: (1+8) + 3 * (1+5) = 27.
        PaletteSearch.Score("copy", "Copy").Should().Be(27 * 100 - 4);
    }
}

/// <summary>
/// How much of the source an EMPTY query reads. Counted, not timed: the source is a list that counts every
/// item handed out through its enumerator or its indexer.
/// </summary>
[TestClass]
public class CommandPaletteSourceReadTests
{
    private sealed class CountingList(IReadOnlyList<PaletteItem> inner) : IReadOnlyList<PaletteItem>
    {
        public int Reads { get; private set; }
        public int Count => inner.Count;

        public PaletteItem this[int index]
        {
            get { Reads++; return inner[index]; }
        }

        public IEnumerator<PaletteItem> GetEnumerator()
        {
            foreach (var item in inner)
            {
                Reads++;
                yield return item;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static CountingList Rows(int count, Func<int, bool>? enabled = null) =>
        new([.. Enumerable.Range(0, count).Select(i => new PaletteItem
        {
            Label = $"file{i}.ts", IsEnabled = enabled?.Invoke(i) ?? true, Run = () => { },
        })]);

    [TestMethod]
    public void Opening_Over_A_Large_List_Reads_Only_A_Screenful()
    {
        var source = Rows(5000);

        var vm = new CommandPaletteViewModel(source, () => { });

        vm.Results.Should().HaveCount(PaletteOptions.DefaultMaxResults);
        source.Reads.Should().BeLessThanOrEqualTo(PaletteOptions.DefaultMaxResults + 1,
            "with nothing to rank, the cap stops the read — as the original palette's Take(40) did");
    }

    [TestMethod]
    public void The_Early_Stop_Follows_A_Callers_Cap()
    {
        var source = Rows(5000);

        CommandPaletteViewModel.Create(source, () => { }, new PaletteOptions { MaxResults = 10 });

        source.Reads.Should().BeLessThanOrEqualTo(11);
    }

    [TestMethod]
    public void Clearing_The_Query_Reads_Only_A_Screenful_Again()
    {
        var source = Rows(5000);
        var vm = new CommandPaletteViewModel(source, () => { });
        vm.Query = "file";
        var afterSearch = source.Reads;

        vm.Query = "";

        (source.Reads - afterSearch).Should().BeLessThanOrEqualTo(PaletteOptions.DefaultMaxResults + 1);
    }

    [TestMethod]
    public void Disabled_Rows_Still_Sink_On_The_Lazy_Path()
    {
        var source = Rows(5, enabled: i => i % 2 == 1);

        var vm = new CommandPaletteViewModel(source, () => { });

        vm.Results.Select(r => r.Label).Should().Equal(["file1.ts", "file3.ts", "file0.ts", "file2.ts", "file4.ts"]);
        vm.SelectedIndex.Should().Be(0);
    }
}

