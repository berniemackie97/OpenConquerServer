# World command load tool

CP1.4 measures the existing `WorldRuntime.TryWalkCharacterAsync` path. Logical
sessions are synthetic resident characters, each with one outstanding walk. This
does not run TCP, `WorldSession`, pacing, visibility/packet fan-out, database work,
or retail map content. It is a closed-loop stress test, not an online-player capacity
claim or a native 5517 performance comparison.

```bash
dotnet restore OpenConquer.Server.slnx --locked-mode
dotnet build OpenConquer.Server.slnx -c Release --no-restore
dotnet tools/OpenConquer.World.LoadTool/bin/Release/net10.0/OpenConquer.World.LoadTool.dll \
  --sessions 256 --maps 8 --workers 4 --steps 4096 --warmup 1024
python3 tools/OpenConquer.World.LoadTool/run_matrix.py --output /tmp/world-load-results
python3 tools/OpenConquer.World.LoadTool/summarize_matrix.py /tmp/world-load-results
```

`--help` lists all integer options. The CLI limits sessions, maps, total samples,
queues and timeout. Invalid input exits 2; deadline expiry exits 3; queue refusal,
runtime failure or a wrong position/ownership exits 4. Failed trials emit no success
JSON. The matrix runner stops on any failure and never overwrites an output folder.

The matrix uses 32/256/1024 sessions, 1/8/32 maps, four scheduler workers, and a
single-worker control at 1024 sessions. Each measured trial completes 1,048,576
commands; `--commands` changes that count. Warmup is 1024 commands per session.
The queue capacity is 1024 and turn budget 64. Three process-isolated repetitions
are interleaved, with the scenario order reversed for the second repetition.
Do not run other load tests concurrently. Record host activity when interpreting
run-to-run variation. The script records revision, dirty state, source/tool hashes,
SDK, runtime environment and commands with every set of raw trial reports.
The summarizer refuses incomplete matrices and reports medians of per-trial
statistics, including the range of per-trial p99 values; it does not pool quantiles.

## Measurement boundary

Two opposite existing ground directions (0/4) alternate between `(1,1)` and `(1,2)`
on synthetic flat 3x3 maps. Native orientation is already established by client
tables `0x86C608` / `0x86C628` in
`20_notes/Network/15_msgwalk_10005_direction_index_order.md`. Every move and final
ownership/position must match. The checked-in synthetic golden fixture tests odd
warmups, odd measured counts, multiple maps and uneven map populations. It is not
a captured native wire fixture.

Client tasks, sample arrays and the start gate are created before the window. A
full GC runs at the quiescent boundary before both warmup and measured phases.
Timing uses injected `TimeProvider` monotonic timestamps. JSON and statistics are
computed afterward. Percentiles use nearest rank, `ceil(p * N)`, not interpolation.

- **Completion latency:** just before calling the runtime through completion of
  its returned `ValueTask`; includes submission, scheduling, execution, continuation
  dispatch and small measurement overhead.
- **Submission latency:** synchronous runtime call duration. It combines gate wait,
  ownership checks, command construction and enqueueing; it is not isolated lock
  wait time.
- **Submission bytes/command:** current-thread managed allocation delta around the
  synchronous runtime call. Both counter reads occur before any await. Worker-side
  and continuation allocations are excluded.
- **Process bytes/command:** precise process-wide managed allocation delta divided
  by completed commands, including scheduler and harness overhead. It is not live
  heap growth and excludes native allocation.
- **Throughput:** completed commands divided by full phase wall time. This finite,
  closed-loop test can hide delay before producers start a command; it does not
  measure open-loop arrival delay or overload at a fixed external request rate.

Allocation definitions follow the .NET APIs:
[current thread](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread)
and [process total](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalallocatedbytes).
Do not infer the global gate is a bottleneck from these timings alone. Production
lock/command changes require relevant workload budgets and attribution evidence.
