using System.Runtime.InteropServices;
using AwesomeAssertions;
using Avalonia.Input;
using Avalloy.Commands.Keymaps;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Avalloy.Base.Tests.KeymapEngineTests;

namespace Avalloy.Base.Tests;

/// <summary>
/// Presses that ask the keyboard layout what a key types where the platform's event cannot say:
/// Windows reports no <c>KeySymbol</c> with Ctrl held, and a composing macOS Option leaves <c>Key</c> at
/// the US position. The layouts here are fakes holding what the real ones type, so every case runs on
/// every OS; <see cref="RealKeyboardLayoutTests"/> reads the real ones.
/// </summary>
[TestClass]
public class KeyboardLayoutTests
{
    internal sealed class FakeLayout(params (string Code, string Unshifted, string Shifted)[] keys) : IKeyboardLayout
    {
        private readonly Dictionary<string, (string Unshifted, string Shifted)> _keys =
            keys.ToDictionary(k => k.Code, k => (k.Unshifted, k.Shifted));
        public int Lookups { get; private set; }
        public string? Unshifted(string code) { Lookups++; return _keys.TryGetValue(code, out var k) ? k.Unshifted : null; }
        public string? Shifted(string code) { Lookups++; return _keys.TryGetValue(code, out var k) ? k.Shifted : null; }
    }

    // What the real layouts type, as Windows' kbdgr.dll and kbdfr.dll and macOS's German and French layouts do.
    internal static readonly FakeLayout German = new(
        ("Minus", "ß", "?"), ("Slash", "-", "_"), ("BracketRight", "+", "*"), ("Digit0", "0", "="),
        ("Equal", "´", "`"), ("KeyY", "z", "Z"), ("KeyZ", "y", "Y"));
    internal static readonly FakeLayout Azerty = new(
        ("Digit0", "à", "0"), ("Digit1", "&", "1"), ("Digit2", "é", "2"), ("Minus", ")", "°"),
        ("KeyQ", "a", "A"), ("KeyA", "q", "Q"), ("KeyW", "z", "Z"), ("KeyZ", "w", "W"),
        ("Semicolon", "m", "M"), ("KeyM", ",", "?"));
    internal static readonly FakeLayout Us = new(("Digit0", "0", ")"), ("Minus", "-", "_"), ("Comma", ",", "<"));

    private static SequenceMatcher Matcher(params string[] lines) => new(KeymapFixtures.Map(lines));

    // A Windows press with Ctrl held: Avalonia's KeySymbol is null, its Key the layout's virtual key.
    private static KeyPress Windows(IKeyboardLayout layout, KeyModifiers mods, string code, string? virtualLetter = null) =>
        KeyNames.ToPress(mods, code, keySymbol: null, layout, optionComposes: false, virtualLetter);

    // A macOS press: KeySymbol is what Option composed, Key the US position Avalonia falls back to.
    private static KeyPress Mac(IKeyboardLayout layout, KeyModifiers mods, string code, string composed, string usLetter) =>
        KeyNames.ToPress(mods, code, composed, layout, optionComposes: true, usLetter);

    [TestMethod]
    public void On_Windows_German_ctrl_punctuation_is_the_layouts_character_not_the_US_one_at_its_position()
    {
        var m = Matcher("ctrl++=increase_font_size:1", "ctrl+-=decrease_font_size:1", "ctrl+==reset_font_size");
        m.Press(Windows(German, KeyModifiers.Control, "BracketRight")).Name().Should().Be("increase_font_size");
        m.Press(Windows(German, KeyModifiers.Control, "Slash")).Name().Should().Be("decrease_font_size");
        m.Press(Windows(German, KeyModifiers.Control, "Minus")).Kind.Should().Be(MatchKind.Unbound, "Ctrl+ß is not Ctrl+-");
        // German = is Shift+0: the Shift that made it is consumed, as on macOS where the symbol says so.
        m.Press(Windows(German, KeyModifiers.Control | KeyModifiers.Shift, "Digit0")).Name().Should().Be("reset_font_size");
    }

    [TestMethod]
    public void On_Windows_AZERTY_ctrl_digits_are_the_layouts_characters_and_shift_makes_the_digit()
    {
        var m = Matcher("ctrl+0=reset_font_size", "ctrl+1=goto_tab:1", "ctrl+shift+0=other");
        m.Press(Windows(Azerty, KeyModifiers.Control, "Digit0")).Kind.Should().Be(MatchKind.Unbound, "Ctrl+à is not Ctrl+0");
        m.Press(Windows(Azerty, KeyModifiers.Control | KeyModifiers.Shift, "Digit0")).Name().Should().Be("reset_font_size");
        m.Press(Windows(Azerty, KeyModifiers.Control | KeyModifiers.Shift, "Digit1")).Invocation().Should()
            .Be(new CommandInvocation("goto_tab", "1"));
        Matcher("ctrl+&=amp").Press(Windows(Azerty, KeyModifiers.Control, "Digit1")).Name().Should().Be("amp");
    }

