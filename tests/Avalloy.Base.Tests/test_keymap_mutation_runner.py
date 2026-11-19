"""Deterministic process cleanup regressions; no processes or clocks are needed."""
import importlib.util
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

sys.dont_write_bytecode = True

spec = importlib.util.spec_from_file_location(
    "keymap_mutations", Path(__file__).with_name("verify-keymap-mutations.py"))
runner = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = runner
spec.loader.exec_module(runner)


class MutationRunnerTests(unittest.TestCase):
    def check_cleanup(self, interruption):
        process = Mock(pid=7341)
        process.wait.side_effect = [interruption, -signal.SIGKILL]
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / "invocation.log"
            with patch.object(runner.os, "name", "posix"), \
                    patch.object(runner.subprocess, "Popen", return_value=process) as popen, \
                    patch.object(runner.os, "killpg") as killpg:
                if isinstance(interruption, subprocess.TimeoutExpired):
                    self.assertEqual(runner.run(["dotnet", "test"], log), 124)
                else:
                    with self.assertRaises(InterruptedError):
                        runner.run(["dotnet", "test"], log)
                self.assertTrue(popen.call_args.kwargs["start_new_session"])
                killpg.assert_called_once_with(process.pid, signal.SIGKILL)
                self.assertEqual(process.wait.call_count, 2)
                self.assertEqual(process.wait.call_args.args, ())
                self.assertEqual(process.wait.call_args.kwargs, {})

    def test_timeout_kills_process_group_before_returning(self):
        self.check_cleanup(subprocess.TimeoutExpired(["dotnet", "test"], 60))

    def test_interruption_kills_process_group_before_propagating(self):
        self.check_cleanup(InterruptedError("explicit interruption"))


if __name__ == "__main__":
    unittest.main()
