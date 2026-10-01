#!/usr/bin/env python3
"""Bounded validity/fulfillment gate; parallel elapsed times are not speed evidence."""

import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import zipfile

ROOT = Path(__file__).resolve().parents[1]
# Exact demand and sheet count: empty, skipped, and partial jobs must never pass.
EXPECTED = {
    "single-triangle": (1, 1),
    "paired-wedges": (2, 1),
    "repeated-ell-fill": (7, 1),
    "mixed-irregular": (6, 1),
    "rotation-edge-fit": (1, 1),
    "multi-sheet": (3, 3),
}


class GateError(Exception):
    pass


def check_fixtures(directory):
    files = list(directory.rglob("*.nest"))
    names = [path.stem for path in files]
    if len(names) != len(EXPECTED) or set(names) != set(EXPECTED):
        raise GateError(f"Expected exactly six fixtures {sorted(EXPECTED)}; found {sorted(names)}")
    if list(directory.rglob("*.manifest.json")):
        raise GateError("Unexpected manifest in synthetic fixture directory")
    hashes = {}
    for path in sorted(files):
        with zipfile.ZipFile(path) as archive:
            metadata = json.loads(archive.read("nest.json").decode("utf-8-sig"))
            drawings = metadata["drawings"]
            demand = sum(drawing["quantity"]["required"] for drawing in drawings)
            if demand != EXPECTED[path.stem][0] or not drawings:
                raise GateError(f"Unexpected demand in {path.name}: {demand}")
            if any(metadata.get(key) for key in ("customer", "madeBy")):
                raise GateError(f"Non-synthetic identity metadata in {path.name}")
            for drawing in drawings:
                if drawing.get("customer") or drawing.get("source", {}).get("path"):
                    raise GateError(f"Non-synthetic drawing metadata in {path.name}")
            if archive.comment or any(entry.comment for entry in archive.infolist()):
                raise GateError(f"Unexpected ZIP comments in {path.name}")
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    return hashes


def check_csv(path):
    seen = set()
    outcomes = []
    with path.open(newline="", encoding="utf-8-sig") as stream:
        reader = csv.DictReader(stream)
        required = {"Job", "Engine", "Valid", "Crashed", "FullyPlaced", "PartsPlaced",
                    "PartsRequested", "PlatesUsed", "Notes"}
        if not required.issubset(reader.fieldnames or []):
            raise GateError("Missing benchmark CSV columns")
        for row in reader:
            if row["Engine"] == "Baseline":
                continue
            if row["Engine"] != "Irregular":
                raise GateError(f"Unexpected engine: {row['Engine']}")
            name = row["Job"]
            if name not in EXPECTED or name in seen:
                raise GateError(f"Unknown or duplicate fixture result: {name}")
            seen.add(name)
            demand, sheets = EXPECTED[name]
            if (row["Valid"] != "True" or row["Crashed"] != "False"
                    or row["FullyPlaced"] != "True" or row["Notes"].strip()):
                raise GateError(f"Invalid, crashed, timed-out or incomplete result: {name}: {row}")
            if (int(row["PartsRequested"]) != demand or int(row["PartsPlaced"]) != demand
                    or int(row["PlatesUsed"]) != sheets):
                raise GateError(f"Unexpected fulfillment or sheet count: {name}: {row}")
            outcomes.append(row)
    if seen != set(EXPECTED):
        raise GateError(f"Missing fixture results: {sorted(set(EXPECTED) - seen)}")
    return outcomes


def terminate_tree(process):
    if os.name == "nt":
        if process.poll() is None:
            subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                           check=True, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT,
                           timeout=10)
    else:
        # Also remove any descendants left after their parent exits. The session is ours alone.
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
    process.wait(timeout=10)


def run_process(command, deadline, log):
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise GateError("Whole-check watchdog expired")
    print("Running: " + " ".join(map(str, command)), flush=True)
    with log.open("w", encoding="utf-8") as output:
        process = subprocess.Popen(command, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT,
                                   start_new_session=os.name != "nt",
                                   creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0)
        try:
            code = process.wait(timeout=remaining)
            if code:
                raise GateError(f"Subprocess exited {code}; see {log}")
        except subprocess.TimeoutExpired as error:
            raise GateError(f"Whole-check watchdog expired; see {log}") from error
        finally:
            terminate_tree(process)


def bounded_int(low, high):
    def parse(value):
        number = int(value)
        if not low <= number <= high:
            raise argparse.ArgumentTypeError(f"must be between {low} and {high}")
        return number
    return parse


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--parallel", type=bounded_int(1, 4), default=2)
    parser.add_argument("--timeout", type=bounded_int(1, 300), default=300,
                        help="whole build + solve budget, seconds (maximum 300)")
    parser.add_argument("--fixtures", type=Path, default=ROOT / "test-data/synthetic-nests")
    parser.add_argument("--output", type=Path, help="retain logs/CSV/provenance in this directory")
    parser.add_argument("--check-csv", type=Path, help="check an existing CSV without solving")
    args = parser.parse_args(argv)
    try:
        if args.check_csv:
            outcomes = check_csv(args.check_csv)
        else:
            deadline = time.monotonic() + args.timeout
            hashes = check_fixtures(args.fixtures)
            output = args.output or Path(tempfile.mkdtemp(prefix="opennest-synthetic-"))
            output = output.resolve()
            output.mkdir(parents=True, exist_ok=True)
            (output / "accepted.json").unlink(missing_ok=True)
            print(f"Evidence directory: {output}", flush=True)
            project = ROOT / "OpenNest.Benchmark/OpenNest.Benchmark.csproj"
            run_process(["dotnet", "build", str(project), "-c", "Release",
                         "--disable-build-servers"], deadline, output / "build.log")
            assembly = ROOT / "OpenNest.Benchmark/bin/Release/net8.0/OpenNest.Benchmark.dll"
            command = ["dotnet", str(assembly), str(args.fixtures.resolve()),
                       "--engines", "Irregular", "--parallel", str(args.parallel),
                       "--progress", "--csv", str(output / "results.csv")]
            run_process(command, deadline, output / "benchmark.log")
            outcomes = check_csv(output / "results.csv")
            if check_fixtures(args.fixtures) != hashes:
                raise GateError("Fixture inputs changed during the check")
            (output / "accepted.json").write_text(json.dumps({
                "fixtures": hashes, "parallel": args.parallel,
                "benchmark_command": list(map(str, command)), "outcomes": outcomes,
                "benchmark_assembly_sha256": hashlib.sha256(assembly.read_bytes()).hexdigest(),
                "checker_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                "timing": "diagnostic only; not speed evidence",
            }, indent=2) + "\n", encoding="utf-8")
        for row in sorted(outcomes, key=lambda row: row["Job"]):
            print(f"PASS {row['Job']}: {row['PartsPlaced']}/{row['PartsRequested']} parts, "
                  f"{row['PlatesUsed']} sheet(s)")
        print("PASS: exactly six Irregular results, all valid and fully placed")
        return 0
    except KeyboardInterrupt:
        print("FAIL: cancelled; owned subprocess tree terminated", file=sys.stderr)
        return 130
    except (GateError, OSError, ValueError, KeyError, zipfile.BadZipFile,
            subprocess.SubprocessError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1


def cancel(_signum, _frame):
    raise KeyboardInterrupt()


if __name__ == "__main__":
    # Treat CI/terminal termination the same as Ctrl-C; run_process's finally owns cleanup.
    signal.signal(signal.SIGTERM, cancel)
    sys.exit(main())
