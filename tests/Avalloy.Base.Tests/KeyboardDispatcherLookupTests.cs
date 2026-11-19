using Avalonia.Input;
using AwesomeAssertions;
using Avalloy.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Base.Tests;

/// <summary>
/// The two lookups a surface that LISTS commands needs: "would this actually run?" and "what fires it?".
/// Both are the inverse of what a keystroke needs, which is why neither existed until a palette wanted them.
/// </summary>
[TestClass]
public class KeyboardDispatcherLookupTests
{
    private static class Gate { public static bool Open = true; }

    [TestInitialize]
    public void Reset() { Gate.Open = true; Ran.Clear(); }

    private static readonly List<string> Ran = [];

    [Command("save")]
    private sealed class SaveCommand : IAlloyCommand
    {
        public Task ExecuteAsync() { Ran.Add("save"); return Task.CompletedTask; }
    }

    [Command("typing-safe", AllowWhenTextInputFocused = true)]
    private sealed class TypingSafeCommand : IAlloyCommand
    {
        public Task ExecuteAsync() { Ran.Add("typing-safe"); return Task.CompletedTask; }
    }

    [Command("gated")]
    private sealed class GatedCommand : IConditionalCommand
    {
        public bool CanExecute() => Gate.Open;
        public Task ExecuteAsync() => Task.CompletedTask;
    }

    private static KeyboardDispatcher Build(
        Dictionary<string, IReadOnlyList<string>>? keymap = null, params Type[] types)
    {
        var registry = new CommandRegistry();
        var services = new ServiceCollection();
        foreach (var t in types) { registry.Register(t); services.AddSingleton(t); }
        return new KeyboardDispatcher(registry, services.BuildServiceProvider(), () => keymap ?? []);
    }

    // ── CanRun ───────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void An_Unconditional_Command_Can_Always_Run()
        => Build(types: typeof(SaveCommand)).CanRun("save").Should().BeTrue();

    [TestMethod]
    public void An_Unregistered_Name_Cannot_Run()
        => Build(types: typeof(SaveCommand)).CanRun("nope").Should().BeFalse();

    [TestMethod]
    public void A_Conditional_Command_Follows_Its_Gate()
    {
        var d = Build(types: typeof(GatedCommand));

        d.CanRun("gated").Should().BeTrue();
        Gate.Open = false;
        d.CanRun("gated").Should().BeFalse("the gate is read per call, not cached at registration");
    }

    // ── GesturesFor ──────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void An_Unbound_Command_Has_No_Gestures()
        => Build(types: typeof(SaveCommand)).GesturesFor("save").Should().BeEmpty();

    /// <summary>
    /// "Mod+" throughout, not "Ctrl+", and that is not cosmetic: <see cref="GestureParser"/> renders the
    /// PLATFORM-DEFAULT modifier as <c>Mod</c>, so a keymap saying <c>Ctrl+S</c> round-trips as <c>Mod+S</c>
    /// everywhere except macOS. Asserting the literal <c>Ctrl+S</c> passes on a Mac and fails on Linux and
    /// Windows — which is exactly what it did. Mod is also what a real preferences.json holds.
    /// </summary>
    [TestMethod]
    public void A_Bound_Command_Reports_Its_Gesture()
        => Build(new() { ["save"] = ["Mod+S"] }, typeof(SaveCommand))
            .GesturesFor("save").Should().Equal("Mod+S");

    /// <summary>The platform-relative half, stated once so the tests above can stay literal: whatever the
    /// parser makes of a gesture, <c>GesturesFor</c> gives back the parser's own portable spelling.</summary>
    [TestMethod]
    public void A_Gesture_Comes_Back_Exactly_As_The_Parser_Spells_It()
    {
        var d = Build(new() { ["save"] = ["Ctrl+S"] }, typeof(SaveCommand));

        d.GesturesFor("save").Should().Equal(
            [GestureParser.ToPortableString(GestureParser.TryParse("Ctrl+S")!)],
            "on macOS that is Ctrl+S; everywhere else Ctrl IS the platform default and spells Mod+S");
    }

