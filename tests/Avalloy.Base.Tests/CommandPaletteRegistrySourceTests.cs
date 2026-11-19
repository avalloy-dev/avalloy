using AwesomeAssertions;
using Avalloy.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Base.Tests;

/// <summary>
/// <see cref="CommandPaletteViewModel.FromRegistry"/> — turning the command registry into palette rows, which
/// is the whole reason <see cref="ICommandSurface"/> named a palette as a consumer.
/// </summary>
[TestClass]
public class CommandPaletteRegistrySourceTests
{
    private static readonly List<string> Ran = [];

    [TestInitialize]
    public void Reset() { Ran.Clear(); Gate.Open = true; }

    [Command("save", DisplayName = "Save", Category = "File", Icon = "Save")]
    private sealed class SaveCommand : IAlloyCommand
    {
        public Task ExecuteAsync() { Ran.Add("save"); return Task.CompletedTask; }
    }

    [Command("plain")]
    private sealed class PlainCommand : IAlloyCommand
    {
        public Task ExecuteAsync() => Task.CompletedTask;
    }

    private static class Gate { public static bool Open = true; }

    [Command("gated", DisplayName = "Gated", Category = "Edit")]
    private sealed class GatedCommand : IConditionalCommand
    {
        public bool CanExecute() => Gate.Open;
        public Task ExecuteAsync() { Ran.Add("gated"); return Task.CompletedTask; }
    }

    /// <summary>Throws before returning a task — the dispatcher call itself blows up.</summary>
    [Command("boom", DisplayName = "Boom")]
    private sealed class ThrowingCommand : IAlloyCommand
    {
        public Task ExecuteAsync() => throw new InvalidOperationException("nope");
    }

    /// <summary>Returns a FAULTED task instead. The commoner shape by far — any `async Task` command that
    /// throws after its first await lands here — and a different code path: the exception arrives on the task,
    /// long after Run() has returned to the UI thread that called it.</summary>
    [Command("boom-async", DisplayName = "Boom Async")]
    private sealed class AsyncThrowingCommand : IAlloyCommand
    {
        public async Task ExecuteAsync()
        {
            await Task.Yield();
            throw new InvalidOperationException("nope, later");
        }
    }

    /// <summary>Registry + dispatcher over the command types this fixture declares, with an optional keymap.</summary>
    private static (CommandRegistry Registry, KeyboardDispatcher Dispatcher) Build(
        Dictionary<string, IReadOnlyList<string>>? keymap = null,
        params Type[] types)
    {
        var registry = new CommandRegistry();
        var services = new ServiceCollection();
        foreach (var t in types)
        {
            registry.Register(t);
            services.AddSingleton(t);
        }

        var dispatcher = new KeyboardDispatcher(
            registry, services.BuildServiceProvider(),
            () => keymap ?? []);
        return (registry, dispatcher);
    }

    [TestMethod]
    public void A_Categorised_Command_Is_Labelled_Category_Then_Name()
    {
        var (r, d) = Build(types: typeof(SaveCommand));

        var rows = CommandPaletteViewModel.FromRegistry(r, d);

        rows.Should().ContainSingle().Which.Label.Should().Be("File: Save",
            "so a category is typeable without a second column to search");
    }

    [TestMethod]
    public void The_Default_General_Category_Is_Not_Printed()
    {
        var (r, d) = Build(types: typeof(PlainCommand));

        rowsOf(r, d).Should().ContainSingle().Which.Label.Should().Be("plain",
            "'General: plain' is noise on every unclassified command");
    }

    /// <summary>"Mod+", not "Ctrl+": the parser renders the platform-default modifier as Mod, so a literal
    /// <c>Ctrl+S</c> expectation is true only on macOS. See <c>KeyboardDispatcherLookupTests</c>.</summary>
    [TestMethod]
    public void The_Hint_Is_The_Shortcut_That_Actually_Fires_It()
    {
        var (r, d) = Build(new() { ["save"] = ["Mod+S"] }, typeof(SaveCommand));

        rowsOf(r, d).Single().Hint.Should().Be("Mod+S");
    }

    [TestMethod]
    public void An_Unbound_Command_Has_No_Hint()
    {
        var (r, d) = Build(types: typeof(SaveCommand));
        rowsOf(r, d).Single().Hint.Should().BeEmpty();
    }

    [TestMethod]
    public void The_Declared_Icon_Becomes_The_Glyph()
    {
        var (r, d) = Build(types: typeof(SaveCommand));
        rowsOf(r, d).Single().Glyph.Should().Be("Save");
    }

    [TestMethod]
    public void A_Command_With_No_Icon_Falls_Back_To_The_Run_Glyph()
    {
        var (r, d) = Build(types: typeof(PlainCommand));
        rowsOf(r, d).Single().Glyph.Should().Be("Enter");
    }

