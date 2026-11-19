#!/usr/bin/env python3
"""Explicit, sequential source mutation checks for the typed keymap regression tests.

Run only in an idle, clean Avalloy worktree. Output belongs outside the repository.
Every test invocation builds; every restored mutant gets a separate full rebuild.
A failed build, timeout, empty selection, or unrelated failure is not a killed mutant.
"""

import argparse
from dataclasses import asdict, dataclass
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
PROJECT = "tests/Avalloy.Base.Tests/Avalloy.Base.Tests.csproj"
COMMANDS = "src/Avalloy.Base/Commands/"
KEYMAPS = COMMANDS + "Keymaps/"


@dataclass(frozen=True)
class Mutant:
    name: str
    path: str
    find: str
    replace: str
    test: str
    runner: str = "core"


MUTANTS = [
    Mutant("sequence-pending", KEYMAPS + "SequenceMatcher.cs",
           "return new MatchResult(MatchKind.Pending, null, 0);",
           "return new MatchResult(MatchKind.Unbound, null, 0);",
           "A_chord_holds_its_leader_then_completes"),
    Mutant("sequence-completion", KEYMAPS + "SequenceMatcher.cs",
           "return new MatchResult(MatchKind.Bound, binding, 0);",
           "return new MatchResult(MatchKind.Unbound, null, 0);",
           "A_chord_holds_its_leader_then_completes"),
    Mutant("cancel-and-retry", KEYMAPS + "SequenceMatcher.cs",
           "            abandoned = _held;\n            Reset();",
           "            return new MatchResult(MatchKind.Unbound, null, 0);",
           "A_broken_chord_gives_back_its_held_keys_and_matches_the_new_one_afresh"),
    Mutant("cancel-replay", KEYMAPS + "KeymapDispatcher.cs",
           "Replay(window, step.GiveBack, step.Target);",
           "// Deliberate mutant: discard the abandoned prefix without replay.",
           "Broken_prefix_replays_down_text_and_up_to_original_focus_before_new_key"),
    Mutant("parameter", COMMANDS + "KeyboardDispatcher.cs",
           "pc.ExecuteAsync(parameter, target)", "pc.ExecuteAsync(null, target)",
           "Parameter_and_origin_target_survive_async_chain_and_another_window"),
    Mutant("performable", KEYMAPS + "SequenceMatcher.cs",
           "Consume: !unconsumed && !binding.Options.HasFlag(KeyBindingOptions.ConsumeIfExecutable)",
           "Consume: !unconsumed",
           "Consumption_obeys_readiness_and_options"),
    Mutant("unconsumed", KEYMAPS + "SequenceMatcher.cs",
           "var unconsumed = binding.Options.HasFlag(KeyBindingOptions.PassThrough);",
           "var unconsumed = false;",
           "Pass_through_chord_replays_prefix_but_consumed_chord_handles_last_character"),
    Mutant("next-text-rollover", KEYMAPS + "KeymapDispatcher.cs",
           "EventHandler<KeyEventArgs> down = (_, e) => observeExecution(OnKeyDown(e, focused(), state));",
           """var swallowNextText = false;
        root.AddHandler(InputElement.TextInputEvent, (_, e) =>
        {
            if (swallowNextText) { e.Handled = true; swallowNextText = false; }
        }, RoutingStrategies.Tunnel);
        EventHandler<KeyEventArgs> down = (_, e) =>
        {
            observeExecution(OnKeyDown(e, focused(), state));
            if (e.Handled) swallowNextText = true;
        };""",
           "Handled_shortcut_never_swallows_next_text_even_without_keyup_or_keydown"),
    Mutant("cmd-missing-keyup", KEYMAPS + "KeymapDispatcher.cs",
           "window.SwallowRelease.Remove(KeyIdentity(e));",
           "if (window.SwallowRelease.Contains(KeyIdentity(e))) { e.Handled = true; return Task.CompletedTask; }",
           "Missing_cmd_keyup_does_not_swallow_next_press_or_its_release"),
    Mutant("deactivation", KEYMAPS + "KeymapDispatcher.cs",
           "        Chord.Cancel();", "        // Deliberate mutant: keep the pending chord.",
           "Deactivation_cancels_pending_state_without_replay_and_preserves_release_pairing"),
    Mutant("ime-processed", KEYMAPS + "KeymapDispatcher.cs",
           "if (IsModifierKey(e) || e.Key == Key.ImeProcessed)",
           "if (IsModifierKey(e))",
           "Ime_processed_is_never_matched_or_peeked_even_with_a_pending_chord"),
    Mutant("display-labels", KEYMAPS + "KeyNames.cs",
           "if (m.HasFlag(KeyModifiers.Meta)) sb.Append('⌘');",
           "if (m.HasFlag(KeyModifiers.Meta)) sb.Append(\"Ctrl+\");",
           "Labels_and_virtual_letters_follow_platform_and_layout"),
]


