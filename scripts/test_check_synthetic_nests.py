"""Unit data below is intentionally synthetic CSV, not recorded benchmark evidence."""

import csv
import importlib.util
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("synthetic_gate", Path(__file__).with_name("check-synthetic-nests.py"))
assert spec is not None and spec.loader is not None
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


class SyntheticGateTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="opennest-gate-test-")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.csv = self.root / "results.csv"
        self.rows = [dict(Job=name, Engine="Irregular", Valid="True", Crashed="False",
                          FullyPlaced="True", PartsPlaced=str(demand), PartsRequested=str(demand),
                          PlatesUsed=str(sheets), Notes="")
                     for name, (demand, sheets) in gate.EXPECTED.items()]

    def write_csv(self, rows):
        with self.csv.open("w", newline="", encoding="utf-8") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(self.rows[0]))
            writer.writeheader()
            writer.writerows(rows)

    def test_exact_six_and_baseline_filter(self):
        self.write_csv(self.rows + [dict(self.rows[0], Engine="Baseline", Valid="False")])
        self.assertEqual(6, len(gate.check_csv(self.csv)))

    def test_fail_closed_result_cases(self):
        cases = {
            "invalid": dict(Valid="False"),
            "crashed": dict(Crashed="True", Notes="exception"),
            "timeout": dict(Valid="False", Notes="Timed out after 5 minutes"),
            "incomplete": dict(FullyPlaced="False", PartsPlaced="0"),
            "empty": dict(PartsPlaced="0", PartsRequested="0", PlatesUsed="0"),
            "unknown": dict(Job="unknown"),
            "engine": dict(Engine="Unknown"),
            "demand": dict(PartsRequested="99"),
            "sheets": dict(PlatesUsed="0"),
            "notes": dict(Notes="validation warning"),
        }
        for label, change in cases.items():
            with self.subTest(label=label):
                self.write_csv([dict(self.rows[0], **change)] + self.rows[1:])
                with self.assertRaises(gate.GateError):
                    gate.check_csv(self.csv)
        for rows in ([], self.rows[:-1], self.rows + [self.rows[0]]):
            self.write_csv(rows)
            with self.assertRaises(gate.GateError):
                gate.check_csv(self.csv)

    def test_malformed_csv_returns_nonzero(self):
        self.write_csv([dict(self.rows[0], PartsPlaced="not-a-number")] + self.rows[1:])
        self.assertEqual(1, gate.main(["--check-csv", str(self.csv)]))
        self.csv.write_text("Job,Engine\n", encoding="utf-8")
        self.assertEqual(1, gate.main(["--check-csv", str(self.csv)]))

    def test_missing_fixture_actual_entrypoint(self):
        self.assertEqual(1, gate.main(["--fixtures", str(self.root)]))

    def test_committed_fixtures_are_complete_and_synthetic(self):
        self.assertEqual(6, len(gate.check_fixtures(gate.ROOT / "test-data/synthetic-nests")))

    def test_concurrency_and_watchdog_are_bounded(self):
        for args in (["--parallel", "0"], ["--parallel", "5"], ["--timeout", "301"]):
            with self.assertRaises(SystemExit) as error:
                gate.main(args)
            self.assertEqual(2, error.exception.code)

    def test_nonzero_child_fails(self):
        script = self.root / "exit.py"
        script.write_text("raise SystemExit(7)\n", encoding="utf-8")
        with self.assertRaisesRegex(gate.GateError, "exited 7"):
            gate.run_process([sys.executable, str(script)], time.monotonic() + 10,
                             self.root / "exit.log")

    @unittest.skipIf(os.name == "nt", "POSIX SIGTERM probe; Windows requires runtime acceptance")
    def test_cancellation_kills_ready_owned_tree(self):
        parent = self.root / "cancel-parent.py"
        pidfile = self.root / "cancel-pids.txt"
        ready = self.root / "cancel-ready"
        parent.write_text(
            "import os, subprocess, sys, time\n"
            "from pathlib import Path\n"
            "child = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'])\n"
            f"Path({str(pidfile)!r}).write_text(str(os.getpid()) + ' ' + str(child.pid))\n"
            f"Path({str(ready)!r}).touch()\n"
            "time.sleep(60)\n", encoding="utf-8")
        wrapper = self.root / "cancel-wrapper.py"
        wrapper.write_text(
            "import importlib.util, signal, sys, time\n"
            "from pathlib import Path\n"
            "sys.dont_write_bytecode = True\n"
            f"spec = importlib.util.spec_from_file_location('gate', {str(Path(__file__).with_name('check-synthetic-nests.py'))!r})\n"
            "gate = importlib.util.module_from_spec(spec)\nspec.loader.exec_module(gate)\n"
            "signal.signal(signal.SIGTERM, gate.cancel)\n"
            "try:\n"
            f"    gate.run_process([sys.executable, {str(parent)!r}], time.monotonic() + 15, "
            f"Path({str(self.root / 'cancel.log')!r}))\n"
            "except KeyboardInterrupt:\n    sys.exit(130)\n", encoding="utf-8")
        process = subprocess.Popen([sys.executable, str(wrapper)], start_new_session=True)
        try:
            end = time.monotonic() + 10
            while not ready.exists() and time.monotonic() < end:
                time.sleep(0.01)
            self.assertTrue(ready.exists(), "cancellation tree never became ready")
            pids = [int(value) for value in pidfile.read_text().split()]
            process.send_signal(signal.SIGTERM)
            self.assertEqual(130, process.wait(timeout=10))
            self.assert_quiescent(pids)
        finally:
            gate.terminate_tree(process)

    def assert_quiescent(self, pids):
        for pid in pids:
            end = time.monotonic() + 5
            while True:
                status = Path(f"/proc/{pid}/stat")
                if status.exists() and status.read_text().split()[2] == "Z":
                    break  # Linux init may not yet have reaped a killed orphan.
                try:
                    os.kill(pid, 0)
                except ProcessLookupError:
                    break
                self.assertLess(time.monotonic(), end, f"owned process {pid} remains alive")
                time.sleep(0.01)

    @unittest.skipIf(os.name == "nt", "POSIX session-tree probe; Windows uses taskkill /T /F")
    def test_watchdog_kills_ready_parent_and_grandchild(self):
        # A real owned process tree, synchronized before the watchdog. No benchmark results mocked.
        child = self.root / "child.py"
        child.write_text("import time\ntime.sleep(60)\n", encoding="utf-8")
        parent = self.root / "parent.py"
        pidfile = self.root / "pids.txt"
        ready = self.root / "ready"
        parent.write_text(
            "import os, subprocess, sys, time\n"
            "from pathlib import Path\n"
            f"child = subprocess.Popen([sys.executable, {str(child)!r}])\n"
            f"Path({str(pidfile)!r}).write_text(str(os.getpid()) + ' ' + str(child.pid))\n"
            f"Path({str(ready)!r}).touch()\n"
            "time.sleep(60)\n", encoding="utf-8")
        from concurrent.futures import ThreadPoolExecutor
        with ThreadPoolExecutor(max_workers=1) as executor:
            future = executor.submit(gate.run_process, [sys.executable, str(parent)],
                                     time.monotonic() + 5, self.root / "timeout.log")
            end = time.monotonic() + 4
            while not ready.exists() and time.monotonic() < end:
                time.sleep(0.01)
            self.assertTrue(ready.exists(), "child tree never became ready")
            pids = [int(value) for value in pidfile.read_text().split()]
            with self.assertRaisesRegex(gate.GateError, "watchdog expired"):
                future.result(timeout=10)
        self.assert_quiescent(pids)


if __name__ == "__main__":
    unittest.main()
