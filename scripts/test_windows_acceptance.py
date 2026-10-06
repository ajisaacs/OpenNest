"""Synthetic parser controls, not Windows application acceptance evidence."""

import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

import windows_acceptance as acceptance


class WindowsAcceptanceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name)
        self.manifest = {
            "schema_version": 1,
            "projects": ["Example.Tests"],
            "groups": [{
                "id": "example", "title": "Example",
                "required_tests": [{"project": "Example.Tests", "method": "Example.Tests.Form.Accept", "minimum_cases": 2}],
                "remaining_acceptance": ["Visual/operator check"],
            }],
        }
        self.rows = [("Example.Tests.Form.Accept", "Passed"), ("Example.Tests.Form.Accept", "Passed")]

    def write_trx(self, rows=None, total=None, summary="Completed"):
        rows = self.rows if rows is None else rows
        root = ET.Element("TestRun", xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
        definitions = ET.SubElement(root, "TestDefinitions")
        results = ET.SubElement(root, "Results")
        for index, (method, outcome) in enumerate(rows):
            test = ET.SubElement(definitions, "UnitTest", id=str(index))
            class_name, name = method.rsplit(".", 1)
            ET.SubElement(test, "TestMethod", className=class_name, name=name)
            result = ET.SubElement(results, "UnitTestResult", testId=str(index), executionId=str(index),
                                   testName=f"{method}(case: {index})", outcome=outcome)
            if outcome != "Passed":
                ET.SubElement(ET.SubElement(result, "Output"), "StdOut").text = "fixture reason"
        result_summary = ET.SubElement(root, "ResultSummary", outcome=summary)
        ET.SubElement(result_summary, "Counters", total=str(len(rows) if total is None else total),
                      executed=str(sum(r[1] != "NotExecuted" for r in rows)),
                      passed=str(sum(r[1] == "Passed" for r in rows)),
                      failed=str(sum(r[1] == "Failed" for r in rows)), notExecuted="0")
        path = self.path / "Example.Tests.trx"
        ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)
        return path

    def evaluate(self):
        return acceptance.evaluate(self.manifest, self.path)

    def test_complete_theory_passes_and_retains_manual_gate(self):
        self.write_trx()
        report = self.evaluate()
        self.assertEqual("passed", report["automated_status"])
        self.assertEqual(2, report["groups"][0]["result_count"])
        self.assertEqual(["Visual/operator check"], report["groups"][0]["remaining_acceptance"])
        self.assertEqual(2, report["suites"]["Example.Tests"]["total"])

    def test_missing_or_malformed_file_fails(self):
        self.assertEqual("failed", self.evaluate()["automated_status"])
        (self.path / "Example.Tests.trx").write_text("<broken", encoding="utf-8")
        self.assertEqual("failed", self.evaluate()["automated_status"])

    def test_missing_method_or_theory_row_fails(self):
        for rows in ([self.rows[0]], [("Example.Tests.Other.Accept", "Passed")] * 2):
            with self.subTest(rows=rows):
                self.write_trx(rows)
                self.assertEqual("failed", self.evaluate()["automated_status"])

    def test_skip_failed_aborted_and_unknown_required_results_fail(self):
        for outcome in ("NotExecuted", "Failed", "Aborted", "TimedOut", "Unknown"):
            with self.subTest(outcome=outcome):
                self.write_trx([self.rows[0], (self.rows[1][0], outcome)])
                report = self.evaluate()
                self.assertEqual("failed", report["automated_status"])
                self.assertEqual("fixture reason", report["groups"][0]["tests"][1]["detail"])

    def test_unmapped_failed_test_still_fails_gate(self):
        self.write_trx(self.rows + [("Example.Tests.Other.Failure", "Failed")])
        self.assertEqual("failed", self.evaluate()["automated_status"])

    def test_unmapped_skip_is_explicit_not_a_pass(self):
        self.write_trx(self.rows + [("Example.Tests.Other.Optional", "NotExecuted")])
        report = self.evaluate()
        self.assertEqual("passed", report["automated_status"])
        suite = report["suites"]["Example.Tests"]
        self.assertEqual({"NotExecuted": 1, "Passed": 2}, suite["outcomes"])
        self.assertEqual("fixture reason", suite["results"][2]["detail"])

    def test_empty_or_inconsistent_counters_fail(self):
        for rows, total in (([], 0), (self.rows, 3)):
            self.write_trx(rows, total)
            self.assertEqual("failed", self.evaluate()["automated_status"])
        path = self.write_trx()
        path.write_text(path.read_text().replace('passed="2"', 'passed="1"'))
        self.assertEqual("failed", self.evaluate()["automated_status"])

    def test_contradictory_or_incomplete_counters_fail(self):
        changes = [("executed", "999"), ("executed", "-1"), ("executed", "0"),
                   ("executed", "bad"), ("executed", None), ("notExecuted", "1"),
                   ("completed", "999")]
        changes += [(key, "1") for key in ("aborted", "error", "timeout", "inconclusive",
                    "passedButRunAborted", "notRunnable", "disconnected", "warning",
                    "inProgress", "pending", "unknownCounter")]
        for key, value in changes:
            with self.subTest(counter=key, value=value):
                path = self.write_trx()
                tree = ET.parse(path)
                counters = tree.getroot().find(".//{*}Counters")
                assert counters is not None
                if value is None:
                    counters.attrib.pop(key)
                else:
                    counters.set(key, value)
                tree.write(path, encoding="utf-8", xml_declaration=True)
                self.assertEqual("failed", self.evaluate()["automated_status"])

    def test_aborted_run_fails_even_with_passed_rows(self):
        self.write_trx(summary="Aborted")
        self.assertEqual("failed", self.evaluate()["automated_status"])

    def test_failed_summary_and_bad_counters_retain_readable_evidence(self):
        rows = self.rows + [("Example.Tests.Other.Failure", "Failed"),
                            ("Example.Tests.Other.Optional", "NotExecuted")]
        for mode in ("Failed", "Aborted", "bad-counter", "missing-summary"):
            with self.subTest(mode=mode):
                path = self.write_trx(rows, summary=mode if mode in ("Failed", "Aborted") else "Completed")
                tree = ET.parse(path)
                root = tree.getroot()
                summary = root.find("{*}ResultSummary")
                assert summary is not None
                if mode == "bad-counter":
                    counters = summary.find("{*}Counters")
                    assert counters is not None
                    counters.set("total", "999")
                ET.SubElement(ET.SubElement(summary, "Output"), "StdOut").text = "runner failure detail"
                if mode == "missing-summary":
                    root.remove(summary)
                tree.write(path, encoding="utf-8", xml_declaration=True)
                report = self.evaluate()
                self.assertEqual("failed", report["automated_status"])
                self.assertIn("Example.Tests", report["suites"])
                suite = report["suites"]["Example.Tests"]
                self.assertEqual(4, suite["total"])
                self.assertEqual({"Passed": 2, "Failed": 1, "NotExecuted": 1}, suite["outcomes"])
                self.assertTrue(suite["validation_errors"])
                self.assertEqual("fixture reason", suite["results"][2]["detail"])
                self.assertEqual("fixture reason", suite["results"][3]["detail"])
                if mode != "missing-summary":
                    self.assertIn("runner failure detail", suite["summary_output"])
                self.assertEqual("failed", report["groups"][0]["automated_status"])

    def test_duplicate_or_unmapped_execution_fails(self):
        for old, new in (('executionId="1"', 'executionId="0"'),
                         ('testId="1"', 'testId="not-defined"'), ('id="1"', 'id="0"')):
            path = self.write_trx()
            path.write_text(path.read_text().replace(old, new))
            self.assertEqual("failed", self.evaluate()["automated_status"])

    def test_invalid_manifests_are_rejected(self):
        original = copy.deepcopy(self.manifest)
        for change in (
            lambda m: m.update(projects=[]),
            lambda m: m.update(groups=[]),
            lambda m: m["groups"].append(copy.deepcopy(m["groups"][0])),
            lambda m: m["groups"][0].update(required_tests=[]),
            lambda m: m["groups"][0]["required_tests"][0].update(minimum_cases=0),
            lambda m: m["groups"][0]["required_tests"][0].update(project="Unknown.Tests"),
        ):
            self.manifest = copy.deepcopy(original)
            change(self.manifest)
            with self.assertRaises(ValueError):
                self.evaluate()

    def test_cli_refuses_linux_runtime_and_dirty_source(self):
        self.write_trx()
        manifest = self.path / "manifest.json"
        manifest.write_text(json.dumps(self.manifest), encoding="utf-8")
        args = ["--results", str(self.path), "--manifest", str(manifest)]
        for system, dirty in (("Linux", ""), ("Windows", " M source.cs")):
            with self.subTest(system=system, dirty=dirty), \
                    patch.object(acceptance.platform, "system", return_value=system), \
                    patch.object(acceptance.platform, "platform", return_value="Synthetic parser fixture"), \
                    patch.object(acceptance.subprocess, "check_output", side_effect=["a" * 40, "b" * 40, dirty]), \
                    patch.dict(acceptance.os.environ, {"GITHUB_STEP_SUMMARY": ""}), \
                    patch("builtins.print"):
                self.assertEqual(1, acceptance.main(args))
                report = json.loads((self.path / "windows-acceptance.json").read_text())
                self.assertEqual("failed", report["automated_status"])
                self.assertTrue(report["errors"])


if __name__ == "__main__":
    unittest.main()
