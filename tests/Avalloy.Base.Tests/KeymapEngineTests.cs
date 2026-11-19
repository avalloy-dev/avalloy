using Avalonia.Input;
using Avalloy.Commands;
using Avalloy.Commands.Keymaps;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Base.Tests;

[TestClass]
public class KeymapEngineTests
{
    [Command("record", Category = "Keymap")]
    public sealed class Recording : IParameterizedCommand
    {
        public bool Ready { get; set; } = true;
        public List<(string? Parameter, object? Target)> Ran { get; } = [];
        public Func<Task>? During { get; set; }
        public bool CanExecute(string? parameter, object? target = null) => Ready && parameter != "blocked";
        public async Task ExecuteAsync(string? parameter, object? target = null)
        {
            Ran.Add((parameter, target));
            if (During is { } run) await run();
        }
    }

    /// <summary>A registered command that reports its own readiness, behind a conditional gate too.</summary>
    [Command("stub", Category = "Keymap")]
    public sealed class Stub : IParameterizedCommand, IConditionalCommand
    {
        public ActionReadiness Own { get; set; } = ActionReadiness.Unavailable;
        public bool Gate { get; set; } = true;
        public int Ran { get; private set; }
        public ActionReadiness Readiness(string? parameter, object? target = null) => Own;
        public bool CanExecute() => Gate;
        public Task ExecuteAsync(string? parameter, object? target = null) { Ran++; return Task.CompletedTask; }
    }

    internal sealed class Harness
    {
        public Recording Command { get; } = new();
        public Stub Stub { get; } = new();
        public CommandRegistry Registry { get; } = new();
        public KeyboardDispatcher Commands { get; }
        public KeymapDispatcher Keys { get; }
        public KeyboardState State { get; } = new(new object());
        public Harness(params string[] bindings)
        {
            Registry.Register<Recording>();
            Registry.Register<Stub>();
            Commands = new(Registry, new ServiceCollection().AddSingleton(Command).AddSingleton(Stub).BuildServiceProvider(), () => new Dictionary<string, IReadOnlyList<string>>());
            Keys = Commands.CreateKeymapDispatcher(KeymapFixtures.Map(bindings));
        }
        public Task Press(KeyEventArgs e) => Keys.OnKeyDown(e, null, State);
    }

    internal static KeyEventArgs Down(Key key, PhysicalKey physical = PhysicalKey.None,
        KeyModifiers mods = KeyModifiers.None, string? symbol = null) => new()
    { RoutedEvent = InputElement.KeyDownEvent, Key = key, PhysicalKey = physical, KeyModifiers = mods, KeySymbol = symbol };
    internal static KeyEventArgs Up(Key key, PhysicalKey physical = PhysicalKey.None) => new()
    { RoutedEvent = InputElement.KeyUpEvent, Key = key, PhysicalKey = physical };

    [TestMethod]
    public async Task Parameter_and_origin_target_survive_async_chain_and_another_window()
    {
        var h = new Harness();
        var a = h.State;
        var b = new KeyboardState(new object());
        h.Keys.Keymap = new KeymapBuilder().Bind(new([KeymapFixtures.Trigger("ctrl+k")],
            [new("record", "first"), new("record", "second")])).Build();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Command.During = () => completion.Task;
        var execution = h.Keys.OnKeyDown(Down(Key.K, PhysicalKey.K, KeyModifiers.Control, "k"), null, a);
        h.Command.Ran.Should().Equal(("first", a.Owner));
        b.Deactivate();
        completion.SetResult();
        await execution;
        h.Command.Ran.Should().Equal(("first", a.Owner), ("second", a.Owner));
    }

    [TestMethod]
    [DataRow(false, false, false, true)]
    [DataRow(false, true, false, false)]
    [DataRow(true, true, false, true)]
    [DataRow(true, false, true, false)]
    [DataRow(false, false, true, false)]
    public async Task Consumption_obeys_readiness_and_options(bool ready, bool conditional, bool pass, bool consumed)
    {
        var h = new Harness();
        var options = (conditional ? KeyBindingOptions.ConsumeIfExecutable : 0) | (pass ? KeyBindingOptions.PassThrough : 0);
        h.Keys.Keymap = new KeymapBuilder().Bind(new([KeymapFixtures.Trigger("ctrl+k")], [new("record", "3")], options)).Build();
        h.Command.Ready = ready;
        var e = Down(Key.K, PhysicalKey.K, KeyModifiers.Control, "k");
        await h.Press(e);
        e.Handled.Should().Be(consumed);
        h.Command.Ran.Count.Should().Be(ready ? 1 : 0);
        var up = Up(Key.K, PhysicalKey.K);
        h.Keys.OnKeyUp(up, h.State).Should().Be(consumed);
    }

