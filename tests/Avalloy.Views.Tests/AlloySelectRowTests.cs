using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Avalloy.Views.Controls;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Views.Tests;

/// <summary>
/// What <see cref="AlloySelect"/>'s rows show. An <see cref="IAlloySelectItem"/> is read through the interface,
/// with no reflection, so a select works in a trimmed or Native AOT app; any other item keeps what it had: its
/// <c>LabelPath</c> property, or the default presenter when there is none.
/// </summary>
[TestClass]
public class AlloySelectRowTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Get();

    private sealed class Mode(string label, string description) : IAlloySelectItem
    {
        public string Label => label;
        public string? Description => description;
        public override string ToString() => "not the label";
    }

    private sealed record Branch(string Name);

    private static IEnumerable<string?> Texts(Control row) =>
        row.GetSelfAndLogicalDescendants().OfType<TextBlock>().Select(t => t.Text);

    [TestMethod]
    public Task A_Single_Line_Row_Shows_An_Interface_Items_Label() => Session.Dispatch(() =>
    {
        var item = new Mode("Ask first", "unused here");
        var select = new AlloySelect { ItemsSource = new[] { item } };

        select.ItemTemplate!.Match(item).Should().BeTrue();
        Texts(select.ItemTemplate.Build(item)!).Should().Equal("Ask first");
    }, CancellationToken.None);

    [TestMethod]
    public Task A_Plain_Item_Without_A_LabelPath_Keeps_The_Default_Presenter() => Session.Dispatch(() =>
    {
        // As before: no DisplayMemberBinding meant ToString, or the item itself when it is a control.
        var select = new AlloySelect { ItemsSource = new[] { "main" } };

        select.ItemTemplate!.Match("main").Should().BeFalse();
    }, CancellationToken.None);

    [TestMethod]
    public Task A_Plain_Item_Is_Labelled_By_Its_LabelPath() => Session.Dispatch(() =>
    {
        var item = new Branch("feature/x");
        var select = new AlloySelect { ItemsSource = new[] { item }, LabelPath = "Name" };
        var window = new Window { Content = select };
        window.Show();

        var row = select.ItemTemplate!.Build(item)!;
        row.DataContext = item;   // what the item's container does

        Texts(row).Should().Equal("feature/x");
    }, CancellationToken.None);

    [TestMethod]
    public Task A_Two_Line_Row_Reads_An_Interface_Items_Description() => Session.Dispatch(() =>
    {
        var item = new Mode("Ask first", "Every write waits for you");
        var select = new AlloySelect { ItemsSource = new[] { item }, DescriptionPath = "Description" };

        Texts(select.ItemTemplate!.Build(item)!).Should().Equal("Ask first", "Every write waits for you");
    }, CancellationToken.None);
}