    [TestMethod]
    public void Several_Bindings_All_Come_Back_In_A_Stable_Order()
    {
        var d = Build(new() { ["save"] = ["Mod+S", "Mod+Shift+S", "Alt+S"] }, typeof(SaveCommand));

        d.GesturesFor("save").Should().Equal(d.GesturesFor("save"),
            "the inverted map has no order of its own — an unsorted answer would reshuffle a menu " +
            "between renders");
        d.GesturesFor("save").Should().HaveCount(3);
    }

    [TestMethod]
    public void A_Gesture_Another_Command_Won_Is_Not_Reported()
    {
        // Both claim Mod+S; the inverted map is last-write-wins, so "gated" holds it.
        var d = Build(new() { ["save"] = ["Mod+S"], ["gated"] = ["Mod+S"] },
            typeof(SaveCommand), typeof(GatedCommand));

        d.GesturesFor("gated").Should().Equal("Mod+S");
        d.GesturesFor("save").Should().BeEmpty(
            "printing Mod+S beside Save would name a shortcut that no longer triggers it");
    }

    [TestMethod]
    public void An_Unparseable_Gesture_Is_Dropped_Rather_Than_Shown()
        => Build(new() { ["save"] = ["Mod+S", "not-a-gesture"] }, typeof(SaveCommand))
            .GesturesFor("save").Should().Equal("Mod+S");

    [TestMethod]
    public void GesturesFor_Is_The_Inverse_Of_CommandForGesture()
    {
        var d = Build(new() { ["save"] = ["Mod+S"] }, typeof(SaveCommand));

        d.CommandForGesture(d.GesturesFor("save").Single()).Should().Be("save");
    }

    [TestMethod]
    public void Rebuild_Is_Reflected_In_Both_Lookups()
    {
        var keymap = new Dictionary<string, IReadOnlyList<string>> { ["save"] = ["Mod+S"] };
        var registry = new CommandRegistry();
        var services = new ServiceCollection();
        registry.Register(typeof(SaveCommand));
        services.AddSingleton(typeof(SaveCommand));
        var d = new KeyboardDispatcher(registry, services.BuildServiceProvider(), () => keymap);

        keymap["save"] = ["Mod+Alt+S"];
        d.Rebuild();

        d.GesturesFor("save").Should().Equal("Mod+Alt+S");
    }
}

/// <summary>
/// Dispatch itself — the path <see cref="KeyboardDispatcherLookupTests"/>' two lookups are the inverse of.
/// It had no coverage until the palette needed a neighbour of it, and its return value carries meaning that
/// is easy to get wrong: null means "not mine, keep looking", and a Task means "handled, stop".
/// </summary>
[TestClass]
public class KeyboardDispatcherDispatchTests
{
    private static readonly List<string> Ran = [];

    [TestInitialize]
    public void Reset() => Ran.Clear();

    [Command("save")]
    private sealed class SaveCommand : IAlloyCommand
    {
        public Task ExecuteAsync() { Ran.Add("save"); return Task.CompletedTask; }
    }

    [Command("typing-safe", AllowWhenTextInputFocused = true)]
    private sealed class TypingSafeCommand : IAlloyCommand
    {
        public Task ExecuteAsync() { Ran.Add("typing-safe"); return Task.CompletedTask; }
    }

    private static KeyboardDispatcher Build(
        Dictionary<string, IReadOnlyList<string>>? keymap = null, params Type[] types)
    {
        var registry = new CommandRegistry();
        var services = new ServiceCollection();
        foreach (var t in types) { registry.Register(t); services.AddSingleton(t); }
        return new KeyboardDispatcher(registry, services.BuildServiceProvider(), () => keymap ?? []);
    }

    /// <summary>What "Mod+" resolves to here — Cmd on macOS, Ctrl elsewhere. Pressing a hardcoded
    /// <see cref="KeyModifiers.Control"/> against a Mod+ binding matches on Linux/Windows and misses on
    /// macOS, which is the same platform trap from the other direction.</summary>
    private static KeyModifiers PrimaryModifier =>
        OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    private static KeyEventArgs Press(Key key, KeyModifiers mods = KeyModifiers.None) =>
        new() { Key = key, KeyModifiers = mods, RoutedEvent = InputElement.KeyDownEvent };