    [TestMethod]
    public void A_Closed_Gate_Keeps_The_Command_Out_Of_The_LIST()
    {
        var (r, d) = Build(types: typeof(GatedCommand), keymap: null);
        Gate.Open = false;

        rowsOf(r, d).Should().BeEmpty(
            "the dispatcher answers a closed gate by silently completing — right for a keystroke, and " +
            "wrong for a row the user can see and pick");
    }

    [TestMethod]
    public void An_Open_Gate_Lets_It_Through()
    {
        var (r, d) = Build(types: typeof(GatedCommand));
        rowsOf(r, d).Should().ContainSingle();
    }

    [TestMethod]
    public void The_Gate_Is_Read_When_The_Rows_Are_BUILT()
    {
        var (r, d) = Build(types: typeof(GatedCommand));

        Gate.Open = false;
        var closed = CommandPaletteViewModel.FromRegistry(r, d);
        Gate.Open = true;
        var open = CommandPaletteViewModel.FromRegistry(r, d);

        closed.Should().BeEmpty();
        open.Should().ContainSingle("the list is built per open, so it reflects the state on screen");
    }

    [TestMethod]
    public void The_Include_Predicate_Filters_On_Top_Of_The_Gate()
    {
        var (r, d) = Build(types: [typeof(SaveCommand), typeof(PlainCommand)]);

        var rows = CommandPaletteViewModel.FromRegistry(r, d, include: c => c.Category == "File");

        rows.Should().ContainSingle().Which.Label.Should().Be("File: Save");
    }

    [TestMethod]
    public void Every_Row_Carries_The_Caller_Group()
    {
        var (r, d) = Build(types: [typeof(SaveCommand), typeof(PlainCommand)]);

        CommandPaletteViewModel.FromRegistry(r, d, group: 3)
            .Should().OnlyContain(x => x.Group == 3);
    }

    [TestMethod]
    public void Running_A_Row_Dispatches_The_Command()
    {
        var (r, d) = Build(types: typeof(SaveCommand));

        rowsOf(r, d).Single().Run();

        Ran.Should().Equal("save");
    }

    [TestMethod]
    public void A_Command_That_Throws_Does_Not_Take_The_Palette_Down()
    {
        var (r, d) = Build(types: typeof(ThrowingCommand));

        var run = rowsOf(r, d).Single().Run;

        run.Should().NotThrow("PaletteItem.Run is invoked from a UI event handler — an escaping " +
                              "exception there is an app crash, not a failed command");
    }