    [TestMethod]
    public void On_Windows_US_a_shift_that_made_no_digit_stays_part_of_the_trigger()
    {
        var m = Matcher("ctrl+shift+0=other", "ctrl+shift+,=reload_config");
        m.Press(Windows(Us, KeyModifiers.Control | KeyModifiers.Shift, "Digit0")).Name().Should().Be("other");
        m.Press(Windows(Us, KeyModifiers.Control | KeyModifiers.Shift, "Comma")).Name().Should().Be("reload_config");
        Matcher("ctrl+-=decrease_font_size:1").Press(Windows(Us, KeyModifiers.Control, "Minus"))
            .Name().Should().Be("decrease_font_size");
    }

    [TestMethod]
    public void On_Windows_a_letter_keeps_the_virtual_keys_answer_and_the_layout_answers_where_it_names_none()
    {
        // AZERTY's M is the US ; key, which Windows calls VK 'M'; AZERTY's , is the US M key, VK_OEM_COMMA.
        var m = Matcher("ctrl+m=letter_m", "ctrl+,=open_config", "ctrl+shift+a=select_all");
        m.Press(Windows(Azerty, KeyModifiers.Control, "Semicolon", virtualLetter: "M")).Name().Should().Be("letter_m");
        m.Press(Windows(Azerty, KeyModifiers.Control, "KeyM")).Name().Should().Be("open_config");
        var a = Windows(Azerty, KeyModifiers.Control | KeyModifiers.Shift, "KeyQ", virtualLetter: "A");
        a.Produced.Should().BeNull("Shift changes no letter, so the layout is not asked");
        m.Press(a).Name().Should().Be("select_all");
    }

    [TestMethod]
    public void On_macOS_AZERTY_cmd_option_z_is_z_not_the_w_of_its_US_position()
    {
        // The W key types z on AZERTY; Option composes Â there, and Avalonia's Key falls back to W.
        var m = Matcher("super+alt+w=close_tab:this", "super+alt+z=undo");
        m.Press(Mac(Azerty, KeyModifiers.Meta | KeyModifiers.Alt, "KeyW", "Â", "W")).Name().Should().Be("undo");
        m.Press(Mac(Azerty, KeyModifiers.Meta | KeyModifiers.Alt, "KeyZ", "‹", "Z")).Name().Should().Be("close_tab");
        m.Press(Mac(Azerty, KeyModifiers.Meta | KeyModifiers.Alt | KeyModifiers.Shift, "KeyW", "Å", "W")).Kind
            .Should().Be(MatchKind.Unbound, "super+alt+shift+z is not bound, and a composed character is no trigger");
    }

    [TestMethod]
    public void On_macOS_QWERTZ_option_y_and_z_follow_the_layout()
    {
        var m = Matcher("alt+y=why", "alt+z=zed");
        m.Press(Mac(German, KeyModifiers.Alt, "KeyY", "Ω", "Y")).Name().Should().Be("zed");
        m.Press(Mac(German, KeyModifiers.Alt, "KeyZ", "¥", "Z")).Name().Should().Be("why");
    }

    [TestMethod]
    public void A_layout_that_does_not_know_the_key_falls_back_as_before()
    {
        // KeyboardLayouts.None, and a layout without the key, read as the US position: today's answer.
        var none = KeyboardLayouts.None;
        KeyNames.ToPress(KeyModifiers.Control, "Minus", null, none).Unicode.Should().Be("-");
        KeyNames.ToPress(KeyModifiers.Control | KeyModifiers.Shift, "Digit0", null, none)
            .Should().Be(new KeyPress(KeyModifiers.Control | KeyModifiers.Shift, "Digit0", "0", null));
        KeyNames.ToPress(KeyModifiers.Meta | KeyModifiers.Alt, "KeyW", "Â", none, optionComposes: true, "W").Unicode.Should().Be("w");
        Windows(new FakeLayout(), KeyModifiers.Control, "Minus").Should().Be(KeyNames.ToPress(KeyModifiers.Control, "Minus", null));
        KeyNames.ToPress(KeyModifiers.Control, "Minus", null).Unicode.Should().Be("-", "the overload without a layout keeps the US guess");
    }

