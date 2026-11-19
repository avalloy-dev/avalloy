"""Isolated mutation proof; run it through the test runner, not concurrently with edits.

Python standard library only. Each test invocation builds and has a 60-second limit.
The detached scratch worktree is always removed; full logs and JSON evidence remain
in the output directory. Product source in the invoking worktree is never mutated.
"""
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
PRODUCT = "src/Avalloy.Base/Preferences/"
WATCHER = PRODUCT + "FileSetWatcher.cs"
PLAN = PRODUCT + "WatchPlan.cs"
SHARED = PRODUCT + "SharedDirectory.cs"
MUTANTS = [
    ("group-write", SHARED, "UnixFileMode.GroupWrite | UnixFileMode.OtherWrite", "UnixFileMode.OtherWrite", "Group_writable_directory_is_shared"),
    ("covering-gap", WATCHER, "        var plan = Plan();", "        foreach (var entry in _subscriptions.Values) entry.Dispose();\n        _subscriptions.Clear();\n        var plan = Plan();", "Second_pass_keeps_covering_subscription_and_revalidates_new_directories"),
    ("skip-revalidation", WATCHER, "            var next = Plan();", "            var next = plan;", "Second_pass_keeps_covering_subscription_and_revalidates_new_directories"),
    ("unbounded-retries", WATCHER, "_retryAttempts >= MaxRetries || ", "", "Persistent_failures_back_off_stop_and_preserve_healthy_subscriptions"),
    ("retry-reload-storm", WATCHER, "                // A retry is maintenance, not a filesystem notification.", "                Changed?.Invoke(this, EventArgs.Empty);", "Persistent_failures_back_off_stop_and_preserve_healthy_subscriptions"),
    ("missing-host-parent", PLAN, "            holdingNames.Add(directory);", "            if (child == path) holdingNames.Add(directory);", "Missing_segment_host_has_parent_coverage_subject_to_shared_rule_and_cap"),
    ("ancestor-walk", PLAN, "while (directory is not null && !directoryExists(directory))", "if (directory is not null && !directoryExists(directory))", "Missing_ancestor_walk_reaches_nearest_existing_directory"),
    ("replacement", WATCHER, "                        pair.Value.Invalidated = true;", "                        pair.Value.Invalidated = false;", "Missing_segments_advance_and_directory_replacement_recovers"),
    ("cap-priority", PLAN, "foreach (var directory in order)", "foreach (var directory in order.AsEnumerable().Reverse())", "Cap_skips_an_oversized_chain_then_keeps_a_later_candidate_that_fits"),
    ("shared-parent", PLAN, " && !shared(p)", "", "Shared_parent_is_excluded_but_direct_files_and_missing_segments_are_watched"),
    ("debounce", WATCHER, "    private void Schedule()\n    {\n        _pending?.Dispose();", "    private void Schedule()\n    {", "Debounce_is_global_and_stale_cancelled_callbacks_cannot_fire"),
    ("disposal", WATCHER, "            foreach (var subscription in _subscriptions.Values) subscription.Dispose();", "            // mutant: subscriptions remain alive", "Disposal_rejects_stale_subscriptions_and_retargeting_and_is_idempotent"),
]


def git(*args, cwd=ROOT):
    return subprocess.run(["git", *args], cwd=cwd, check=True, capture_output=True).stdout


def test(tree, output, test_name=None):
    command = ["dotnet", "test", "--project", "tests/Avalloy.Base.Tests/Avalloy.Base.Tests.csproj"]
    if test_name:
        command += ["--", "--filter", "FullyQualifiedName~" + test_name]
    with output.open("w") as log:
        process = subprocess.Popen(command, cwd=tree, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            code = process.wait(timeout=60)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            process.wait()
            raise RuntimeError("60-second test limit exceeded: " + str(output))
    return code, output.read_text()


def main():
    output = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(tempfile.mkdtemp(prefix="avalloy-watch-proof-"))
    output.mkdir(parents=True, exist_ok=True)
    tree = output / "worktree"
    if tree.exists():
        raise RuntimeError("Refusing to reuse scratch worktree: " + str(tree))
    rows = []
    git("worktree", "add", "--detach", str(tree), "HEAD")
    try:
        # Include reviewed but not yet committed changes without changing either branch.
        patch = git("diff", "HEAD", "--binary")
        if patch:
            subprocess.run(["git", "apply", "-"], input=patch, cwd=tree, check=True)
        # Include untracked test artifacts too, without copying ignored build outputs.
        for name in git("ls-files", "--others", "--exclude-standard", "-z").decode().split("\0"):
            if name:
                target = tree / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes((ROOT / name).read_bytes())
        code, _ = test(tree, output / "baseline.log")
        if code:
            raise RuntimeError("Scratch baseline failed; see baseline.log")
        selected = set(sys.argv[2:])
        if selected - {m[0] for m in MUTANTS}:
            raise RuntimeError("Unknown mutant selection")
        for name, relative, find, replace, caught_by in MUTANTS:
            if selected and name not in selected:
                continue
            source = tree / relative
            original = source.read_bytes()
            text = original.decode()
            if text.count(find) != 1:
                raise RuntimeError(name + ": mutant must match exactly once")
            print("Checking " + name + " with " + caught_by, flush=True)
            row = dict(mutant=name, test=caught_by, caught=False, restored=False, rebuilt=False)
            try:
                source.write_text(text.replace(find, replace))
                code, log = test(tree, output / (name + ".log"), caught_by)
                # A compilation error or an empty test selection is not a killed mutant.
                row["caught"] = code != 0 and "Test run summary: Failed!" in log and "failed: 0" not in log and caught_by in log
            finally:
                source.write_bytes(original)
                row["restored"] = source.read_bytes() == original
                code, _ = test(tree, output / (name + "-restored.log"))
                row["rebuilt"] = code == 0
                rows.append(row)
                (output / "results.json").write_text(json.dumps(rows, indent=2) + "\n")
            print(json.dumps(row), flush=True)
            if not row["caught"] or not row["rebuilt"]:
                raise RuntimeError("Mutation proof failed: " + name)
    finally:
        # This detached worktree is ours; all edits in it are disposable proof artifacts.
        git("worktree", "remove", "--force", str(tree))
        if tree.exists():
            raise RuntimeError("Scratch worktree was not removed")
    print("All mutants caught; every restore rebuilt green; scratch worktree removed.", flush=True)


if __name__ == "__main__":
    main()