    [TestMethod]
    public async Task A_Bound_Gesture_Runs_Its_Command()
    {
        var d = Build(new() { ["save"] = ["Mod+S"] }, typeof(SaveCommand));

        var task = d.TryDispatch(Press(Key.S, PrimaryModifier));

        task.Should().NotBeNull("a non-null task is how the caller knows to mark the event handled");
        await task!;
        Ran.Should().Equal(["save"]);
    }

    [TestMethod]
    public void An_Unbound_Gesture_Is_Declined()
        => Build(new() { ["save"] = ["Mod+S"] }, typeof(SaveCommand))
            .TryDispatch(Press(Key.Q, PrimaryModifier))
            .Should().BeNull("null lets other key handlers downstream still get their shot");

    /// <summary>
    /// A keymap can name a command that is not registered yet — that is the normal state during an
    /// incremental migration. Claiming the gesture anyway would shadow whatever else wanted it.
    /// </summary>
    [TestMethod]
    public void A_Gesture_Bound_To_An_Unregistered_Command_Is_Declined()
        => Build(new() { ["not-registered-yet"] = ["Mod+S"] })
            .TryDispatch(Press(Key.S, PrimaryModifier))
            .Should().BeNull();

    [TestMethod]
    public void An_Ordinary_Command_Is_Withheld_While_Typing()
        => Build(new() { ["save"] = ["Mod+S"] }, typeof(SaveCommand))
            .TryDispatch(Press(Key.S, PrimaryModifier), textInputFocused: true)
            .Should().BeNull("a text field's own editing chords must win over app commands");

    [TestMethod]
    public async Task A_Command_That_Opts_In_Still_Fires_While_Typing()
    {
        var d = Build(new() { ["typing-safe"] = ["Mod+T"] }, typeof(TypingSafeCommand));

        var task = d.TryDispatch(Press(Key.T, PrimaryModifier), textInputFocused: true);

        task.Should().NotBeNull();
        await task!;
        Ran.Should().Equal(["typing-safe"]);
    }

    // ── the fallback ─────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void The_Fallback_Is_Offered_Only_What_The_Keymap_Declined()
    {
        var seen = new List<KeyGesture>();
        var d = Build(new() { ["save"] = ["Mod+S"] }, typeof(SaveCommand));
        d.SetGestureFallback((g, _) => { seen.Add(g); return Task.CompletedTask; });

        d.TryDispatch(Press(Key.S, PrimaryModifier));   // the keymap claims this
        d.TryDispatch(Press(Key.Q, PrimaryModifier));   // nothing does

        seen.Should().ContainSingle().Which.Key.Should().Be(Key.Q,
            "the static keymap is authoritative — a registered command can never be shadowed by a " +
            "runtime binding");
    }

    [TestMethod]
    public void A_Fallback_That_Declines_Leaves_The_Event_Unhandled()
    {
        var d = Build();
        d.SetGestureFallback((_, _) => null);

        d.TryDispatch(Press(Key.Q, PrimaryModifier)).Should().BeNull();
    }

    [TestMethod]
    public void Clearing_The_Fallback_Stops_The_Offers()
    {
        var offered = 0;
        var d = Build();
        d.SetGestureFallback((_, _) => { offered++; return Task.CompletedTask; });
        d.TryDispatch(Press(Key.Q, PrimaryModifier));

        d.SetGestureFallback(null);
        d.TryDispatch(Press(Key.Q, PrimaryModifier));

        offered.Should().Be(1);
    }

    [TestMethod]
    public async Task RunByNameAsync_On_An_Unregistered_Name_Is_A_No_Op()
    {
        await Build(types: typeof(SaveCommand)).RunByNameAsync("nope");
        Ran.Should().BeEmpty();
    }
}
