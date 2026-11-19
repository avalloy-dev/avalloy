using AwesomeAssertions;
using Avalonia.Input;
using Avalloy.Commands.Keymaps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Base.Tests;

[TestClass]
public class SequenceMatcherTests
{
    private static KeymapBinding Kb(string text)
    {
        return KeymapFixtures.Binding(text);
    }

    private static SequenceMatcher Matcher(params string[] lines) =>
        new(KeymapFixtures.Map(lines));

    private static KeyPress Press(KeyModifiers mods, string physical, string? symbol = null) =>
        KeyNames.ToPress(mods, physical, symbol);

    [TestMethod]
    public void A_bound_key_matches_by_its_physical_code()
    {
        var m = Matcher("super+arrow_up=jump_to_prompt:-1");
        var r = m.Press(Press(KeyModifiers.Meta, "ArrowUp"));
        r.Kind.Should().Be(MatchKind.Bound);
        r.Binding!.Action.Should().Be(new CommandInvocation("jump_to_prompt", "-1"));
    }

    [TestMethod]
    public void An_unbound_key_or_other_modifiers_are_unbound()
    {
        var m = Matcher("super+arrow_up=jump_to_prompt:-1");
        m.Press(Press(KeyModifiers.Control, "ArrowUp")).Kind.Should().Be(MatchKind.Unbound);
        m.Press(Press(KeyModifiers.Meta | KeyModifiers.Shift, "ArrowUp")).Kind.Should().Be(MatchKind.Unbound);
    }

    [TestMethod]
    public void A_unicode_trigger_matches_the_unshifted_character_even_when_the_platform_reports_the_shifted_one()
    {
        var m = Matcher("super+shift+[=previous_tab", "super+shift+p=toggle_command_palette");
        m.Press(Press(KeyModifiers.Meta | KeyModifiers.Shift, "BracketLeft", "{")).Binding!.Action!.Name.Should().Be("previous_tab");
        m.Press(Press(KeyModifiers.Meta | KeyModifiers.Shift, "KeyP", "P")).Binding!.Action!.Name.Should().Be("toggle_command_palette");
    }

    [TestMethod]
    public void A_letter_follows_the_layout_not_the_key_position()
    {
        // On AZERTY the key in QWERTY's Q position types "a".
        var m = Matcher("super+a=select_all");
        m.Press(Press(KeyModifiers.Meta, "KeyQ", "a")).Kind.Should().Be(MatchKind.Bound);
        m.Press(Press(KeyModifiers.Meta, "KeyA", "q")).Kind.Should().Be(MatchKind.Unbound);
    }

    [TestMethod]
    public void Ctrl_plus_fires_from_shift_equals_because_shift_was_consumed_making_the_plus()
    {
        var m = Matcher("ctrl++=increase_font_size:2", "super++=increase_font_size:3");
        m.Press(Press(KeyModifiers.Control | KeyModifiers.Shift, "Equal", "+")).Binding!.Action!.Parameter.Should().Be("2");
        m.Press(Press(KeyModifiers.Meta | KeyModifiers.Shift, "Equal", "+")).Binding!.Action!.Parameter.Should().Be("3");
        m.Press(Press(KeyModifiers.Control, "Equal", "=")).Kind.Should().Be(MatchKind.Unbound, "Ctrl+= is not Ctrl++");
    }

    [TestMethod]
    public void A_shift_that_changes_nothing_is_still_part_of_the_trigger()
    {
        var m = Matcher("ctrl+c=copy_to_clipboard", "ctrl+shift+v=paste_from_clipboard");
        m.Press(Press(KeyModifiers.Control | KeyModifiers.Shift, "KeyC", "C")).Kind.Should().Be(MatchKind.Unbound,
            "Ctrl+Shift+C is not Ctrl+C: the shift made no other character");
        m.Press(Press(KeyModifiers.Control | KeyModifiers.Shift, "KeyV", "V")).Kind.Should().Be(MatchKind.Bound);
    }

    [TestMethod]
    public void Punctuation_follows_a_German_layout_not_the_US_key_position()
    {
        // German: + is unshifted on the US ] key, - is on the US / key, ß is on the US - key,
        // and = is Shift+0.
        var m = Matcher("ctrl++=increase_font_size:1", "ctrl+-=decrease_font_size:1", "ctrl+==reset_font_size");
        m.Press(Press(KeyModifiers.Control, "BracketRight", "+")).Binding!.Action!.Name.Should().Be("increase_font_size");
        m.Press(Press(KeyModifiers.Control, "Slash", "-")).Binding!.Action!.Name.Should().Be("decrease_font_size");
        m.Press(Press(KeyModifiers.Control, "Minus", "ß")).Kind.Should().Be(MatchKind.Unbound, "ß is not -, wherever US keeps -");
        m.Press(Press(KeyModifiers.Control | KeyModifiers.Shift, "Digit0", "=")).Binding!.Action!.Name.Should().Be("reset_font_size");
    }

