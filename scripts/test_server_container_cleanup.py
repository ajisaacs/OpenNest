"""Unit fault injection: real Bash cleanup functions, explicitly virtual Docker.

No daemon, .NET build, HTTP endpoint, or real Docker resources are used here.
"""

import json
import os
from pathlib import Path
import re
import shlex
import subprocess
import sys
import tempfile
import unittest


TOKEN = "cleanup-unit-owner"
FIRST_ID = "a" * 64
CID = "b" * 64
FIRST_NAME = "cleanup-unit.first"
NAME = "cleanup-unit.replacement"
VOLUME = "cleanup-unit-data"
EXTRA_ID = "c" * 64
EXTRA_NAME = "cleanup-unit.restored"
EXTRA_VOLUME = "cleanup-unit-restore"


def mock_docker(state_path, args):
    """Model only the Docker transport calls needed by the cleanup contract."""
    path = Path(state_path)
    state = json.loads(path.read_text())
    state["calls"].append(args)
    path.write_text(json.dumps(state))
    kind = "volume" if args[0] == "volume" else "container"
    command = args[1] if kind == "volume" else args[0]
    resources = state["volumes"] if kind == "volume" else state["containers"]

    def fail(message, status):
        print(message, file=sys.stderr)
        return status

    if command == "inspect":
        status = state.get("lookup_status", 0) if kind == state.get("fault_kind") else 0
        if status:
            return fail(f"Injected Docker {kind} lookup failure status={status}", status)
        target = args[-1]
        resource = next(
            (key for key, value in resources.items() if target in (key, value.get("name"))),
            None,
        )
        if resource is None:
            return fail(f"Error: No such {kind}: {target}", 1)
        template = args[args.index("--format") + 1]
        owner = resources[resource]["owner"]
        if "Labels" in template:
            identity = ".Name" if kind == "volume" else ".Id"
            print(f"{resource} {owner}" if identity in template else owner)
        elif template == "{{.Id}}":
            print(resource)
        else:
            print(f"id={resource} running=true exit=0")
        return 0
    if command == "ls" or args[:2] == ["container", "ls"]:
        status = state.get("inventory_status", 0)
        if state.get("inventory_after_removal") and not state["removals"]:
            status = 0
        if status:
            return fail(f"Injected Docker inventory failure status={status}", status)
        filter_value = args[args.index("--filter") + 1]
        field, target = filter_value.split("=", 1)
        for key, value in resources.items():
            if field == "id":
                matches = key.startswith(target)
            elif kind == "container":
                matches = re.search(target, "/" + value["name"]) is not None
            else:
                matches = re.search(target, key) is not None
            if matches:
                print(key)
        return 0
    if command == "rm":
        target = args[-1]
        state["removals"].append([kind, target])
        mode = state.get("remove_mode") if kind == state.get("fault_kind") else None
        if mode not in ("error", "no_effect"):
            del resources[target]
        path.write_text(json.dumps(state))
        if mode == "error":
            return fail("Injected Docker removal failure", 1)
        print(target)
        return 0
    if command == "logs":
        print("Virtual container log; no real daemon contacted")
        return 0
    return fail(f"Unexpected mock Docker call: {args!r}", 97)


