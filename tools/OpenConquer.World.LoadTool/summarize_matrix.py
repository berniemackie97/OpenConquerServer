#!/usr/bin/env python3
"""Summarize a complete load matrix; retain per-trial p99 ranges instead of pooling quantiles."""

import argparse
from collections import defaultdict
import json
from pathlib import Path
from statistics import median


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    manifest = json.loads((args.directory / "manifest.json").read_text())
    trials = manifest["trials"]
    if len(trials) != manifest["expectedTrialCount"]:
        parser.error("Matrix is incomplete; no summary emitted.")
    groups = defaultdict(list)
    for trial in trials:
        report = json.loads((args.directory / trial["file"]).read_text())
        options = report["Options"]
        measured = report["Measurement"]
        if measured["CompletedCommands"] != options["Sessions"] * options["Steps"] or report["VerifiedCharacters"] != options["Sessions"]:
            parser.error("Trial command/character counts do not match its scenario.")
        groups[options["Sessions"], options["Maps"], options["Workers"]].append(measured)
    scenarios = []
    for (sessions, maps, workers), rows in sorted(groups.items()):
        p99 = [r["Completion"]["P99Microseconds"] for r in rows]
        scenarios.append({
            "sessions": sessions, "maps": maps, "workers": workers, "trials": len(rows),
            "commandsPerSecondMedian": median(r["CommandsPerSecond"] for r in rows),
            "completionP50UsMedian": median(r["Completion"]["P50Microseconds"] for r in rows),
            "completionP99UsMedian": median(p99),
            "completionP99UsMin": min(p99), "completionP99UsMax": max(p99),
            "submissionP99UsMedian": median(r["Submission"]["P99Microseconds"] for r in rows),
            "submissionBytesPerCommandMedian": median(r["SubmissionBytesPerCommand"] for r in rows),
            "processBytesPerCommandMedian": median(r["ProcessBytesPerCommand"] for r in rows),
            "elapsedSecondsMin": min(r["ElapsedSeconds"] for r in rows),
            "elapsedSecondsMax": max(r["ElapsedSeconds"] for r in rows),
        })
    print(json.dumps({"schemaVersion": 1, "revision": manifest["revision"], "trials": len(trials),
                      "totalMeasuredCommands": sum(r["CompletedCommands"] for rows in groups.values() for r in rows),
                      "scenarios": scenarios}, indent=2))


if __name__ == "__main__":
    main()