    [TestMethod]
    public void A_character_the_event_reports_is_used_and_the_layout_is_not_asked()
    {
        var layout = new FakeLayout(("Minus", "x", "y"), ("Equal", "x", "y"), ("KeyQ", "x", "y"));
        KeyNames.ToPress(KeyModifiers.Control, "Minus", "ß", layout).Should().Be(new KeyPress(KeyModifiers.Control, "Minus", "ß", "ß"));
        KeyNames.ToPress(KeyModifiers.Meta | KeyModifiers.Shift, "Equal", "+", layout, optionComposes: true).Unicode.Should().Be("=");
        KeyNames.ToPress(KeyModifiers.Alt, "KeyQ", "a", layout, optionComposes: true).Unicode.Should().Be("a");
        layout.Lookups.Should().Be(0);
    }

    [TestMethod]
    public void The_dispatcher_asks_its_layout_which_is_the_current_one_unless_set()
    {
        var h = new Harness("ctrl+-=record");
        h.Keys.Layout.Should().BeSameAs(KeyboardLayouts.Current);
        // German Ctrl+- on Windows: the US / key, VK_OEM_MINUS, no symbol.
        var e = Down(Key.OemMinus, PhysicalKey.Slash, KeyModifiers.Control);
        h.Keys.Layout = German;
        h.Keys.Peek(e).Name().Should().Be("record");
        h.Press(e);
        e.Handled.Should().BeTrue();
        h.Command.Ran.Should().ContainSingle();
        h.Keys.Layout = KeyboardLayouts.None;
        var us = Down(Key.OemMinus, PhysicalKey.Slash, KeyModifiers.Control);
        h.Keys.Peek(us).Should().BeNull("by the US position the key is /");
        h.Press(us);
        us.Handled.Should().BeFalse();
        KeymapDispatcher.ToPress(e, German, KeymapPlatform.Windows).Unicode.Should().Be("-");
    }

    [TestMethod]
    public void Current_is_the_platforms_own_layout()
    {
        var expected = OperatingSystem.IsWindows() ? "WindowsKeyboardLayout"
            : OperatingSystem.IsMacOS() ? "MacKeyboardLayout" : "NoLayout";
        KeyboardLayouts.Current.GetType().Name.Should().Be(expected);
    }
}

// A missing binding fails as an assertion, not a NullReferenceException.
internal static class MatchNames
{
    internal static string? Name(this MatchResult r) => r.Binding?.Action?.Name;
    internal static CommandInvocation? Invocation(this MatchResult r) => r.Binding?.Action;
    internal static string? Name(this KeymapBinding? b) => b?.Action?.Name;
}

/// <summary>
/// The real OS layouts, read by ID without selecting them, so the user's input source is never changed.
/// Each runs only on its own OS.
/// </summary>
[TestClass]
public class RealKeyboardLayoutTests
{
    // What each layout types unshifted and with Shift alone, for keys whose answer differs from US.
    private static readonly (string Code, string Unshifted, string Shifted)[] German =
        [("Minus", "ß", "?"), ("Slash", "-", "_"), ("BracketRight", "+", "*"), ("Digit0", "0", "="), ("KeyY", "z", "Z"), ("KeyZ", "y", "Y")];
    private static readonly (string Code, string Unshifted, string Shifted)[] French =
        [("Digit0", "à", "0"), ("Digit1", "&", "1"), ("Digit2", "é", "2"), ("KeyQ", "a", "A"), ("KeyW", "z", "Z"),
         ("Semicolon", "m", "M"), ("KeyM", ",", "?")];

    [TestMethod]
    [DataRow("com.apple.keylayout.German", "German")]
    [DataRow("com.apple.keylayout.French", "French")]
    public void macOS_layouts_type_their_own_characters(string id, string name)
    {
        if (!OperatingSystem.IsMacOS()) { Assert.Inconclusive("macOS only"); return; }
        if (!MacKeyboardLayout.HasGuiSession)
        {
            Assert.Inconclusive("no window-server session (CGSessionCopyCurrentDictionary is null), so no keyboard layout to read");
            return;
        }
        var layout = new MacKeyboardLayout(id);
        var keys = name == "German" ? German : French;
        var answers = WithinLimit($"{name} layout", () =>
            keys.Select(k => (k.Code, Unshifted: layout.Unshifted(k.Code), Shifted: layout.Shifted(k.Code))).ToList());
        foreach (var ((code, unshifted, shifted), answer) in keys.Zip(answers))
        {
            answer.Unshifted.Should().Be(unshifted, $"{name} {code}");
            answer.Shifted.Should().Be(shifted, $"{name} {code} with Shift");
        }
    }