    [TestMethod]
    public async Task Missing_command_passes_and_suppression_has_no_reserved_action_names()
    {
        var h = new Harness("ctrl+k=missing");
        var e = Down(Key.K, PhysicalKey.K, KeyModifiers.Control, "k");
        await h.Press(e); e.Handled.Should().BeFalse();
        h.Keys.Keymap = new KeymapBuilder().Bind(new([KeymapFixtures.Trigger("ctrl+k")], [], KeyBindingOptions.Suppress)).Build();
        e = Down(Key.K, PhysicalKey.K, KeyModifiers.Control, "k");
        await h.Press(e); e.Handled.Should().BeTrue();
    }

    [TestMethod]
    public async Task A_registered_command_reporting_unavailable_passes_its_key_and_release_through()
    {
        var h = new Harness("ctrl+k=stub");
        h.Keys.Readiness(new("stub"), h.State.Owner).Should().Be(ActionReadiness.Unavailable);
        var e = Down(Key.K, PhysicalKey.K, KeyModifiers.Control, "k");
        await h.Press(e);
        e.Handled.Should().BeFalse();
        h.Keys.OnKeyUp(Up(Key.K, PhysicalKey.K), h.State).Should().BeFalse();
        // The same binding consumes once the command says only "not now".
        h.Stub.Own = ActionReadiness.NotNow;
        var notNow = Down(Key.K, PhysicalKey.K, KeyModifiers.Control, "k");
        await h.Press(notNow);
        notNow.Handled.Should().BeTrue();
        h.Keys.OnKeyUp(Up(Key.K, PhysicalKey.K), h.State).Should().BeTrue();
        h.Stub.Own = ActionReadiness.Unavailable;
        // Nor does running it by name reach a command that says it is not there.
        h.Commands.CanRun("stub").Should().BeFalse();
        await h.Commands.RunByNameAsync("stub", null, h.State.Owner);
        h.Stub.Ran.Should().Be(0);
    }

    [TestMethod]
    [DataRow(ActionReadiness.Unavailable, false, ActionReadiness.Unavailable)]
    [DataRow(ActionReadiness.NotNow, true, ActionReadiness.NotNow)]
    [DataRow(ActionReadiness.Ready, true, ActionReadiness.Ready)]
    [DataRow(ActionReadiness.Ready, false, ActionReadiness.NotNow)]
    public void A_commands_own_unavailable_outranks_a_closed_conditional_gate(ActionReadiness own, bool gate, ActionReadiness expected)
    {
        var h = new Harness();
        h.Stub.Own = own; h.Stub.Gate = gate;
        h.Commands.Readiness("stub", null).Should().Be(expected);
        h.Commands.CanRun("stub").Should().Be(expected == ActionReadiness.Ready);
    }

    [TestMethod]
    public void Default_readiness_follows_can_execute()
    {
        var h = new Harness();
        h.Commands.Readiness("record", "3").Should().Be(ActionReadiness.Ready);
        h.Commands.Readiness("record", "blocked").Should().Be(ActionReadiness.NotNow);
        h.Commands.Readiness("missing", null).Should().Be(ActionReadiness.Unavailable);
    }

