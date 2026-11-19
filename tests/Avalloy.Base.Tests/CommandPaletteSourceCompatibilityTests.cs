using AwesomeAssertions;
using Avalloy.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Base.Tests;

/// <summary>
/// Calls that compiled against Avalloy before the palette options existed, written exactly as an app may have
/// written them — positional <c>null</c> included. The assertions are the small part: the real test is that
/// this file COMPILES. An overload added beside the original entry points whose parameter at a <c>null</c>
/// position is another reference type (a <c>PaletteOptions</c> where a <c>string? placeholder</c> was, a
/// <c>RegistryPaletteOptions</c> where a <c>Func</c> was) makes these calls ambiguous (CS0121) and the build
/// fails, which is why the option entry points have names of their own.
/// </summary>
[TestClass]
public class CommandPaletteSourceCompatibilityTests
{
    [TestMethod]
    public void The_Original_Constructor_Takes_A_Positional_Null_Placeholder()
    {
        PaletteItem[] items = [new PaletteItem { Label = "One", Run = () => { } }];

        var vm = new CommandPaletteViewModel(items, () => { }, null);

        vm.Placeholder.Should().Be(CommandPaletteViewModel.GlobalPlaceholder);
        new CommandPaletteViewModel(items, () => { }).Results.Should().ContainSingle();
    }

    [Command("compat.save", DisplayName = "Save")]
    private sealed class SaveCommand : IAlloyCommand
    {
        public Task ExecuteAsync() => Task.CompletedTask;
    }

    [TestMethod]
    public void The_Original_FromRegistry_Takes_A_Positional_Null_Include()
    {
        // A registered command, so the row loop runs: a null that bound to an options overload would be
        // dereferenced there rather than meaning "no include filter".
        var registry = new CommandRegistry();
        registry.Register(typeof(SaveCommand));
        var services = new ServiceCollection().AddSingleton<SaveCommand>().BuildServiceProvider();
        var dispatcher = new KeyboardDispatcher(registry, services, () => new Dictionary<string, IReadOnlyList<string>>());

        CommandPaletteViewModel.FromRegistry(registry, dispatcher, null).Should().ContainSingle().Which.Label.Should().Be("Save");
        CommandPaletteViewModel.FromRegistry(registry, dispatcher, null, 0).Should().ContainSingle();
        CommandPaletteViewModel.FromRegistry(registry, dispatcher).Should().ContainSingle();
    }
}