    [TestMethod]
    public void Off_the_main_thread_the_current_macOS_layout_does_not_answer()
    {
        if (!OperatingSystem.IsMacOS()) { Assert.Inconclusive("macOS only"); return; }
        if (MacKeyboardLayout.OnMainThread) { Assert.Inconclusive("the test is on the main thread"); return; }
        // Text Input Sources aborts the process on two concurrent calls, and Avalonia calls it on the main
        // thread for every key: off it, the current layout says nothing rather than call it.
        var layout = new MacKeyboardLayout();
        for (var c = 'A'; c <= 'Z'; c++)
            layout.Unshifted("Key" + c).Should().BeNull($"Key{c} off the main thread");
        layout.Shifted("Digit0").Should().BeNull();
    }

    private static readonly LookupStallGuard s_guard = new(TimeSpan.FromSeconds(30));

    private static T WithinLimit<T>(string what, Func<T> lookup) => s_guard.Within(what, lookup);

    [TestMethod]
    public void A_lookup_that_never_answers_is_inconclusive_and_skips_the_rest()
    {
        var guard = new LookupStallGuard(TimeSpan.FromMilliseconds(50));
        using var never = new ManualResetEventSlim();
        try
        {
            var stalled = () => guard.Within("stuck", () => { never.Wait(); return 0; });
            stalled.Should().Throw<AssertInconclusiveException>().WithMessage("*stuck*did not answer*");
            var next = () => guard.Within("next", () => 1);
            next.Should().Throw<AssertInconclusiveException>().WithMessage("*stopped answering*(stuck)*");
        }
        finally
        {
            never.Set();
        }
        new LookupStallGuard(TimeSpan.FromSeconds(30)).Within("answers", () => 42).Should().Be(42);
    }

    [TestMethod]
    [DataRow("00000407", "German")]
    [DataRow("0000040C", "French")]
    public void Windows_layouts_type_their_own_characters(string klid, string name)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows only"); return; }
        var hkl = LoadKeyboardLayoutW(klid, KLF_NOTELLSHELL);
        hkl.Should().NotBe(0, $"{name} ({klid}) loads");
        try
        {
            var layout = new WindowsKeyboardLayout(hkl);
            var keys = name == "German" ? German : French;
            var answers = WithinLimit($"{name} layout", () =>
                keys.Select(k => (Unshifted: layout.Unshifted(k.Code), Shifted: layout.Shifted(k.Code))).ToList());
            foreach (var ((code, unshifted, shifted), answer) in keys.Zip(answers))
            {
                answer.Unshifted.Should().NotBeNull($"{name} {code}");
                answer.Unshifted!.ToLowerInvariant().Should().Be(unshifted.ToLowerInvariant(), $"{name} {code}");
                answer.Shifted.Should().Be(shifted, $"{name} {code} with Shift");
            }
        }
        finally
        {
            // Only a layout this test loaded is unloaded: one the user has stays theirs.
            if (!IsInstalled(hkl)) UnloadKeyboardLayout(hkl);
        }
    }

    private const uint KLF_NOTELLSHELL = 0x80;
    private static readonly HashSet<nint> s_installedBefore = OperatingSystem.IsWindows() ? InstalledLayouts() : [];
    private static bool IsInstalled(nint hkl) => s_installedBefore.Contains(hkl);

    private static HashSet<nint> InstalledLayouts()
    {
        var n = GetKeyboardLayoutList(0, null);
        var list = new nint[n];
        GetKeyboardLayoutList(n, list);
        return [.. list];
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadKeyboardLayoutW(string klid, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnloadKeyboardLayout(nint hkl);

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int count, nint[]? list);
}


/// <summary>
/// Ends a real OS lookup that stops answering, instead of letting it hold the run until CI's job limit.
/// </summary>
/// <remarks>
/// The call is synchronous, so nothing can cancel it, only stop waiting for it: it runs on a background
/// thread, which is left blocked. As <c>SecretStoreStallGuard</c> does for the keychain, a stall is the
/// machine's state rather than a regression, so it is Inconclusive, and every later lookup through the same
/// guard is skipped rather than waiting out the limit behind the stuck call. The limit is a ceiling no
/// answering call comes near, not a timing assertion.
/// </remarks>
internal sealed class LookupStallGuard(TimeSpan limit)
{
    private volatile string? _stalledOn;

    public T Within<T>(string what, Func<T> lookup)
    {
        if (_stalledOn is { } stalled)
            Assert.Inconclusive($"the OS stopped answering a keyboard layout lookup ({stalled}) — skipped");
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = lookup(); }
            catch (Exception e) { error = e; }
        }) { IsBackground = true, Name = "real keyboard layout lookup" };
        thread.Start();
        if (!thread.Join(limit))
        {
            _stalledOn = what;
            Assert.Inconclusive($"{what}: the OS did not answer within {limit.TotalSeconds:0.##}s; the lookup thread is left blocked — skipped");
        }
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
        return result;
    }
}