    [TestMethod]
    public void On_AZERTY_a_digit_typed_with_shift_matches_the_digit_with_shift_consumed()
    {
        // AZERTY's number row is shifted: Cmd+Shift+à reports Digit0 and "0". The US guess at the
        // unshifted character is also 0, which used to keep Shift and miss super+0.
        var m = Matcher("super+0=reset_font_size", "super+shift+0=decrease_font_size:1");
        m.Press(KeyNames.ToPress(KeyModifiers.Meta | KeyModifiers.Shift, "Digit0", "0", optionComposes: true))
            .Binding!.Action!.Name.Should().Be("reset_font_size");
        // On US, Shift+0 types ")": Shift is not what made a 0, so super+shift+0 still matches by key.
        m.Press(KeyNames.ToPress(KeyModifiers.Meta | KeyModifiers.Shift, "Digit0", ")", optionComposes: true))
            .Binding!.Action!.Name.Should().Be("decrease_font_size");
        m.Press(KeyNames.ToPress(KeyModifiers.Meta, "Digit0", "0", optionComposes: true))
            .Binding!.Action!.Name.Should().Be("reset_font_size", "US Cmd+0");
    }

    [TestMethod]
    public void An_alt_trigger_names_the_key_not_the_character_Option_composes()
    {
        var m = Matcher("alt+a=select_all");
        m.Press(KeyNames.ToPress(KeyModifiers.Alt, "KeyA", "å", optionComposes: true)).Kind.Should().Be(MatchKind.Bound);
    }

    [TestMethod]
    public void On_AZERTY_alt_a_is_the_key_that_types_a_not_the_US_A_position()
    {
        // AZERTY puts a on QWERTY's Q key and q on its A key. Off macOS, Alt composes nothing, so the
        // layout's character stands; on macOS an Option press that still reports plain a does too.
        var m = Matcher("alt+a=select_all", "alt+q=clear_screen");
        m.Press(KeyNames.ToPress(KeyModifiers.Alt, "KeyQ", "a")).Binding!.Action!.Name.Should().Be("select_all");
        m.Press(KeyNames.ToPress(KeyModifiers.Alt, "KeyA", "q")).Binding!.Action!.Name.Should().Be("clear_screen");
        m.Press(KeyNames.ToPress(KeyModifiers.Alt, "KeyQ", "a", optionComposes: true)).Binding!.Action!.Name.Should().Be("select_all");
        m.Press(KeyNames.ToPress(KeyModifiers.Alt | KeyModifiers.Shift, "KeyQ", "A")).Binding.Should().BeNull("alt+shift+a is not bound");
    }

    [TestMethod]
    public void Unicode_wins_over_physical_when_both_are_bound()
    {
        var m = Matcher("super+digit_1=goto_tab:9", "super+1=goto_tab:1");
        m.Press(Press(KeyModifiers.Meta, "Digit1", "1")).Binding!.Action!.Parameter.Should().Be("1");
    }

    [TestMethod]
    public void A_chord_holds_its_leader_then_completes()
    {
        var m = Matcher("ctrl+a>n=new_window");
        m.Press(Press(KeyModifiers.Control, "KeyA")).Kind.Should().Be(MatchKind.Pending);
        m.IsPending.Should().BeTrue();
        var r = m.Press(Press(KeyModifiers.None, "KeyN", "n"));
        r.Kind.Should().Be(MatchKind.Bound);
        r.Abandoned.Should().Be(0);
        m.IsPending.Should().BeFalse();
    }

    [TestMethod]
    public void A_broken_chord_gives_back_its_held_keys_and_matches_the_new_one_afresh()
    {
        var m = Matcher("ctrl+a>n=new_window", "ctrl+b=select_all");

        m.Press(Press(KeyModifiers.Control, "KeyA"));
        var r = m.Press(Press(KeyModifiers.None, "KeyX", "x"));
        r.Should().Be(new MatchResult(MatchKind.Unbound, null, Abandoned: 1));

        m.Press(Press(KeyModifiers.Control, "KeyA"));
        var again = m.Press(Press(KeyModifiers.Control, "KeyB"));
        again.Kind.Should().Be(MatchKind.Bound);
        again.Abandoned.Should().Be(1);
        again.Binding!.Action!.Name.Should().Be("select_all");
    }

    [TestMethod]
    public void A_three_step_chord_holds_two_keys()
    {
        var m = Matcher("ctrl+a>b>c=quit");
        m.Press(Press(KeyModifiers.Control, "KeyA"));
        m.Press(Press(KeyModifiers.None, "KeyB", "b"));
        m.Held.Should().Be(2);
        m.Press(Press(KeyModifiers.None, "KeyZ", "z")).Abandoned.Should().Be(2);
    }
}