_OWNED_RELEASE = "if (window.TerminalDown.Contains(id))\n        {\n            var flush"
_STALE_DOWN = "        _down.Remove(id);\n    }\n\n    /// <summary>Flush"
for name, test in [
    ("owned-release-readiness", "Control_owned_keyup_survives_consumed_repeat_after_readiness_changes"),
    ("owned-release-modifiers", "Control_owned_keyup_survives_consumed_repeat_after_modifiers_change"),
    ("owned-release-pending", "Control_owned_release_flushes_pending_repeat_before_keyup_without_abandoning_prefix"),
    ("owned-release-abandon", "Control_owned_release_is_not_replayed_twice_when_pending_repeat_is_abandoned"),
]:
    MUTANTS.append(Mutant(name, KEYMAPS + "KeymapDispatcher.cs", _OWNED_RELEASE,
                         _OWNED_RELEASE.replace("Contains(id)", "Contains(new object())"), test))
MUTANTS.append(Mutant("owned-release-flush", KEYMAPS + "KeymapDispatcher.cs",
                     "Replay(window, flush, window.Chord.Target);", "// Deliberate mutant: omit the repeat flush.",
                     "Control_owned_release_flushes_pending_repeat_before_keyup_without_abandoning_prefix"))
for name, test in [
    ("missing-leader-complete", "Missing_Cmd_leader_keyup_allows_same_key_to_complete_after_modifier_release"),
    ("missing-leader-no-modifier-event", "Missing_Cmd_leader_keyup_detects_modifier_change_without_modifier_event"),
    ("missing-leader-mismatch", "Missing_Cmd_leader_keyup_allows_same_key_mismatch_and_balanced_replay"),
    ("missing-leader-modifier-cycle", "Missing_Cmd_leader_keyup_after_modifier_cycle_distinguishes_fresh_press_from_repeat"),
]:
    MUTANTS.append(Mutant(name, KEYMAPS + "ChordSession.cs", _STALE_DOWN,
                         _STALE_DOWN.replace("_down.Remove(id)", "_down.Add(id)"), test))
MUTANTS.append(Mutant("missing-leader-late-release", KEYMAPS + "ChordSession.cs",
                     "            _orphaned.Add(id);", "            // Deliberate mutant: leak the delayed release.",
                     "Cmd_leader_late_keyup_is_suppressed_once_after_modifier_release"))

MUTANTS.extend([
    Mutant("palette-hints", COMMANDS + "CommandPaletteViewModel.cs",
           "Hints = dispatcher.Keymap.BindingsFor(a.Name, a.Parameter)\n                        .Select(b => Keymaps.KeyNames.Display(b.Sequence, platform ?? Keymaps.KeymapPlatforms.Current)).ToArray(),",
           "Hints = [],", "Keymap_palette_preserves_separate_hints_overflow_and_category"),
    Mutant("palette-recency-key", COMMANDS + "CommandPaletteViewModel.cs",
           "Key = System.Text.Json.JsonSerializer.Serialize(new[] { a.Name, a.Parameter }),",
           "Key = null,", "Keymap_palette_recents_distinguish_parameters_and_keep_target"),
    Mutant("palette-category-detail", COMMANDS + "CommandPaletteViewModel.cs",
           'Detail = d.Category == "General" ? "" : d.Category,',
           'Detail = "",', "Keymap_palette_preserves_separate_hints_overflow_and_category"),
])