class CleanupTests(unittest.TestCase):
    def run_cleanup(self, *, missing_cidfile=False, absent_first=False,
                    absent_all=False, unowned=False, attempted=True,
                    original_status=0, extra=False, **faults):
        source = Path(__file__).with_name("Test-ServerContainer.sh").read_text()
        # Extract the actual candidate functions, never a copied cleanup implementation.
        functions = source[source.index("owned_container() {"):source.index("trap cleanup EXIT")]
        temp_root = Path(os.environ.get("TMPDIR", str(Path.home() / ".cache")))
        temp_root.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="opennest-cleanup-unit.", dir=temp_root) as temp:
            root = Path(temp)
            scratch = root / "scratch"
            logs = root / "results"
            scratch.mkdir()
            logs.mkdir()
            if not missing_cidfile:
                (scratch / "replacement.cid").write_text(CID + "\n")
            if absent_first:
                (scratch / "first.cid").write_text(FIRST_ID + "\n")
            if extra:
                (scratch / "restored.cid").write_text(EXTRA_ID + "\n")
            owner = "someone-else" if unowned else TOKEN
            state_path = root / "virtual-docker.json"
            containers = {} if absent_all else {CID: {"name": NAME, "owner": owner}}
            volumes = {} if absent_all else {VOLUME: {"owner": owner}}
            names = [FIRST_NAME, NAME]
            cidfiles = [scratch / "first.cid", scratch / "replacement.cid"]
            container_attempted = [absent_first and attempted, attempted]
            volume_names = [VOLUME]
            if extra:
                containers[EXTRA_ID] = {"name": EXTRA_NAME, "owner": owner}
                volumes[EXTRA_VOLUME] = {"owner": owner}
                names.append(EXTRA_NAME)
                cidfiles.append(scratch / "restored.cid")
                container_attempted.append(attempted)
                volume_names.append(EXTRA_VOLUME)
            state = {
                "containers": containers,
                "volumes": volumes,
                "calls": [], "removals": [], **faults,
            }
            state_path.write_text(json.dumps(state))
            q = shlex.quote

            def bash_array(values):
                return "(" + " ".join(q(str(value)) for value in values) + ")"

            def bash_flags(values):
                return "(" + " ".join(str(value).lower() for value in values) + ")"

            script = "\n".join([
                "set -Eeuo pipefail",
                f"scratch={q(str(scratch))}",
                f"run_results={q(str(logs))}",
                "owner_label='com.opennest.server-smoke.owner'",
                f"token={q(TOKEN)}",
                f"volumes={bash_array(volume_names)}",
                f"volume_attempted={bash_flags([attempted] * len(volume_names))}",
                f"container_attempted={bash_flags(container_attempted)}",
                f"container_names={bash_array(names)}",
                f"cidfiles={bash_array(cidfiles)}",
                f'docker_local() {{ {q(sys.executable)} {q(str(Path(__file__).resolve()))} '
                f'--mock-docker {q(str(state_path))} "$@"; }}',
                functions,
                "trap cleanup EXIT",
                f"exit {original_status}",
            ])
            run = subprocess.run(["bash", "-c", script], text=True, capture_output=True, timeout=15)
            self.assertFalse(scratch.exists(), "owned scratch must be removed on every outcome")
            self.assertTrue((logs / "outcome.log").is_file(), run.stderr)
            return {
                "status": run.returncode,
                "outcome": (logs / "outcome.log").read_text(),
                "log": (logs / "cleanup.log").read_text() if (logs / "cleanup.log").exists() else "",
                "state": json.loads(state_path.read_text()),
                "stderr": run.stderr,
            }

    def assert_failed(self, result, status=1):
        self.assertEqual(status, result["status"], result)
        self.assertEqual(f"exit={status} cleanup_failed=1\n", result["outcome"])

    def test_healthy_owned_resources_are_removed(self):
        for missing in (False, True):
            with self.subTest(missing_cidfile=missing):
                result = self.run_cleanup(missing_cidfile=missing)
                self.assertEqual(0, result["status"], result)
                self.assertEqual("exit=0 cleanup_failed=0\n", result["outcome"])
                self.assertEqual([["container", CID], ["volume", VOLUME]], result["state"]["removals"])
                self.assertFalse(result["state"]["containers"])
                self.assertFalse(result["state"]["volumes"])

    def test_first_container_already_removed_is_successful_noop(self):
        result = self.run_cleanup(absent_first=True)
        self.assertEqual(0, result["status"], result)
        self.assertEqual("exit=0 cleanup_failed=0\n", result["outcome"])
        self.assertNotIn(["container", FIRST_ID], result["state"]["removals"])

    def test_proven_absence_with_or_without_cidfile_is_success(self):
        for missing in (False, True):
            with self.subTest(missing_cidfile=missing):
                result = self.run_cleanup(absent_all=True, missing_cidfile=missing)
                self.assertEqual(0, result["status"], result)
                self.assertEqual("exit=0 cleanup_failed=0\n", result["outcome"])
                self.assertFalse(result["state"]["removals"])

    def test_lookup_timeout_or_daemon_error_fails_closed(self):
        for kind in ("container", "volume"):
            for status in (124, 1):
                for missing in (False, True):
                    with self.subTest(kind=kind, status=status, missing_cidfile=missing):
                        result = self.run_cleanup(fault_kind=kind, lookup_status=status,
                                                  missing_cidfile=missing)
                        self.assert_failed(result)
                        self.assertIn(f"Injected Docker {kind} lookup failure status={status}", result["log"])
                        self.assertIn("unknown", result["log"].lower())
                        self.assertFalse(any(r[0] == kind for r in result["state"]["removals"]))
                        self.assertTrue(result["state"]["containers" if kind == "container" else "volumes"])

    def test_failed_inventory_cannot_prove_absence(self):
        for status in (124, 1):
            for missing in (False, True):
                with self.subTest(status=status, missing_cidfile=missing):
                    result = self.run_cleanup(absent_all=True, missing_cidfile=missing,
                                              inventory_status=status)
                    self.assert_failed(result)
                    self.assertIn(f"Injected Docker inventory failure status={status}", result["log"])
                    self.assertIn("unknown", result["log"].lower())
                    self.assertFalse(result["state"]["removals"])

    def test_mismatched_owner_is_never_removed_or_claimed_complete(self):
        for missing in (False, True):
            with self.subTest(missing_cidfile=missing):
                result = self.run_cleanup(unowned=True, missing_cidfile=missing)
                self.assert_failed(result)
                self.assertFalse(result["state"]["removals"])
                self.assertIn("owner", result["log"].lower())

    def test_preexisting_unattempted_resources_are_untouched(self):
        result = self.run_cleanup(unowned=True, attempted=False)
        self.assertEqual(0, result["status"], result)
        self.assertEqual("exit=0 cleanup_failed=0\n", result["outcome"])
        self.assertFalse(result["state"]["calls"])
        self.assertTrue(result["state"]["containers"])
        self.assertTrue(result["state"]["volumes"])

    def test_removal_failure_or_false_success_fails_completion(self):
        for kind in ("container", "volume"):
            for mode in ("error", "no_effect"):
                with self.subTest(kind=kind, mode=mode):
                    result = self.run_cleanup(fault_kind=kind, remove_mode=mode)
                    self.assert_failed(result)
                    self.assertTrue(result["state"]["containers" if kind == "container" else "volumes"])
                    self.assertTrue(result["log"])

    def test_inconclusive_post_removal_readback_fails_completion(self):
        result = self.run_cleanup(inventory_status=124, inventory_after_removal=True)
        self.assert_failed(result)
        self.assertIn("Injected Docker inventory failure status=124", result["log"])

    def test_original_smoke_failure_is_preserved(self):
        result = self.run_cleanup(original_status=23, fault_kind="container", lookup_status=124)
        self.assert_failed(result, status=23)
        self.assertIn("Injected Docker container lookup failure status=124", result["log"])

    def test_every_attempted_container_and_volume_is_removed(self):
        result = self.run_cleanup(extra=True)
        self.assertEqual(0, result["status"], result)
        self.assertEqual("exit=0 cleanup_failed=0\n", result["outcome"])
        self.assertEqual([["container", CID], ["container", EXTRA_ID], ["volume", VOLUME],
                          ["volume", EXTRA_VOLUME]], result["state"]["removals"])
        self.assertFalse(result["state"]["containers"])
        self.assertFalse(result["state"]["volumes"])

    def test_one_failed_volume_does_not_skip_or_hide_the_others(self):
        result = self.run_cleanup(extra=True, fault_kind="volume", remove_mode="error")
        self.assert_failed(result)
        self.assertEqual([["container", CID], ["container", EXTRA_ID], ["volume", VOLUME],
                          ["volume", EXTRA_VOLUME]], result["state"]["removals"])
        self.assertFalse(result["state"]["containers"])


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--mock-docker":
        sys.exit(mock_docker(sys.argv[2], sys.argv[3:]))
    unittest.main()
