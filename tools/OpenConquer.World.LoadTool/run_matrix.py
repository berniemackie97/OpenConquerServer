#!/usr/bin/env python3
"""Run isolated closed-loop trials and retain their JSON plus a reproducibility manifest."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--repetitions", default=3, type=int)
    parser.add_argument("--commands", default=1_048_576, type=int)
    parser.add_argument("--warmup", default=1024, type=int)
    args = parser.parse_args()
    if not 1 <= args.repetitions <= 10 or not 1024 <= args.commands <= 8_000_000 or args.commands % 1024 or not 1 <= args.warmup <= 7812:
        parser.error("Repetitions: 1..10; commands: 1024..8000000, multiple of 1024; warmup: 1..7812.")
    root = Path(__file__).resolve().parents[2]
    binary = root / "tools/OpenConquer.World.LoadTool/bin/Release/net10.0/OpenConquer.World.LoadTool.dll"
    if not binary.is_file():
        parser.error("Build the Release load tool before running the matrix.")
    args.output.mkdir(parents=True, exist_ok=False)
    source_files = ["src/OpenConquer.GameServer/World/Execution/" + name for name in
                    ["WorldRuntime.cs", "WorldScheduler.cs", "WorldPartition.cs", "WorldPartitionCommand.cs"]]
    manifest = {
        "schemaVersion": 1,
        "revision": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip(),
        "workingTree": subprocess.check_output(["git", "status", "--porcelain=v1"], cwd=root, text=True).splitlines(),
        "sdk": subprocess.check_output([args.dotnet, "--version"], cwd=root, text=True).strip(),
        "host": platform.platform(),
        "processor": subprocess.check_output(["sysctl", "-n", "machdep.cpu.brand_string"], text=True).strip()
        if platform.system() == "Darwin" else platform.processor(),
        "runtimePolicyEnvironment": {name: os.environ.get(name) for name in
                                     ["DOTNET_TieredCompilation", "DOTNET_TieredPGO", "DOTNET_gcServer"]},
        "sourceSha256": {name: hashlib.sha256((root / name).read_bytes()).hexdigest() for name in source_files},
        "toolSha256": hashlib.sha256(binary.read_bytes()).hexdigest(),
        "trials": [],
    }
    # Fixed total sessions while varying maps, plus single-worker controls at the largest load.
    scenarios = [(sessions, maps, 4) for sessions in (32, 256, 1024) for maps in (1, 8, 32)]
    scenarios += [(1024, maps, 1) for maps in (1, 8, 32)]
    manifest["expectedTrialCount"] = len(scenarios) * args.repetitions
    # Interleave repetitions so a scenario's three trials are not one adjacent thermal burst.
    for repetition in range(1, args.repetitions + 1):
        ordered = scenarios if repetition % 2 else list(reversed(scenarios))
        for sessions, maps, workers in ordered:
            name = f"s{sessions}-m{maps}-w{workers}-r{repetition}.json"
            command = [args.dotnet, str(binary), "--sessions", str(sessions), "--maps", str(maps),
                       "--workers", str(workers), "--steps", str(args.commands // sessions), "--warmup", str(args.warmup),
                       "--queue-capacity", "1024", "--turn-budget", "64"]
            result = subprocess.run(command, cwd=root, text=True, capture_output=True, timeout=150, check=False)
            if result.stderr:
                (args.output / (name + ".stderr")).write_text(result.stderr)
            result.check_returncode()
            report = json.loads(result.stdout)
            (args.output / name).write_text(json.dumps(report, indent=2) + "\n")
            portable_command = ["dotnet", binary.relative_to(root).as_posix(), *command[2:]]
            manifest["trials"].append({"file": name, "command": portable_command, "repetition": repetition})
            (args.output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
            measured = report["Measurement"]
            print(f"{name}: {measured['CommandsPerSecond']:.0f}/s, p99 {measured['Completion']['P99Microseconds']:.1f} us", flush=True)


if __name__ == "__main__":
    main()