[TestClass]
public class ChordSessionTests
{
    private static Keymap Map(params string[] lines) => KeymapFixtures.Map(lines);

    private static KeyPress Ctrl(string letter) =>
        new(KeyModifiers.Control, "Key" + letter.ToUpperInvariant(), letter);

    private static KeyPress Plain(string letter) =>
        new(KeyModifiers.None, "Key" + letter.ToUpperInvariant(), letter);

    [TestMethod]
    public void A_leader_in_one_window_does_not_complete_a_chord_in_another()
    {
        var map = Map("ctrl+a>n=new_window");
        var windowA = new ChordSession<string>();
        var windowB = new ChordSession<string>();

        windowA.Press(map, Ctrl("a"), "A:ctrl+a", "terminal A").Result.Kind.Should().Be(MatchKind.Pending);

        var inB = windowB.Press(map, Plain("n"), "B:n", "terminal B");
        inB.Result.Kind.Should().Be(MatchKind.Unbound, "window B has no chord under way");
        inB.GiveBack.Should().BeEmpty("window A's held key is not B's to replay");
        windowA.IsPending.Should().BeTrue("window A's chord is untouched");

        var inA = windowA.Press(map, Plain("n"), "A:n", "terminal A");
        inA.Result.Kind.Should().Be(MatchKind.Bound);
        inA.ChordKeys.Should().Equal("A:ctrl+a");
        inA.Target.Should().Be("terminal A");
    }

    [TestMethod]
    public void A_broken_chord_gives_its_keys_back_to_the_window_and_element_that_held_them()
    {
        var map = Map("ctrl+a>n=new_window");
        var windowA = new ChordSession<string>();
        var windowB = new ChordSession<string>();

        windowA.Press(map, Ctrl("a"), "A:ctrl+a", "terminal A");
        windowB.Press(map, Ctrl("a"), "B:ctrl+a", "terminal B");

        var broken = windowB.Press(map, Plain("x"), "B:x", "terminal B");
        broken.Result.Kind.Should().Be(MatchKind.Unbound);
        broken.GiveBack.Should().Equal("B:ctrl+a");
        broken.Target.Should().Be("terminal B");
        windowA.IsPending.Should().BeTrue();
    }

    private sealed record Ev(string Kind, string Key);

    private static ChordSession<Ev> Session() => new(e => e.Key);

    [TestMethod]
    public void A_broken_chord_replays_the_leaders_release_after_its_press_in_order()
    {
        // Kitty key-event reporting reports releases: the leader's release, which came while the chord
        // was held, must be replayed after its press, never let through on its own before it.
        var map = Map("ctrl+a>n=new_window");
        var s = Session();
        s.Press(map, Ctrl("a"), new("down", "a"), "t");
        s.Release(new("up", "a")).Should().BeTrue("the leader is held, so its release is too");

        var broken = s.Press(map, Plain("x"), new("down", "x"), "t");
        broken.GiveBack.Should().Equal(new Ev("down", "a"), new Ev("up", "a"));
        s.Release(new("up", "x")).Should().BeFalse("x went straight to the program, and so does its release");
    }

    [TestMethod]
    public void A_release_that_is_not_of_a_held_key_is_not_the_sessions()
    {
        var map = Map("ctrl+a>n=new_window");
        var s = Session();
        s.Release(new("up", "a")).Should().BeFalse("nothing is held");
        s.Press(map, Ctrl("a"), new("down", "a"), "t");
        s.Release(new("up", "z")).Should().BeFalse("z was never held");
    }

    [TestMethod]
    public void A_completed_chord_reports_which_held_keys_are_still_down()
    {
        var map = Map("ctrl+a>n=new_window");

        var stillHeld = Session();
        stillHeld.Press(map, Ctrl("a"), new("down", "a"), "t");
        var done = stillHeld.Press(map, Plain("n"), new("down", "n"), "t");
        done.ChordKeys.Should().Equal(new Ev("down", "a"));
        done.StillDown.Should().Equal("a");

        var released = Session();
        released.Press(map, Ctrl("a"), new("down", "a"), "t");
        released.Release(new("up", "a"));
        var after = released.Press(map, Plain("n"), new("down", "n"), "t");
        after.ChordKeys.Should().Equal(new Ev("down", "a"), new Ev("up", "a"));
        after.StillDown.Should().BeEmpty();
    }