# The keyboard layout presses consult where the event cannot say (FOX-J9F9J7, FOX-03GF1E).
MUTANTS.extend([
    Mutant("layout-unshifted-fallback", KEYMAPS + "KeyNames.cs",
           "return own ?? UsUnshifted(code);", "return UsUnshifted(code);",
           "On_Windows_German_ctrl_punctuation_is_the_layouts_character_not_the_US_one_at_its_position"),
    Mutant("layout-composed-first", KEYMAPS + "KeyNames.cs",
           "        if (composed && own is not null)\n            return own;\n", "",
           "On_macOS_AZERTY_cmd_option_z_is_z_not_the_w_of_its_US_position"),
    Mutant("layout-shifted-produced", KEYMAPS + "KeyNames.cs",
           "produced = Produced(layout.Shifted(code));", "produced = null;",
           "On_Windows_AZERTY_ctrl_digits_are_the_layouts_characters_and_shift_makes_the_digit"),
    Mutant("layout-letter-gate", KEYMAPS + "KeyNames.cs",
           "\n            && !IsVirtualLetter(virtualLetter))", ")",
           "On_Windows_a_letter_keeps_the_virtual_keys_answer_and_the_layout_answers_where_it_names_none"),
    Mutant("layout-only-without-symbol", KEYMAPS + "KeyNames.cs",
           "var own = produced is null && code is not null ?", "var own = code is not null ?",
           "A_character_the_event_reports_is_used_and_the_layout_is_not_asked"),
    Mutant("layout-none-knows-nothing", KEYMAPS + "KeyboardLayout.cs",
           "public string? Unshifted(string code) => null;", "public string? Unshifted(string code) => \"x\";",
           "A_layout_that_does_not_know_the_key_falls_back_as_before"),
    Mutant("layout-dispatcher-peek", KEYMAPS + "KeymapDispatcher.cs",
           "SequenceMatcher.Peek(Keymap, ToPress(e, Layout))", "SequenceMatcher.Peek(Keymap, ToPress(e))",
           "The_dispatcher_asks_its_layout_which_is_the_current_one_unless_set"),
    Mutant("layout-dispatcher-keydown", KEYMAPS + "KeymapDispatcher.cs",
           "window.Chord.Press(Keymap, ToPress(e, Layout),", "window.Chord.Press(Keymap, ToPress(e),",
           "The_dispatcher_asks_its_layout_which_is_the_current_one_unless_set"),
    Mutant("layout-mac-shift-state", KEYMAPS + "MacKeyboardLayout.cs",
           "Translate(code, ShiftModifierState);", "Translate(code, 0);",
           "macOS_layouts_type_their_own_characters"),
    Mutant("layout-mac-main-thread-only", KEYMAPS + "MacKeyboardLayout.cs",
           "        if (_inputSourceId is null && !OnMainThread) return null;\n", "",
           "Off_the_main_thread_the_current_macOS_layout_does_not_answer"),
    Mutant("layout-stall-guard-skips-rest", "tests/Avalloy.Base.Tests/KeyboardLayoutTests.cs",
           "            _stalledOn = what;\n", "",
           "A_lookup_that_never_answers_is_inconclusive_and_skips_the_rest"),
    Mutant("layout-mac-key-code", KEYMAPS + "MacKeyboardLayout.cs",
           '["KeyW"] = 0x0D', '["KeyW"] = 0x0E',
           "macOS_layouts_type_their_own_characters"),
])
_VERIFIER = "tests/Avalloy.Base.Tests/verify-keymap-mutations.py"
MUTANTS.extend([
    Mutant("runner-process-session", _VERIFIER, "                                   stdout=output, stderr=subprocess.STDOUT, start_new_session=" + "True)",
           "                                   stdout=output, stderr=subprocess.STDOUT, start_new_session=False)",
           "test_timeout_kills_process_group_before_returning", "python"),
    Mutant("runner-timeout-tree", _VERIFIER, "        os.killpg(process.pid, signal." + "SIGKILL)", "        process.kill()",
           "test_timeout_kills_process_group_before_returning", "python"),
    Mutant("runner-interruption-tree", _VERIFIER,
           "        except BaseException:\n            terminate_invocation(process)\n            raise",
           "        except BaseException:\n            raise",
           "test_interruption_kills_process_group_before_propagating", "python"),
])


def mutant_command(mutant):
    if mutant.runner == "python":
        return [sys.executable, "-B", "tests/Avalloy.Base.Tests/test_keymap_mutation_runner.py",
                "MutationRunnerTests." + mutant.test]
    return test_command("FullyQualifiedName~" + mutant.test)


def terminate_invocation(process):
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    process.wait()


