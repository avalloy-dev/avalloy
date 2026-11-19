using Avalonia.Media;
using AwesomeAssertions;
using Avalloy.Views.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Desktop.Tests;

/// <summary>
/// How <see cref="AlloySelect"/> reads the chip's content from the selected item.
/// </summary>
/// <remarks>
/// Items that implement <see cref="IAlloySelectItem"/> are read through it, with no reflection, so a select
/// works in a trimmed or Native AOT app; items that don't are still read by the property names the
/// <c>*Path</c> properties give. Both are pinned here, through the trigger (chip) state the control derives.
/// </remarks>
[TestClass]
public sealed class AlloySelectItemTests
{
    private sealed class Mode(string label) : IAlloySelectItem
    {
        public string Label => label;
        public IBrush? Dot => Brushes.Green;
        public string? Glyph => "Bolt";
        public IBrush? GlyphTone => Brushes.Gold;
        // Deliberately not the label: an interface item must never fall back to ToString.
        public override string ToString() => "not the label";
    }

    private sealed record PathItem(string Name, IBrush Status, string Icon);

    [TestMethod]
    public void An_Interface_Item_Is_Read_Through_The_Interface()
    {
        var item = new Mode("Ask first");
        var select = new AlloySelect { ItemsSource = new[] { item }, DotPath = "Dot", GlyphPath = "Glyph", GlyphTonePath = "GlyphTone" };

        select.SelectedItem = item;

        select.TriggerLabel.Should().Be("Ask first", "the label needs no LabelPath");
        select.TriggerDot.Should().Be(Brushes.Green);
        select.TriggerGlyphEffective.Should().Be("Bolt");
        select.TriggerGlyphBrush.Should().Be(Brushes.Gold);
    }

    [TestMethod]
    public void An_Interface_Items_Slots_Stay_Off_Until_The_Select_Asks_For_Them()
    {
        var item = new Mode("Ask first");
        var select = new AlloySelect { ItemsSource = new[] { item }, TriggerGlyph = "Fixed" };

        select.SelectedItem = item;

        select.TriggerDot.Should().BeNull("no DotPath, so no dot slot");
        select.HasDot.Should().BeFalse();
        select.TriggerGlyphEffective.Should().Be("Fixed", "no GlyphPath, so the chip keeps its fixed glyph");
    }

    [TestMethod]
    public void A_Plain_Item_Is_Still_Read_By_Its_Property_Names()
    {
        var item = new PathItem("main", Brushes.Red, "Branch");
        var select = new AlloySelect
        {
            ItemsSource = new[] { item }, LabelPath = "Name", DotPath = "Status", GlyphPath = "Icon",
        };

        select.SelectedItem = item;

        select.TriggerLabel.Should().Be("main");
        select.TriggerDot.Should().Be(Brushes.Red);
        select.TriggerGlyphEffective.Should().Be("Branch");
    }
}