    [TestMethod]
    public async Task A_Command_That_Faults_Its_TASK_Is_Not_An_Unobserved_Exception()
    {
        var (r, d) = Build(types: typeof(AsyncThrowingCommand));
        var unobserved = 0;
        void OnUnobserved(object? _, UnobservedTaskExceptionEventArgs e) => Interlocked.Increment(ref unobserved);
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            rowsOf(r, d).Single().Run();       // returns immediately; the fault lands later

            // Let the continuation run, then force finalization so any UNobserved fault would be raised.
            await Task.Delay(200);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(100);

            unobserved.Should().Be(0,
                "Run() is fire-and-forget, so a faulted task with nobody attached would surface as an " +
                "unobserved exception at some unrelated GC — the continuation is what observes it");
        }
        finally { TaskScheduler.UnobservedTaskException -= OnUnobserved; }
    }

    [TestMethod]
    public void An_Async_Command_That_Faults_Does_Not_Throw_Into_The_Caller()
    {
        var (r, d) = Build(types: typeof(AsyncThrowingCommand));

        var run = rowsOf(r, d).Single().Run;

        run.Should().NotThrow();
    }

    [TestMethod]
    public void An_Empty_Registry_Produces_No_Rows()
    {
        var (r, d) = Build();
        CommandPaletteViewModel.FromRegistry(r, d).Should().BeEmpty();
    }

    [TestMethod]
    public void Registry_Rows_Feed_Straight_Into_A_Palette()
    {
        var (r, d) = Build(new() { ["save"] = ["Mod+S"] }, typeof(SaveCommand), typeof(PlainCommand));

        var vm = new CommandPaletteViewModel(CommandPaletteViewModel.FromRegistry(r, d), () => { });
        vm.Query = "save";
        vm.Activate();

        Ran.Should().Equal("save");
    }

    // ── opt-in: disabled rows and the app's own hints ────────────────────────────────────────────

    [TestMethod]
    public void Kept_Disabled_A_Closed_Gate_Is_A_Disabled_Row_With_Its_Note()
    {
        var (r, d) = Build(types: [typeof(GatedCommand), typeof(SaveCommand)]);
        Gate.Open = false;

        var rows = CommandPaletteViewModel.FromRegistryWith(r, d, new RegistryPaletteOptions
        {
            KeepDisabled = true,
            DisabledNote = c => $"{c.Name} is off",
        });

        var gated = rows.Single(x => x.Key == "gated");
        gated.IsEnabled.Should().BeFalse();
        gated.Note.Should().Be("gated is off");
        var save = rows.Single(x => x.Key == "save");
        save.IsEnabled.Should().BeTrue();
        save.Note.Should().BeEmpty("only a disabled row says why it is disabled");
    }

    [TestMethod]
    public void Kept_Disabled_Is_Read_Per_Build_Like_The_Gate_Itself()
    {
        var (r, d) = Build(types: typeof(GatedCommand));
        var options = new RegistryPaletteOptions { KeepDisabled = true };

        Gate.Open = false;
        var closed = CommandPaletteViewModel.FromRegistryWith(r, d, options).Single();
        Gate.Open = true;
        var open = CommandPaletteViewModel.FromRegistryWith(r, d, options).Single();

        closed.IsEnabled.Should().BeFalse();
        open.IsEnabled.Should().BeTrue();
    }

    [TestMethod]
    public void A_Kept_Disabled_Row_Cannot_Be_Run_From_The_Palette()
    {
        var (r, d) = Build(types: [typeof(GatedCommand), typeof(SaveCommand)]);
        Gate.Open = false;
        var vm = new CommandPaletteViewModel(
            CommandPaletteViewModel.FromRegistryWith(r, d, new RegistryPaletteOptions { KeepDisabled = true }), () => { });

        vm.Results.Select(x => x.Key).Should().Equal(["save", "gated"], "the disabled row sinks to the bottom");
        vm.Move(+1);
        vm.Activate();

        Ran.Should().Equal(["save"], "↓ from the only enabled row wraps back to it");
    }

    [TestMethod]
    public void The_Apps_Own_Hints_Replace_The_Dispatcher_Gesture()
    {
        var (r, d) = Build(new() { ["save"] = ["Mod+S"] }, typeof(SaveCommand));

        var row = CommandPaletteViewModel.FromRegistryWith(r, d, new RegistryPaletteOptions
        {
            Hints = c => c.Name == "save" ? ["⌘S", "⌃X ⌃S", "F2"] : [],
        }).Single();

        row.Hints.Should().Equal(["⌘S", "⌃X ⌃S", "F2"]);
        row.Hint.Should().BeEmpty("the app's notation replaces the dispatcher's, rather than adding a second copy");
        row.HintText.Should().Be("⌘S  ⌃X ⌃S  +1");
    }

    [TestMethod]
    public void Every_Dispatcher_Gesture_Can_Be_Shown()
    {
        var (r, d) = Build(new() { ["save"] = ["Mod+S", "F2"] }, typeof(SaveCommand));

        var row = CommandPaletteViewModel.FromRegistryWith(r, d, new RegistryPaletteOptions { Hints = c => d.GesturesFor(c.Name) }).Single();

        row.Hints.Should().BeEquivalentTo(["Mod+S", "F2"]);
    }

    [TestMethod]
    public void The_Options_Overload_With_Defaults_Matches_The_Original()
    {
        var (r, d) = Build(new() { ["save"] = ["Mod+S"] }, [typeof(SaveCommand), typeof(GatedCommand)]);
        Gate.Open = false;

        var original = CommandPaletteViewModel.FromRegistry(r, d);
        var withOptions = CommandPaletteViewModel.FromRegistryWith(r, d, new RegistryPaletteOptions());

        withOptions.Select(x => (x.Label, x.Hint, x.Glyph, x.Group, x.IsEnabled, x.Note, x.Hints.Count))
            .Should().Equal(original.Select(x => (x.Label, x.Hint, x.Glyph, x.Group, x.IsEnabled, x.Note, x.Hints.Count)));
        original.Should().ContainSingle().Which.Label.Should().Be("File: Save");
    }

    [TestMethod]
    public void Registry_Rows_Are_Keyed_By_Command_Name_For_Recents()
    {
        var (r, d) = Build(types: [typeof(SaveCommand), typeof(PlainCommand)]);

        var vm = CommandPaletteViewModel.Create(CommandPaletteViewModel.FromRegistry(r, d), () => { },
            new PaletteOptions { Recent = ["plain"] });

        vm.Results.Select(x => x.Label).Should().Equal(["plain", "File: Save"]);
    }

    private static IReadOnlyList<PaletteItem> rowsOf(CommandRegistry r, KeyboardDispatcher d) =>
        CommandPaletteViewModel.FromRegistry(r, d);
}
