using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalloy.Commands.Keymaps;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Avalloy.Base.Tests.KeymapEngineTests;

[assembly: AvaloniaTestApplication(typeof(Avalloy.Base.Tests.KeymapHeadless))]
namespace Avalloy.Base.Tests;

public static class KeymapHeadless
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    public static HeadlessUnitTestSession Session => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(KeymapHeadless).Assembly);
}

[TestClass]
public class KeymapRoutedTests
{
    private sealed class Routed : IDisposable
    {
        public Harness App { get; }
        public Border Control { get; } = new();
        public Border Root { get; }
        public List<string> Seen { get; } = [];
        public List<Task> Tasks { get; } = [];
        private readonly IDisposable _subscription;
        public Routed(params string[] bindings)
        {
            App = new(bindings);
            Root = new Border { Child = Control };
            _subscription = App.Keys.Listen(Root, () => Control, App.State, Tasks.Add);
            Control.AddHandler(InputElement.KeyDownEvent, (_, e) => Seen.Add("down:" + e.Key));
            Control.AddHandler(InputElement.KeyUpEvent, (_, e) => Seen.Add("up:" + e.Key));
            Control.AddHandler(InputElement.TextInputEvent, (_, e) => Seen.Add("text:" + e.Text));
        }
        public void Press(KeyEventArgs e) => Control.RaiseEvent(e);
        public void Type(string text) => Control.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });
        public void Dispose() => _subscription.Dispose();
    }

    [TestMethod]
    public Task Handled_shortcut_never_swallows_next_text_even_without_keyup_or_keydown() => KeymapHeadless.Session.Dispatch(() =>
    {
        using var routed = new Routed("super+k=record");
        var k = Down(Key.K, PhysicalKey.K, KeyModifiers.Meta, "k");
        routed.Press(k); k.Handled.Should().BeTrue();
        routed.Type("e");
        routed.Press(Up(Key.LWin, PhysicalKey.MetaLeft)); // K's KeyUp never arrives on macOS.
        routed.Type("p");
        routed.App.State.Deactivate(); routed.Type("q");
        routed.Seen.Where(s => s.StartsWith("text:")).Should().Equal("text:e", "text:p", "text:q");
    }, CancellationToken.None);

    [TestMethod]
    public Task Broken_prefix_replays_down_text_and_up_to_original_focus_before_new_key() => KeymapHeadless.Session.Dispatch(() =>
    {
        using var routed = new Routed("a>h=record");
        routed.Press(Down(Key.A, PhysicalKey.A, symbol: "a"));
        routed.Press(Up(Key.A, PhysicalKey.A));
        routed.Seen.Should().BeEmpty();
        routed.Press(Down(Key.X, PhysicalKey.X, symbol: "x"));
        routed.Seen.Should().Equal("down:A", "text:a", "up:A", "down:X");
        routed.App.Command.Ran.Should().BeEmpty();
        routed.Tasks.Should().AllSatisfy(t => t.IsCompletedSuccessfully.Should().BeTrue());
    }, CancellationToken.None);

    [TestMethod]
    public Task Pass_through_chord_replays_prefix_but_consumed_chord_handles_last_character() => KeymapHeadless.Session.Dispatch(() =>
    {
        using var pass = new Routed("unconsumed:a>h=record:3");
        pass.Press(Down(Key.A, PhysicalKey.A, symbol: "a"));
        pass.Press(Up(Key.A, PhysicalKey.A));
        var h = Down(Key.H, PhysicalKey.H, symbol: "h"); pass.Press(h);
        h.Handled.Should().BeFalse(); pass.Seen.Should().Equal("down:A", "text:a", "up:A", "down:H");
        pass.App.Command.Ran.Should().ContainSingle().Which.Parameter.Should().Be("3");
        using var consumed = new Routed("ctrl+a>h=record");
        consumed.Press(Down(Key.A, PhysicalKey.A, KeyModifiers.Control, "a"));
        var last = Down(Key.H, PhysicalKey.H, symbol: "h"); consumed.Press(last);
        last.Handled.Should().BeTrue(); consumed.Seen.Should().BeEmpty();
        var upA = Up(Key.A, PhysicalKey.A); consumed.Press(upA); upA.Handled.Should().BeTrue();
    }, CancellationToken.None);

    [TestMethod]
    public Task Replay_respects_handled_down_and_detaching_removes_handlers() => KeymapHeadless.Session.Dispatch(() =>
    {
        using var routed = new Routed("a>h=record");
        routed.Control.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (e.Key == Key.A) e.Handled = true; });
        routed.Press(Down(Key.A, PhysicalKey.A, symbol: "a"));
        routed.Press(Down(Key.X, PhysicalKey.X, symbol: "x"));
        routed.Seen.Should().NotContain("text:a");
        routed.Dispose();
        var a = Down(Key.A, PhysicalKey.A, symbol: "a");
        routed.Press(a); routed.App.State.Chord.IsPending.Should().BeFalse();
    }, CancellationToken.None);
    [TestMethod]
    public Task Control_owned_release_flushes_pending_repeat_before_keyup_without_abandoning_prefix() => KeymapHeadless.Session.Dispatch(() =>
    {
        using var routed = new Routed("ctrl+k>h=record");
        routed.Press(Down(Key.K, PhysicalKey.K, symbol: "k"));
        routed.Press(Down(Key.K, PhysicalKey.K, KeyModifiers.Control, "k"));
        routed.App.State.Chord.IsPending.Should().BeTrue();
        var release = Up(Key.K, PhysicalKey.K); routed.Press(release);
        release.Handled.Should().BeFalse();
        routed.Seen.Should().Equal("down:K", "down:K", "up:K");
        routed.App.State.Chord.IsPending.Should().BeTrue();
        routed.Press(Down(Key.H, PhysicalKey.H, symbol: "h"));
        routed.App.Command.Ran.Should().ContainSingle();
        routed.Seen.Should().Equal("down:K", "down:K", "up:K");
    }, CancellationToken.None);

    [TestMethod]
    public Task Control_owned_release_is_not_replayed_twice_when_pending_repeat_is_abandoned() => KeymapHeadless.Session.Dispatch(() =>
    {
        using var routed = new Routed("ctrl+k>h=record");
        routed.Press(Down(Key.K, PhysicalKey.K, symbol: "k"));
        routed.Press(Down(Key.K, PhysicalKey.K, KeyModifiers.Control, "k"));
        var release = Up(Key.K, PhysicalKey.K); routed.Press(release);
        release.Handled.Should().BeFalse();
        routed.Seen.Should().Equal("down:K", "down:K", "up:K");
        routed.Press(Down(Key.X, PhysicalKey.X, symbol: "x"));
        routed.Seen.Should().Equal("down:K", "down:K", "up:K", "down:X");
        routed.App.State.TerminalDown.Should().NotContain(PhysicalKey.K);
        routed.App.State.Chord.IsPending.Should().BeFalse();
    }, CancellationToken.None);

    [TestMethod]
    public Task Missing_Cmd_leader_keyup_allows_same_key_mismatch_and_balanced_replay() => KeymapHeadless.Session.Dispatch(() =>
    {
        using var routed = new Routed("super+k>h=record");
        routed.Press(Down(Key.K, PhysicalKey.K, KeyModifiers.Meta, "k"));
        routed.Press(Up(Key.LWin, PhysicalKey.MetaLeft));
        var next = Down(Key.K, PhysicalKey.K, symbol: "k"); routed.Press(next);
        next.Handled.Should().BeFalse();
        routed.App.State.Chord.IsPending.Should().BeFalse();
        routed.Seen.Should().Equal("up:LWin", "down:K", "up:K", "down:K");
        routed.App.Command.Ran.Should().BeEmpty();
        routed.Press(Up(Key.K, PhysicalKey.K));
        routed.App.State.TerminalDown.Should().BeEmpty();
    }, CancellationToken.None);

}