def run(command, log):
    if os.name != "posix":
        raise RuntimeError("The source mutation verifier requires POSIX process groups")
    with log.open("w") as output:
        process = subprocess.Popen(command, cwd=ROOT, stdin=subprocess.DEVNULL,
                                   stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return process.wait(timeout=60)
        except subprocess.TimeoutExpired:
            terminate_invocation(process)
            output.write("\nVERIFICATION: command exceeded its 60-second limit; process group killed.\n")
            return 124
        except BaseException:
            terminate_invocation(process)
            raise


def counts(log):
    text = log.read_text(errors="replace").replace("\0", "")
    total = re.findall(r"^\s*total:\s*(\d+)", text, re.MULTILINE)
    failed = re.findall(r"^\s*failed:\s*(\d+)", text, re.MULTILINE)
    if not total:
        total = re.findall(r"Ran (\d+) tests?", text)
        failed = re.findall(r"FAILED \(failures=(\d+)", text)
    return text, int(total[-1]) if total else 0, int(failed[-1]) if failed else 0


def test_command(filter_expression=None):
    command = ["dotnet", "test", "--project", PROJECT]
    command += ["--", "--no-ansi", "--output", "Detailed", "--minimum-expected-tests", "1"]
    if filter_expression:
        command += ["--filter", filter_expression]
    return command


def interrupted(signum, _frame):
    raise InterruptedError(f"Interrupted by signal {signum}; restoring active mutant")


def caught(row, text):
    if row.get("runner") == "python":
        return (row["exit_code"] == 1 and row["total"] == 1 and row["failed"] == 1
                and "FAIL: " + row["test"] in text and "AssertionError" in text)
    failures = re.findall(r"^failed (.+)$", text, re.MULTILINE)
    return (row["exit_code"] == 2 and row["failed"] > 0 and row["total"] > 0
            and len(failures) == row["failed"]
            and all(re.match(re.escape(row["test"]) + r"(?:\s|\()", name) for name in failures)
            and "Expected " in text and "AwesomeAssertions.Execution" in text
            and re.search(r"Tests\.cs:\d+", text) is not None)


def write_junit(report, destination, success):
    if destination is None:
        return
    rows = report["mutants"]
    suite = ET.Element("testsuite", name="KeymapMutationVerification", tests=str(len(rows) + 1))
    for row in rows:
        case = ET.SubElement(suite, "testcase", name=row["name"], classname="KeymapMutationVerification")
        if not (row.get("killed") and row["restored"] and row["rebuilt"]):
            ET.SubElement(case, "failure", message="Mutation not killed and restored/rebuilt").text = json.dumps(row)
    case = ET.SubElement(suite, "testcase", name="restored-full-suite", classname="KeymapMutationVerification")
    if not success:
        ET.SubElement(case, "failure", message="Verification incomplete").text = json.dumps(report.get("final"))
    destination.parent.mkdir(parents=True, exist_ok=True)
    ET.ElementTree(suite).write(destination, encoding="utf-8", xml_declaration=True)


def audit(output, junit):
    # Reclassify preserved evidence without rerunning source mutations or tests.
    report = json.loads((output / "results.json").read_text())
    for row in report["mutants"]:
        text, total, failed = counts(output / (row["name"] + ".log"))
        if (total, failed) != (row["total"], row["failed"]):
            raise RuntimeError("Saved counts do not match original log")
        row["killed"] = caught(row, text)
        rebuild = (output / (row["name"] + "-restored-build.log")).read_text()
        row["rebuilt"] = row["rebuilt"] and "0 Error(s)" in rebuild and "Build succeeded." in rebuild
        print(f"{row['name']}: {'KILLED' if row['killed'] else 'NOT PROVEN'}; "
              f"restored={row['restored']}, rebuilt={row['rebuilt']}", flush=True)
    baseline, total, failed = counts(output / "baseline.log")
    final, final_total, final_failed = counts(output / "final-suite.log")
    diff = subprocess.run(["git", "diff", "--quiet", "HEAD", "--", *{m.path for m in MUTANTS},
                           COMMANDS + "CommandPaletteViewModel.cs"], cwd=ROOT)
    success = (len(report["mutants"]) == len(report.get("selected_mutants", MUTANTS)) and diff.returncode == 0
               and all(r["killed"] and r["restored"] and r["rebuilt"] for r in report["mutants"])
               and report["sources_restored"] and report["palette_unchanged"]
               and report["baseline"]["exit_code"] == 0 and total > 0 and failed == 0
               and report["final"]["build_exit_code"] == 0 and report["final"]["exit_code"] == 0
               and final_total > 0 and final_failed == 0)
    (output / "audited-results.json").write_text(json.dumps(report, indent=2) + "\n")
    write_junit(report, junit, success)
    print(f"Audited restored full suite: {final_total} total, {final_failed} failed", flush=True)
    return 0 if success else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--junit", type=Path)
    parser.add_argument("--only", nargs="+", help="Run selected mutation names")
    parser.add_argument("--audit", action="store_true", help="Reclassify saved logs without rerunning tests")
    args = parser.parse_args()
    output = args.output.resolve()
    if output.is_relative_to(ROOT):
        parser.error("--output must be outside the repository")
    if args.audit:
        return audit(output, args.junit)
    selected = [m for m in MUTANTS if args.only is None or m.name in args.only]
    if not selected or (args.only and set(args.only) != {m.name for m in selected}):
        parser.error("Unknown mutation name")
    output.mkdir(parents=True, exist_ok=False)
    for sig in (signal.SIGINT, signal.SIGTERM):
        signal.signal(sig, interrupted)

    originals = {m.path: (ROOT / m.path).read_bytes() for m in selected}
    palette = ROOT / COMMANDS / "CommandPaletteViewModel.cs"
    palette_original = palette.read_bytes()
    for mutant in selected:
        if originals[mutant.path].decode().count(mutant.find) != 1 or mutant.find == mutant.replace:
            raise RuntimeError(f"Invalid mutation anchor: {mutant.name}")

    results = []
    report = {"mutants": results, "selected_mutants": [m.name for m in selected]}
    problem = None
    try:
        expression = "|".join(f"FullyQualifiedName~{name}" for name in sorted({m.test for m in selected if m.runner == "core"}))
        baseline_log = output / "baseline.log"
        baseline_code = run(test_command(expression), baseline_log)
        _, total, failed = counts(baseline_log)
        report["baseline"] = {"exit_code": baseline_code, "total": total, "failed": failed}
        if baseline_code != 0 or total < len({m.test for m in selected if m.runner == "core"}) or failed:
            raise RuntimeError("Focused baseline did not pass; see baseline.log")
        print(f"Focused baseline: {total} passed", flush=True)

        python_log = output / "python-baseline.log"
        if run([sys.executable, "-B", "tests/Avalloy.Base.Tests/test_keymap_mutation_runner.py"], python_log) != 0:
            raise RuntimeError("Process cleanup baseline failed; see python-baseline.log")

        for mutant in selected:
            path = ROOT / mutant.path
            original = originals[mutant.path]
            modified = original.decode().replace(mutant.find, mutant.replace).encode()
            result = {**asdict(mutant), "restored": False, "rebuilt": False}
            results.append(result)
            try:
                path.write_bytes(modified)
                log = output / (mutant.name + ".log")
                result["exit_code"] = run(mutant_command(mutant), log)
                text, total, failed = counts(log)
                result.update(total=total, failed=failed)
                # A compilation failure or missing test cannot satisfy these conditions.
                result["killed"] = caught(result, text)
            finally:
                if path.read_bytes() not in (original, modified):
                    raise RuntimeError(f"Concurrent edit detected: {path}; refusing to overwrite it")
                path.write_bytes(original)
                result["restored"] = path.read_bytes() == original
                rebuild = output / (mutant.name + "-restored-build.log")
                result["rebuilt"] = run(["dotnet", "build", PROJECT, "--no-incremental"], rebuild) == 0
                (output / "results.json").write_text(json.dumps(report, indent=2) + "\n")
            print(f"{mutant.name}: {'KILLED' if result.get('killed') else 'NOT PROVEN'}; "
                  f"restored={result['restored']}, rebuilt={result['rebuilt']}", flush=True)
            if not result["rebuilt"]:
                raise RuntimeError(f"Restored rebuild failed after {mutant.name}")
    except BaseException as error:
        problem = error
        report["error"] = str(error)
    finally:
        report["sources_restored"] = all((ROOT / p).read_bytes() == b for p, b in originals.items())
        report["palette_unchanged"] = palette.read_bytes() == palette_original
        final_build = run(["dotnet", "build", PROJECT, "--no-incremental"], output / "final-build.log")
        final_code = run(test_command(), output / "final-suite.log")
        _, total, failed = counts(output / "final-suite.log")
        report["final"] = {"build_exit_code": final_build, "exit_code": final_code, "total": total, "failed": failed}
        (output / "results.json").write_text(json.dumps(report, indent=2) + "\n")
        success = (problem is None and len(results) == len(selected)
                   and all(r.get("killed") and r["restored"] and r["rebuilt"] for r in results)
                   and report["sources_restored"] and report["palette_unchanged"]
                   and final_build == 0 and final_code == 0 and total > 0 and failed == 0)
        write_junit(report, args.junit, success)
        print(f"Final suite: {total} total, {failed} failed; sources restored={report['sources_restored']}; "
              f"palette unchanged={report['palette_unchanged']}", flush=True)
    if problem:
        raise problem
    return 0 if success else 1


if __name__ == "__main__":
    sys.exit(main())
