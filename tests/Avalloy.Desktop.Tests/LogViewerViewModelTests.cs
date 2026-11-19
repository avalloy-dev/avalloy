using AwesomeAssertions;
using Avalloy.Controls.Logs;
using Avalloy.Services;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Controls.Logs.Tests;

[TestClass]
public class LogViewerViewModelTests
{
    private static LogBuffer NewBuffer(int capacity = 100) => new(capacity, uiPost: a => a());

    private static void Add(LogBuffer buffer, string message,
        LogLevel level = LogLevel.Information, string category = "cat") =>
        buffer.Add(new LogEntry(DateTimeOffset.Now, level, category, message, null, default, null));

    [TestMethod]
    public void Snapshot_and_live_entries_merge_without_duplicates_newest_first()
    {
        var buffer = NewBuffer();
        Add(buffer, "before-1");
        Add(buffer, "before-2");

        using var vm = new LogViewerViewModel(buffer);
        Add(buffer, "after");

        vm.Visible.Select(r => r.Message).Should().Equal("after", "before-2", "before-1");
    }

    [TestMethod]
    public void Oldest_first_ordering_flips_the_view()
    {
        var buffer = NewBuffer();
        using var vm = new LogViewerViewModel(buffer);
        Add(buffer, "one");
        Add(buffer, "two");

        vm.NewestFirst = false;
        vm.Visible.Select(r => r.Message).Should().Equal("one", "two");
    }

    [TestMethod]
    public void Min_level_floor_filters_and_chips_track_it()
    {
        var buffer = NewBuffer();
        using var vm = new LogViewerViewModel(buffer);
        Add(buffer, "t", LogLevel.Trace);
        Add(buffer, "i", LogLevel.Information);
        Add(buffer, "w", LogLevel.Warning);
        Add(buffer, "e", LogLevel.Error);

        var warnChip = vm.LevelChips.Single(c => c.Level == LogLevel.Warning);
        vm.SetMinLevelCommand.Execute(warnChip);

        vm.Visible.Select(r => r.Message).Should().Equal("e", "w");
        vm.LevelChips.Where(c => c.IsActive).Select(c => c.Level)
            .Should().Equal(LogLevel.Warning, LogLevel.Error);

        // Re-clicking the active floor lowers it back to everything.
        vm.SetMinLevelCommand.Execute(warnChip);
        vm.Visible.Should().HaveCount(4);
    }

    [TestMethod]
    public void Category_and_search_filters_compose()
    {
        var buffer = NewBuffer();
        using var vm = new LogViewerViewModel(buffer);
        Add(buffer, "alpha message", category: "A");
        Add(buffer, "beta message", category: "B");
        Add(buffer, "alpha again", category: "B");

        vm.Categories.Should().Equal(LogViewerViewModel.AllCategories, "A", "B");

        vm.SelectedCategory = "B";
        vm.Visible.Should().HaveCount(2);

        vm.SearchText = "alpha";
        vm.Visible.Select(r => r.Message).Should().Equal("alpha again");

        vm.SelectedCategory = LogViewerViewModel.AllCategories;
        vm.Visible.Select(r => r.Message).Should().Equal("alpha again", "alpha message");
    }

    [TestMethod]
    public void Pause_freezes_the_view_and_resume_catches_up()
    {
        var buffer = NewBuffer();
        using var vm = new LogViewerViewModel(buffer);
        Add(buffer, "seen");

        vm.Paused = true;
        Add(buffer, "missed-while-paused");
        vm.Visible.Select(r => r.Message).Should().Equal("seen");

        vm.Paused = false;
        vm.Visible.Select(r => r.Message).Should().Equal("missed-while-paused", "seen");
    }

    [TestMethod]
    public void Clear_empties_view_and_buffer()
    {
        var buffer = NewBuffer();
        using var vm = new LogViewerViewModel(buffer);
        Add(buffer, "gone");

        vm.ClearCommand.Execute(null);

        vm.Visible.Should().BeEmpty();
        buffer.Count.Should().Be(0);

        Add(buffer, "fresh");
        vm.Visible.Select(r => r.Message).Should().Equal("fresh");
    }

    [TestMethod]
    public void Disposed_view_model_stops_following_the_buffer()
    {
        var buffer = NewBuffer();
        var vm = new LogViewerViewModel(buffer);
        vm.Dispose();

        Add(buffer, "after-dispose");
        vm.Visible.Should().BeEmpty();
    }

    [TestMethod]
    public void Copy_text_uses_selection_or_falls_back_to_all_shown()
    {
        var buffer = NewBuffer();
        using var vm = new LogViewerViewModel(buffer);
        Add(buffer, "first");
        Add(buffer, "second");

        vm.CopyText().Should().Contain("first").And.Contain("second");
        var one = vm.Visible.First(r => r.Message == "second");
        vm.CopyText([one]).Should().Contain("second").And.NotContain("first");
    }

    [TestMethod]
    public void Rows_precompute_badges_and_clipboard_lines()
    {
        var entry = new LogEntry(
            DateTimeOffset.Now, LogLevel.Error, "cat", "kaboom",
            "System.Exception: kaboom", default, "outer");
        var row = new LogEntryRow(entry with { Sequence = 1 });

        row.LevelBadge.Should().Be("ERR");
        row.IsError.Should().BeTrue();
        row.HasException.Should().BeTrue();
        row.HasScopes.Should().BeTrue();
        var line = row.ToClipboardLine();
        line.Should().Contain("ERR [cat] outer · kaboom");
        line.Should().Contain("    System.Exception: kaboom");
    }
}
