#!/usr/bin/env python3
"""Fail-closed tests for the exact command used by the final CI check."""

from pathlib import Path
import subprocess
import unittest


CHECK = Path(__file__).resolve().with_name("check-ci-results.sh")


class CIResultsTests(unittest.TestCase):
    def check(self, *results):
        return subprocess.run(
            ["bash", str(CHECK), *results],
            capture_output=True,
            text=True,
            timeout=5,
        )

    def test_both_required_jobs_successful(self):
        self.assertEqual(self.check("success", "success").returncode, 0)

    def test_unit_matrix_failure_blocks_aggregate(self):
        self.assertNotEqual(self.check("failure", "success").returncode, 0)

    def test_synthetic_failure_blocks_aggregate(self):
        self.assertNotEqual(self.check("success", "failure").returncode, 0)

    def test_cancelled_dependency_blocks_aggregate(self):
        for results in [("cancelled", "success"), ("success", "cancelled")]:
            with self.subTest(results=results):
                self.assertNotEqual(self.check(*results).returncode, 0)

    def test_skipped_dependency_blocks_aggregate(self):
        for results in [("skipped", "success"), ("success", "skipped")]:
            with self.subTest(results=results):
                self.assertNotEqual(self.check(*results).returncode, 0)

    def test_empty_unknown_and_nonliteral_results_block_aggregate(self):
        for result in ["", "unknown", "SUCCESS", "success failure", "success\nsuccess"]:
            with self.subTest(result=result):
                self.assertNotEqual(self.check(result, "success").returncode, 0)
                self.assertNotEqual(self.check("success", result).returncode, 0)

    def test_missing_required_result_blocks_aggregate(self):
        self.assertNotEqual(self.check().returncode, 0)
        self.assertNotEqual(self.check("success").returncode, 0)

    def test_extra_result_blocks_aggregate(self):
        self.assertNotEqual(self.check("success", "success", "success").returncode, 0)


if __name__ == "__main__":
    unittest.main()
