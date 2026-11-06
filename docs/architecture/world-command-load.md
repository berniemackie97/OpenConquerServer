# World command load baseline — CP1.4

Measured 2026-11-05 on the merged CP1.3 server baseline `718eaf7` plus the load
harness commit `2a406b2387d6d3b7e7fcaa7c825af888a7f15a3c`. Production runtime, scheduler, partition and command
implementation files are unchanged. One friend-assembly grant lets the tool exercise
those internal types. This is modern engineering evidence (ledger kind M).

## Result and decision

**Retain the global residency gate, command representation and configured worker
policy in this slice.** Synchronous walk submission allocated approximately **336
managed bytes/command** across the matrix. Scaling across maps/workers varied with
load; these measurements do not isolate a gate bottleneck or establish a workload
budget requiring a redesign. D4 recommendation is retain; Bernie review is pending.

At 1,024 logical sessions with four workers, median per-trial completion p99 was
1.970 ms / 2.921 ms / 2.151 ms for 1 / 8 / 32 maps. Corresponding submission p99 was
11.3 / 31.7 / 13.8 µs. These are separate distributions; subtracting their percentiles
would not measure queue wait or identify which lock caused an individual delay.

## Workload and provenance

- Apple M4; 10 logical processors; Arm64; macOS 26.6.2; SDK 10.0.403;
  .NET 10.0.14; workstation GC; default minimum ThreadPool worker count 10.
- Each logical session owns a resident character and has one outstanding command.
  Admission is completed before sampling. Directions 0/4 alternate on synthetic
  flat 3x3 maps. Every returned snapshot and final residency/position is checked.
- Existing orientation authority: client tables `0x86C608` / `0x86C628`,
  `20_notes/Network/15_msgwalk_10005_direction_index_order.md`. The tool changes no
  native behavior and makes no native server performance claim.
- Queue capacity 1,024; turn budget 64. Three isolated-process repetitions of each
  scenario, interleaved with reversed order for repetition two. Four-worker matrix
  at 32/256/1,024 sessions and 1/8/32 maps; one-worker controls at 1,024 sessions.
- Warmup: 1,024 walks per session. Measured: 1,048,576 walks per trial. **36 trials,
  37,748,736 measured walks**, all completed with validated positions and ownership.
  Measured phase duration was 0.337–1.084 seconds per trial.
- The host was interactive. CPU affinity, power policy and background activity were
  uncontrolled. World workers and load producers share the process ThreadPool.

The [manifest](evidence/world-load-2026-11-05/manifest.json) pins source revision,
clean starting tree, runtime-source hashes, tool assembly hash, environment and each
command. [Raw trial files and summary](evidence/world-load-2026-11-05/) preserve all
runs. The summary uses the **median of three per-trial statistics**; the range is the
minimum/maximum per-trial p99. Quantiles are not pooled.

## Latency and throughput

| Sessions | Maps | Workers | M commands/s | Completion p50 µs | Completion p99 µs | p99 range µs | Submission p99 µs |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 32 | 1 | 4 | 1.055 | 28.0 | 72.5 | 65.9–130.8 | 32.2 |
| 32 | 8 | 4 | 1.327 | 9.1 | 301.1 | 278.4–369.9 | 206.6 |
| 32 | 32 | 4 | 1.236 | 9.7 | 315.1 | 306.8–383.2 | 217.2 |
| 256 | 1 | 4 | 1.215 | 176.8 | 476.5 | 446.3–935.6 | 31.7 |
| 256 | 8 | 4 | 1.839 | 51.0 | 1082.9 | 963.3–1113.6 | 156.3 |
| 256 | 32 | 4 | 1.754 | 54.6 | 1060.3 | 883.0–1090.0 | 178.8 |
| 1024 | 1 | 1 | 2.818 | 205.8 | 2019.4 | 1889.2–2033.3 | 8.2 |
| 1024 | 1 | 4 | 2.674 | 253.5 | 1969.8 | 1729.7–2021.2 | 11.3 |
| 1024 | 8 | 1 | 2.220 | 362.3 | 1915.3 | 1760.0–2409.8 | 10.6 |
| 1024 | 8 | 4 | 2.081 | 206.9 | 2920.9 | 2555.3–3124.2 | 31.7 |
| 1024 | 32 | 1 | 2.775 | 279.5 | 1626.2 | 1499.8–1975.5 | 9.7 |
| 1024 | 32 | 4 | 2.830 | 165.5 | 2150.8 | 2018.8–5385.2 | 13.8 |

A logical session generates its next request after completion. Throughput is finite
phase wall throughput, including producer dispatch/validation. Closed-loop operation
can hide delay before a producer starts a command. This workload excludes TCP,
WorldSession pacing, visibility/packet fan-out, database work and content loading.
End-to-end capacity and fixed-rate arrival latency remain unmeasured.

## Allocations

Across scenario medians, synchronous submission was 336.000–336.001 bytes/command;
process-wide managed allocation was 336.0–358.2 bytes/command. The submission delta
covers gate/check/enqueue code, including command, delegate/closure and completion
machinery; it does not assign all 336 bytes to the lambda alone. The process delta
also includes scheduler and harness costs. This measures allocation traffic, not
retained heap or native allocation.

The sample arrays, client state machines and start gate are allocated before the
window. A full collection at the quiescent boundary precedes warmup and measurement;
those forced collections are outside reported collection counts. Trials observed
42–45 gen-0 collections, 0–1 gen-1 collections and no gen-2 collections inside
the measured window. Precise process
allocation snapshots bracket the full window. Current-thread allocation reads bracket
only the synchronous call, with no await between them. Measurement overhead remains
in timings; JSON, sorting and percentile calculations run afterward.

Allocation traffic can be budgeted as approximately `336 × walk commands/second`
bytes/second for this synchronous path. A production traffic rate/allocation budget
has not been established here. The one-worker controls use the same runtime gate and
command path; they do not provide a gate-off comparison or a production worker-count
recommendation.

## Reproduce and validate

See [tool usage and measurement boundaries](../../tools/OpenConquer.World.LoadTool/README.md).

```bash
dotnet restore OpenConquer.Server.slnx --locked-mode
dotnet build OpenConquer.Server.slnx -c Release --no-restore
python3 tools/OpenConquer.World.LoadTool/run_matrix.py --output /tmp/world-load-repeat
python3 tools/OpenConquer.World.LoadTool/summarize_matrix.py /tmp/world-load-repeat
```

Build/tests/format pass under the shared baseline. The new tool has 11 focused tests,
including fixed golden position loops, independent percentile vectors, invalid-load
bounds, JSON round-trip and cancellation. The existing GameServer suite has 873 passing
tests. CI runs the remaining gates. Performance numbers are evidence, not CI timing
thresholds. CP1.4 remains current until CI and Bernie review close the slice.
