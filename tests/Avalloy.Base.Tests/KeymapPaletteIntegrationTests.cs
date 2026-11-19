using Avalloy.Commands;
using Avalloy.Commands.Keymaps;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Avalloy.Base.Tests.KeymapEngineTests;

namespace Avalloy.Base.Tests;

[TestClass]
public class KeymapPaletteIntegrationTests
{
    [TestMethod]
    public void Keymap_palette_preserves_separate_hints_overflow_and_category()
    {
        var h = new Harness("super+3=record:3", "super+4=record:3", "super+5=record:3");
        var row = CommandPaletteViewModel.FromKeymap(h.Registry, h.Keys, h.State.Owner, KeymapPlatform.MacOS).Single();

        row.Hints.Should().Equal("⌘5", "⌘4", "⌘3");
        row.Hint.Should().BeEmpty("key labels use the non-searchable Hints field");
        row.HintText.Should().Be("⌘5  ⌘4  +1");
        row.HintTip.Should().Be("⌘5\n⌘4\n⌘3");
        row.Detail.Should().Be("Keymap");
        row.IsEnabled.Should().BeTrue();
        row.AccessibleName.Should().Contain("Keymap").And.Contain("⌘3");

        var palette = CommandPaletteViewModel.Create([row], () => { }, new PaletteOptions { Matching = PaletteMatching.Scored });
        palette.Query = "⌘5";
        palette.Results.Should().BeEmpty("shortcuts must not pollute label/path search");
    }

    [TestMethod]
    public void Keymap_palette_recents_distinguish_parameters_and_keep_target()
    {
        var h = new Harness("super+1=record", "super+2=record:", "super+3=record:3", "super+4=record:4", "super+5=record:blocked");
        var rows = CommandPaletteViewModel.FromKeymap(h.Registry, h.Keys, h.State.Owner, KeymapPlatform.MacOS);

        rows.Should().HaveCount(4, "unrunnable commands are still omitted");
        rows.Should().OnlyContain(row => row.IsEnabled && !string.IsNullOrEmpty(row.Key));
        rows.Select(row => row.Key).Distinct().Should().HaveCount(4, "null, empty, and distinct parameters have distinct recency identities");
        rows.Select(row => row.Key).Should().Contain(["[\"record\",null]", "[\"record\",\"\"]", "[\"record\",\"4\"]"],
            "the keys apps have already stored as recents keep their exact text");
        var four = rows.Single(row => row.Label.EndsWith(": 4"));
        var again = CommandPaletteViewModel.FromKeymap(h.Registry, h.Keys, h.State.Owner, KeymapPlatform.MacOS);
        again.Single(row => row.Label == four.Label).Key.Should().Be(four.Key);
        PaletteItem? used = null;
        var palette = CommandPaletteViewModel.Create(rows, () => { },
            new PaletteOptions { Recent = [four.RecentKey], Used = row => used = row });

        palette.Results[0].Should().BeSameAs(four);
        palette.Activate();
        used.Should().BeSameAs(four);
        h.Command.Ran.Should().ContainSingle().Which.Should().Be(("4", h.State.Owner));
    }
}
