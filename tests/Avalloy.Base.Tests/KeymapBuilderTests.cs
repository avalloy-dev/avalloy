using AwesomeAssertions;
using Avalloy.Commands.Keymaps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Base.Tests;

[TestClass]
public class KeymapBuilderTests
{
    private static string ThreeKeys(int i) =>
        $"ctrl+alt+{(char)('a' + i / 676)}>ctrl+alt+{(char)('a' + i / 26 % 26)}>ctrl+alt+{(char)('a' + i % 26)}";

    [TestMethod]
    [DataRow(1_000)]
    [DataRow(10_000)]
    public void Building_is_linear_in_the_number_of_bindings(int count)
    {
        // Four-key chords, all distinct and none a prefix of another. Placing one looks up its own key and
        // its three prefixes; it must never walk the bindings already made, which is what made the old
        // build quadratic. Counted, not timed, so a busy machine cannot fail it or a fast one hide it.
        var builder = new KeymapBuilder();
        for (var i = 0; i < count; i++)
            builder.Bind(KeymapFixtures.Binding($"ctrl+alt+{(char)('a' + i / 17576)}>{ThreeKeys(i % 17576)}=record"));

        builder.Build().Bindings.Should().HaveCount(count);
        builder.Steps.Should().BeLessThanOrEqualTo(4L * count);
    }

    [TestMethod]
    public void The_step_count_sees_every_binding_a_new_one_displaces()
    {
        // The other side of the bound above: a count that missed the walk under a prefix would pass it
        // however long that walk was.
        var builder = new KeymapBuilder();
        for (var i = 0; i < 26; i++)
            builder.Bind(KeymapFixtures.Binding($"ctrl+a>{(char)('a' + i)}=record"));
        var before = builder.Steps;

        builder.Bind(KeymapFixtures.Binding("ctrl+a=record"));

        builder.Build().Bindings.Should().ContainSingle();
        (builder.Steps - before).Should().Be(1 + 2 * 26, "its own key, then each of the 26 chords under it, visited and removed");
    }
}