    [TestMethod]
    public async Task Ime_processed_is_never_matched_or_peeked_even_with_a_pending_chord()
    {
        var h = new Harness("ctrl+a>h=record");
        await h.Press(Down(Key.A, PhysicalKey.A, KeyModifiers.Control, "a"));
        var ime = Down(Key.ImeProcessed, PhysicalKey.H, symbol: "h");
        await h.Press(ime);
        ime.Handled.Should().BeFalse(); h.Keys.Peek(ime).Should().BeNull();
        h.Command.Ran.Should().BeEmpty(); h.State.Chord.IsPending.Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_cmd_keyup_does_not_swallow_next_press_or_its_release()
    {
        var h = new Harness("super+t=record");
        await h.Press(Down(Key.T, PhysicalKey.T, KeyModifiers.Meta, "t"));
        h.Keys.OnKeyUp(Up(Key.LWin, PhysicalKey.MetaLeft), h.State);
        var next = Down(Key.T, PhysicalKey.T, symbol: "t");
        await h.Press(next);
        next.Handled.Should().BeFalse();
        h.Keys.OnKeyUp(Up(Key.T, PhysicalKey.T), h.State).Should().BeFalse();
    }

    [TestMethod]
    public async Task Deactivation_cancels_pending_state_without_replay_and_preserves_release_pairing()
    {
        var h = new Harness("ctrl+a>h=record");
        await h.Press(Down(Key.A, PhysicalKey.A, KeyModifiers.Control, "a"));
        h.State.Deactivate();
        h.State.Chord.IsPending.Should().BeFalse(); h.State.TerminalDown.Should().BeEmpty();
        var hKey = Down(Key.H, PhysicalKey.H, symbol: "h");
        await h.Press(hKey); hKey.Handled.Should().BeFalse(); h.Command.Ran.Should().BeEmpty();
        h.Keys.OnKeyUp(Up(Key.A, PhysicalKey.A), h.State).Should().BeTrue();
    }

    [TestMethod]
    public async Task Palette_keys_suppress_only_releases_the_control_is_not_owed()
    {
        var h = new Harness();
        await h.Press(Down(Key.X, PhysicalKey.X, symbol: "x"));
        KeymapDispatcher.OnPaletteKeyDown(Down(Key.X, PhysicalKey.X), h.State);
        KeymapDispatcher.OnPaletteKeyDown(Down(Key.LeftShift, PhysicalKey.ShiftLeft), h.State);
        h.Keys.OnKeyUp(Up(Key.X, PhysicalKey.X), h.State).Should().BeFalse();
        h.Keys.OnKeyUp(Up(Key.LeftShift, PhysicalKey.ShiftLeft), h.State).Should().BeTrue();
    }

    [TestMethod]
    public async Task Registry_and_palette_preserve_parameter_and_target_and_display_labels()
    {
        var h = new Harness("super+3=record:3", "super+4=record:blocked");
        h.Commands.CanRun("record", "blocked").Should().BeFalse();
        await h.Commands.RunByNameAsync("record", "3", h.State.Owner);
        var rows = CommandPaletteViewModel.FromKeymap(h.Registry, h.Keys, h.State.Owner, KeymapPlatform.MacOS);
        rows.Should().ContainSingle(); rows[0].Hints.Should().Equal("⌘3");
        rows[0].Run();
        h.Command.Ran.Should().Equal(("3", h.State.Owner), ("3", h.State.Owner));
    }

    [TestMethod]
    public void Prefix_conflicts_unbinding_and_snapshots_match_insertion_order()
    {
        var builder = new KeymapBuilder().Bind(KeymapFixtures.Binding("ctrl+a=record"));
        builder.Bind(KeymapFixtures.Binding("ctrl+a>h=record:1")).Bind(KeymapFixtures.Binding("ctrl+a>n=record:2"));
        var before = builder.Build();
        before.Bindings.Should().HaveCount(2);
        builder.Bind(KeymapFixtures.Binding("ctrl+a=record:3"));
        builder.Build().Bindings.Should().ContainSingle().Which.Action!.Parameter.Should().Be("3");
        before.Bindings.Should().HaveCount(2);
        builder.Unbind([KeymapFixtures.Trigger("ctrl+a")]);
        builder.Build().Bindings.Should().BeEmpty();
        var sequence = new List<KeyTrigger> { KeymapFixtures.Trigger("x") };
        var actions = new List<CommandInvocation> { new("record", "old") };
        builder.Bind(new(sequence, actions)); sequence.Clear(); actions.Clear();
        builder.Build().Bindings.Should().ContainSingle().Which.Action!.Parameter.Should().Be("old");
    }

    [TestMethod]
    public void Labels_and_virtual_letters_follow_platform_and_layout()
    {
        var sequence = KeymapFixtures.Binding("ctrl+alt+shift+super+p=record").Sequence;
        KeyNames.Display(sequence, KeymapPlatform.MacOS).Should().Be("⌃⌥⇧⌘P");
        KeyNames.Display(sequence, KeymapPlatform.Windows).Should().Be("Ctrl+Alt+Shift+Win+P");
        KeyNames.Display(sequence, KeymapPlatform.Linux).Should().Be("Ctrl+Alt+Shift+Super+P");
        var e = Down(Key.A, PhysicalKey.Q, KeyModifiers.Control | KeyModifiers.Shift);
        var map = KeymapFixtures.Map("ctrl+shift+a=record", "ctrl+shift+q=missing");
        foreach (var platform in Enum.GetValues<KeymapPlatform>())
            SequenceMatcher.Peek(map, KeymapDispatcher.ToPress(e, platform))!.Action!.Name.Should().Be("record");
    }
    [TestMethod]
    public void Every_readiness_option_combination_has_explicit_consumption_rules()
    {
        foreach (var readiness in Enum.GetValues<ActionReadiness>())
        foreach (var conditional in new[] { false, true })
        foreach (var pass in new[] { false, true })
        foreach (var suppress in new[] { false, true })
        {
            var options = (conditional ? KeyBindingOptions.ConsumeIfExecutable : 0)
                | (pass ? KeyBindingOptions.PassThrough : 0) | (suppress ? KeyBindingOptions.Suppress : 0);
            var binding = new KeymapBinding([KeymapFixtures.Trigger("k")], [new("record")], options);
            var decision = KeyDecision.For(binding, _ => readiness);
            decision.Run.Should().Be(readiness == ActionReadiness.Ready);
            var consumes = suppress || (!pass && readiness != ActionReadiness.Unavailable
                && (!conditional || readiness == ActionReadiness.Ready));
            decision.Consume.Should().Be(consumes);
        }
        var chain = new KeymapBinding([KeymapFixtures.Trigger("k")], [new("missing"), new("record")]);
        KeyDecision.For(chain, a => a.Name == "record" ? ActionReadiness.Ready : ActionReadiness.Unavailable)
            .Should().Be(new KeyDecision(true, true));
    }

    [TestMethod]
    public void Unbinding_a_prefix_removes_descendants_and_lookup_tokens_cannot_collide()
    {
        var builder = new KeymapBuilder().Bind(KeymapFixtures.Binding("ctrl+a>h=record"))
            .Bind(KeymapFixtures.Binding("ctrl+a>n=record"));
        builder.Unbind([KeymapFixtures.Trigger("ctrl+a")]);
        builder.Build().Bindings.Should().BeEmpty();
        var one = new KeymapBinding([new(KeyModifiers.None, KeyKind.Unicode, ">")], [new("record", "one")]);
        var two = new KeymapBinding([KeymapFixtures.Trigger("a"), KeymapFixtures.Trigger("b")], [new("record", "two")]);
        var map = builder.Bind(one).Bind(two).Build();
        map.Find(Keymap.KeyOf(one.Sequence))!.Action!.Parameter.Should().Be("one");
        map.Find(Keymap.KeyOf(two.Sequence))!.Action!.Parameter.Should().Be("two");
        KeyNames.Display(new KeyGesture(Key.P, KeyModifiers.Meta | KeyModifiers.Shift), KeymapPlatform.MacOS)
            .Should().Be("⇧⌘P");
    }

    [TestMethod]
    public async Task Chords_are_per_window_and_handled_events_are_untouched()
    {
        var h = new Harness("ctrl+a>h=record");
        await h.Press(Down(Key.A, PhysicalKey.A, KeyModifiers.Control, "a"));
        var other = new KeyboardState(new object());
        var e = Down(Key.H, PhysicalKey.H, symbol: "h");
        await h.Keys.OnKeyDown(e, null, other); e.Handled.Should().BeFalse();
        h.State.Chord.IsPending.Should().BeTrue();
        var handled = Down(Key.H, PhysicalKey.H, symbol: "h"); handled.Handled = true;
        await h.Press(handled); h.Command.Ran.Should().BeEmpty();
        await h.Press(Down(Key.H, PhysicalKey.H, symbol: "h"));
        h.Command.Ran.Should().ContainSingle().Which.Target.Should().BeSameAs(h.State.Owner);
    }

    [TestMethod]
    public async Task Control_owned_keyup_survives_consumed_repeat_after_readiness_changes()
    {
        var h = new Harness("performable:k=record");
        h.Command.Ready = false;
        await h.Press(Down(Key.K, PhysicalKey.K, symbol: "k"));
        h.Command.Ready = true;
        var repeat = Down(Key.K, PhysicalKey.K, symbol: "k");
        await h.Press(repeat); repeat.Handled.Should().BeTrue();
        h.Keys.OnKeyUp(Up(Key.K, PhysicalKey.K), h.State).Should().BeFalse();
        h.State.TerminalDown.Should().BeEmpty();
        h.State.SwallowRelease.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Control_owned_keyup_survives_consumed_repeat_after_modifiers_change()
    {
        var h = new Harness("ctrl+k=record");
        await h.Press(Down(Key.K, PhysicalKey.K, symbol: "k"));
        var repeat = Down(Key.K, PhysicalKey.K, KeyModifiers.Control, "k");
        await h.Press(repeat); repeat.Handled.Should().BeTrue();
        h.Keys.OnKeyUp(Up(Key.K, PhysicalKey.K), h.State).Should().BeFalse();
        h.State.TerminalDown.Should().BeEmpty();
        h.State.SwallowRelease.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Missing_Cmd_leader_keyup_allows_same_key_to_complete_after_modifier_release()
    {
        var h = new Harness("super+k>k=record");
        await h.Press(Down(Key.K, PhysicalKey.K, KeyModifiers.Meta, "k"));
        h.Keys.OnKeyUp(Up(Key.LWin, PhysicalKey.MetaLeft), h.State);
        h.State.Chord.IsPending.Should().BeTrue();
        var next = Down(Key.K, PhysicalKey.K, symbol: "k");
        await h.Press(next);
        next.Handled.Should().BeTrue();
        h.State.Chord.IsPending.Should().BeFalse();
        h.Command.Ran.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Missing_Cmd_leader_keyup_detects_modifier_change_without_modifier_event()
    {
        var h = new Harness("super+k>k=record");
        await h.Press(Down(Key.K, PhysicalKey.K, KeyModifiers.Meta, "k"));
        await h.Press(Down(Key.K, PhysicalKey.K, symbol: "k"));
        h.Command.Ran.Should().ContainSingle();
        h.State.Chord.IsPending.Should().BeFalse();
    }

    [TestMethod]
    public async Task Missing_Cmd_leader_keyup_after_modifier_cycle_distinguishes_fresh_press_from_repeat()
    {
        var h = new Harness("super+k>super+k=record");
        await h.Press(Down(Key.K, PhysicalKey.K, KeyModifiers.Meta, "k"));
        await h.Press(Down(Key.K, PhysicalKey.K, KeyModifiers.Meta, "k"));
        h.Command.Ran.Should().BeEmpty("the second event is still an auto-repeat");
        h.Keys.OnKeyUp(Up(Key.LWin, PhysicalKey.MetaLeft), h.State);
        await h.Press(Down(Key.LWin, PhysicalKey.MetaLeft, KeyModifiers.Meta));
        h.State.Chord.IsPending.Should().BeTrue();
        await h.Press(Down(Key.K, PhysicalKey.K, KeyModifiers.Meta, "k"));
        h.Command.Ran.Should().ContainSingle();
        h.State.Chord.IsPending.Should().BeFalse();
    }

    [TestMethod]
    public async Task Cmd_leader_late_keyup_is_suppressed_once_after_modifier_release()
    {
        var h = new Harness("super+k>k=record");
        await h.Press(Down(Key.K, PhysicalKey.K, KeyModifiers.Meta, "k"));
        h.Keys.OnKeyUp(Up(Key.LWin, PhysicalKey.MetaLeft), h.State);
        h.Keys.OnKeyUp(Up(Key.K, PhysicalKey.K), h.State).Should().BeTrue();
        h.Keys.OnKeyUp(Up(Key.K, PhysicalKey.K), h.State).Should().BeFalse();
        h.State.Chord.IsPending.Should().BeTrue();
        await h.Press(Down(Key.K, PhysicalKey.K, symbol: "k"));
        h.Command.Ran.Should().ContainSingle();
    }

}
