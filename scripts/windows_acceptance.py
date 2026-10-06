#!/usr/bin/env python3
"""Fail-closed evidence for named Windows tests, not automatic feature sign-off."""

import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import xml.etree.ElementTree as ET


def read_suite(path):
    """Read actual result rows; TRX notExecuted counters can omit xUnit skips."""
    root = ET.parse(path).getroot()
    if root.tag != "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}TestRun":
        raise ValueError(f"{path.name}: not a TRX TestRun")
    definitions = {}
    for test in root.findall("./{*}TestDefinitions/{*}UnitTest"):
        method = test.find("{*}TestMethod")
        identity = test.attrib["id"]
        if identity in definitions or method is None:
            raise ValueError(f"{path.name}: duplicate or incomplete test definition")
        definitions[identity] = method.attrib["className"] + "." + method.attrib["name"]
    results = []
    executions = set()
    for result in root.findall("./{*}Results/{*}UnitTestResult"):
        execution = result.attrib["executionId"]
        if execution in executions:
            raise ValueError(f"{path.name}: duplicate test execution")
        executions.add(execution)
        output = result.find("{*}Output")
        results.append({
            "method": definitions[result.attrib["testId"]],
            "name": result.attrib["testName"],
            "outcome": result.attrib["outcome"],
            "detail": "\n".join(output.itertext()).strip() if output is not None else "",
        })
    summary = root.find("{*}ResultSummary")
    if summary is None or summary.attrib.get("outcome") not in ("Completed", "Passed"):
        raise ValueError(f"{path.name}: missing or unsuccessful run summary")
    counters = summary.find("{*}Counters")
    if counters is None or not results or int(counters.attrib["total"]) != len(results):
        raise ValueError(f"{path.name}: empty results or total does not match enumerated results")
    outcomes = Counter(r["outcome"] for r in results)
    for key, outcome in (("passed", "Passed"), ("failed", "Failed")):
        if int(counters.attrib[key]) != outcomes[outcome]:
            raise ValueError(f"{path.name}: {key} counter does not match result rows")
    return {
        "file": path.name,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "total": len(results),
        "outcomes": dict(sorted(outcomes.items())),
        "results": results,
    }


def evaluate(manifest, results_dir):
    if manifest.get("schema_version") != 1:
        raise ValueError("Unsupported acceptance manifest schema")
    projects = manifest["projects"]
    groups = manifest["groups"]
    if not projects or len(projects) != len(set(projects)) or not groups:
        raise ValueError("Manifest must contain unique projects and nonempty groups")
    if any(Path(p).name != p or not p.endswith(".Tests") for p in projects):
        raise ValueError("Invalid test project name")
    ids = [g["id"] for g in groups]
    if len(ids) != len(set(ids)):
        raise ValueError("Duplicate acceptance group")
    suites, errors = {}, []
    for project in projects:
        try:
            suites[project] = read_suite(results_dir / f"{project}.trx")
        except (OSError, ET.ParseError, ValueError, KeyError) as exc:
            errors.append(f"{project}: {exc}")
    for project, suite in suites.items():
        for result in suite["results"]:
            if result["outcome"] not in ("Passed", "NotExecuted"):
                errors.append(f"{project}: {result['name']} => {result['outcome']}")
    reports = []
    for group in groups:
        requirements = group["required_tests"]
        if not requirements:
            raise ValueError(f"{group['id']}: empty required test list")
        failures, matched = [], []
        seen = set()
        for requirement in requirements:
            project, method, minimum = (
                requirement["project"], requirement["method"], requirement["minimum_cases"]
            )
            if project not in projects or not method.startswith(project + "."):
                raise ValueError(f"{group['id']}: invalid project/method")
            if type(minimum) is not int or minimum < 1 or (project, method) in seen:
                raise ValueError(f"{group['id']}: duplicate test or invalid minimum_cases")
            seen.add((project, method))
            rows = [r for r in suites.get(project, {}).get("results", []) if r["method"] == method]
            if len(rows) < minimum:
                failures.append(f"{method}: expected at least {minimum} cases, found {len(rows)}")
            for row in rows:
                if row["outcome"] != "Passed":
                    failures.append(f"{row['name']}: {row['outcome']}")
            matched.extend(rows)
        reports.append({
            "id": group["id"],
            "title": group["title"],
            "automated_status": "failed" if failures else "passed",
            "result_count": len(matched),
            "failures": failures,
            "remaining_acceptance": group["remaining_acceptance"],
            "tests": matched,
        })
    return {
        "schema_version": 1,
        "automated_status": "failed" if errors or any(g["failures"] for g in reports) else "passed",
        "errors": errors,
        "suites": suites,
        "groups": reports,
        "notice": "Automated evidence only. Remaining acceptance is not waived; no tracker status is changed.",
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--results", type=Path, default=Path("TestResults"))
    parser.add_argument("--manifest", type=Path, default=Path(__file__).with_name("windows-acceptance.json"))
    args = parser.parse_args(argv)
    try:
        report = evaluate(json.loads(args.manifest.read_text(encoding="utf-8")), args.results)
    except (OSError, ValueError, KeyError, TypeError) as exc:
        report = {"schema_version": 1, "automated_status": "failed", "errors": [str(exc)], "suites": {}, "groups": []}
    source_root = Path(__file__).resolve().parent.parent
    try:
        def git(*arguments):
            return subprocess.check_output(["git", *arguments], cwd=source_root, text=True).strip()
        report["source"] = {"commit": git("rev-parse", "HEAD"), "tree": git("rev-parse", "HEAD^{tree}")}
        if git("status", "--porcelain", "--untracked-files=no"):
            raise ValueError("Tracked source differs from the committed tree")
    except (OSError, subprocess.CalledProcessError, ValueError) as exc:
        report["errors"].append(str(exc))
        report["automated_status"] = "failed"
    report["runtime"] = platform.platform()
    report["created_at"] = datetime.now(timezone.utc).isoformat()
    report["workflow"] = {key: os.environ.get(key) for key in (
        "GITHUB_REPOSITORY", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "GITHUB_SHA"
    )}
    if platform.system() != "Windows":
        report["errors"].append("Windows acceptance must execute on Windows; cross-builds are not runtime evidence")
        report["automated_status"] = "failed"
    args.results.mkdir(parents=True, exist_ok=True)
    (args.results / "windows-acceptance.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    lines = ["# Windows automated acceptance", "", report["automated_status"].upper(), "",
             "Automated evidence only; this does not close tracker tasks or waive remaining acceptance.", ""]
    lines.extend(f"- {error}" for error in report["errors"])
    for project, suite in report["suites"].items():
        lines.append(f"- {project}: {suite['total']} results, {suite['outcomes']}")
    for group in report["groups"]:
        lines.extend(["", f"## {group['title']}: {group['automated_status']} ({group['result_count']} results)"])
        lines.extend(f"- FAIL: {f}" for f in group["failures"])
        lines.extend(f"- Remaining: {r}" for r in group["remaining_acceptance"])
    summary = "\n".join(lines) + "\n"
    (args.results / "windows-acceptance.md").write_text(summary, encoding="utf-8")
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as output:
            output.write(summary)
    print(summary)
    return 0 if report["automated_status"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(main())