    [TestMethod]
    public void A_leader_held_across_a_keymap_swap_still_has_its_release_swallowed()
    {
        // Leader down, config reload swaps the keymap, an unrelated press, then the leader comes up.
        // The press was discarded unreplayed, so the program must not see the release either.
        var s = Session();
        s.Press(Map("ctrl+a>n=new_window"), Ctrl("a"), new("down", "a"), "t");

        var unrelated = s.Press(Map("ctrl+b=select_all"), Plain("x"), new("down", "x"), "t");
        unrelated.Result.Kind.Should().Be(MatchKind.Unbound);
        unrelated.GiveBack.Should().BeEmpty("a keymap swap discards the chord rather than replaying it");

        s.Release(new("up", "x")).Should().BeFalse("x reached the program");
        s.Release(new("up", "a")).Should().BeTrue("the leader's press never did");
        s.Release(new("up", "a")).Should().BeFalse("only once");
    }

    [TestMethod]
    public void A_discarded_keys_next_press_ends_the_wait_for_its_release()
    {
        var s = Session();
        s.Press(Map("ctrl+a>n=new_window"), Ctrl("a"), new("down", "a"), "t");
        var swapped = Map("ctrl+b=select_all");
        s.Press(swapped, Plain("x"), new("down", "x"), "t");
        s.Press(swapped, Ctrl("a"), new("down", "a"), "t");
        s.Release(new("up", "a")).Should().BeFalse("its new press went to the program, so this release is that press's");
    }

    [TestMethod]
    public void An_auto_repeating_leader_is_buffered_not_taken_as_a_new_chord_step()
    {
        // Hold Ctrl+A until it repeats, release A, press N: the repeat must not break and restart the
        // chord (which replayed the first press and then dropped the release with the restarted one).
        var map = Map("ctrl+a>n=clear_screen");
        var s = Session();
        s.Press(map, Ctrl("a"), new("down", "a"), "t");

        var repeat = s.Press(map, Ctrl("a"), new("repeat", "a"), "t");
        repeat.Result.Kind.Should().Be(MatchKind.Pending);
        repeat.GiveBack.Should().BeEmpty("nothing is replayed while the chord holds");

        s.Release(new("up", "a")).Should().BeTrue();
        var done = s.Press(map, Plain("n"), new("down", "n"), "t");
        done.Result.Binding!.Action!.Name.Should().Be("clear_screen");
        done.ChordKeys.Should().Equal(new Ev("down", "a"), new Ev("repeat", "a"), new Ev("up", "a"));
        done.StillDown.Should().BeEmpty();
    }

    [TestMethod]
    public void An_auto_repeating_leader_in_a_broken_chord_replays_every_press_before_its_release()
    {
        var map = Map("ctrl+a>n=clear_screen");
        var s = Session();
        s.Press(map, Ctrl("a"), new("down", "a"), "t");
        s.Press(map, Ctrl("a"), new("repeat", "a"), "t");
        s.Release(new("up", "a"));
        s.Press(map, Plain("x"), new("down", "x"), "t").GiveBack
            .Should().Equal(new Ev("down", "a"), new Ev("repeat", "a"), new Ev("up", "a"));
    }

    [TestMethod]
    public void Losing_focus_cancels_a_pending_chord_without_replaying_its_keys()
    {
        // Hold Ctrl+A, switch away, release A in the other app, come back and press X: the chord is gone,
        // so X does not replay an A press whose release went elsewhere.
        var map = Map("ctrl+a>n=new_window");
        var s = Session();
        s.Press(map, Ctrl("a"), new("down", "a"), "t");

        s.Cancel();
        s.IsPending.Should().BeFalse();

        var back = s.Press(map, Plain("x"), new("down", "x"), "t");
        back.Result.Kind.Should().Be(MatchKind.Unbound);
        back.GiveBack.Should().BeEmpty();
    }

    [TestMethod]
    public void A_key_still_down_when_focus_was_lost_keeps_its_release_suppressed_until_it_is_pressed_again()
    {
        var map = Map("ctrl+a>n=new_window");
        var s = Session();
        s.Press(map, Ctrl("a"), new("down", "a"), "t");
        s.Cancel();
        s.Release(new("up", "a")).Should().BeTrue("released back here: its press never reached the program");
        s.Release(new("up", "a")).Should().BeFalse("only once");

        var t = Session();
        t.Press(map, Ctrl("a"), new("down", "a"), "t");
        t.Cancel();
        t.Press(map, Plain("a"), new("down", "a"), "t").Result.Kind.Should().Be(MatchKind.Unbound);
        t.Release(new("up", "a")).Should().BeFalse("a new press of it reached the program, so this release is that press's");
    }

    [TestMethod]
    public void A_new_keymap_drops_a_chord_under_way_without_replaying_it()
    {
        var session = new ChordSession<string>();
        session.Press(Map("ctrl+a>n=new_window"), Ctrl("a"), "ctrl+a", "t");

        var after = session.Press(Map("ctrl+b=select_all"), Plain("n"), "n", "t");
        after.Result.Kind.Should().Be(MatchKind.Unbound);
        after.GiveBack.Should().BeEmpty();
    }
}
