# World Execution

OpenConquer uses a server authoritative world model with explicit ownership of mutable runtime
state.

The core rule is:

> **Every mutable runtime aggregate has one authoritative owner at any point in time.**

For spatial gameplay, that owner is a `WorldPartition`.

Network connections, persistence workers, timers, and unrelated partitions do not directly mutate
partition owned state.

## Implemented Core and Current Boundary

The initial authoritative execution core is implemented in
`src/OpenConquer.GameServer/World/Execution/` and tested in
`tests/OpenConquer.GameServer.Tests/World/Execution/`. The GameServer Generic Host now
verifies the required database schemas and canonical content before constructing and publishing
the runtime. World-session admission from completed existing-character bootstrap is implemented,
but no network listener or ongoing gameplay dispatcher is wired to it yet.

`WorldRuntime` constructs a `WorldScheduler` from the validated immutable
`WorldMapContentCatalog`. Each world `MapId` receives one `WorldPartition` containing one
`MapInstance`, which retains the **same** canonical `WorldMapContent`, effective static collision,
and base terrain references. `MapId`, `MapDataId`, and 64-bit map flags remain distinct and intact.
There is no fallback map or synthetic terrain.

`WorldExecutionOptions` explicitly configures worker count, pending commands per partition,
maximum commands per execution turn, global resident character capacity, and per-map resident
character capacity. Each partition has a finite FIFO command queue. Once a
command is accepted, the partition is placed on the shared bounded ready channel at most once
until it finishes its turn; additional accepted work uses the existing readiness claim. Workers
execute synchronous, exclusive turns and requeue a busy partition after its command-count quantum.
Different partitions can progress concurrently. No per-map thread or general-purpose actor
framework is introduced. A full command queue rejects admission immediately rather than silently
dropping accepted commands.

`WorldRuntime.AdmitCharacterAsync` reserves a player identity across **all** partitions before
submitting its map-local admission command. Duplicate or concurrent admission is rejected. The
persisted map must exist in the canonical catalog, and the supplied coordinates must fall within
the validated static collision grid dimensions. **Bounds validation is not walkability, occupancy,
or movement authorization.** The partition-owned `MapInstance` currently stores only a character
ownership record and the admitted coordinates; monsters, NPCs, timers, visibility, and combat state
remain future gameplay features.

Each admission returns a `WorldCharacterOwnership` containing character ID, world map ID,
nonzero generation, and a runtime-instance identity. Tokens from previous runtime instances cannot
refer to characters in a newly started runtime, even if their generations coincide. `RemoveCharacterAsync` and `ReadCharacterAsync` accept
that complete ownership token, so stale generations cannot remove or read a replacement character.
Successful removal releases the global reservation, allowing a later admission with a new
generation. This is not yet a map-transfer or network-session ownership protocol.

Caller cancellation is checked **before** command acceptance. After acceptance, caller cancellation
does not discard or roll back the submitted command: its result must be observed by the owner.
Failure callbacks release or restore admission/removal reservations as appropriate. On shutdown,
new work is rejected, queued commands are failed explicitly, executing turns are allowed to
finish, workers are joined, and the remaining map-local character state and global reservations
are cleared. Runtime instances cannot be restarted after shutdown.

There is no tick loop, gameplay command router, durable world transaction, event replication,
or production load-tuned capacity policy in this core. Those remain separate slices, governed by
the design requirements below.

## World-Session Admission and Cleanup

`WorldSessionAdmissionProcessor` takes exclusive ownership of
`ExistingCharacterBootstrapCompleteConnection`, verifies that its map is the **same canonical
object** retained by the runtime, and awaits world admission with authenticated character identity
and persisted coordinates. Caller cancellation or presence revocation after world admission still
releases the admitted token and closes the transferred connection. A failed admission cannot leave
an orphaned presence lease or map-local character. Hydrated bootstrap snapshots remain available
through the new `WorldSession`; no new client packets are emitted by promotion.

A `WorldSession` owns the live `ExistingCharacterGameConnection` (including its revocable realm
presence lease) and the runtime-scoped `WorldCharacterOwnership`. Concurrent disposal is
idempotent. Revocation and runtime stopping initiate the same teardown. `Completion` exposes the
terminal cleanup result for runtime shutdown to supervise; connection-disposal and
world-release failures are preserved. The running world host does not install a game-session
processing loop or automatically retry conflicting new logins while an old owner is still being
released; admission fails closed until the previous ownership has actually ended.

Ordinary partition commands are still bounded by `PartitionQueueCapacity`, but cleanup is admitted
through a separate **bounded high-priority removal lane**. Its capacity equals the per-map resident
character limit, and at most one removal may be outstanding for each active character. Therefore a
full ordinary mailbox cannot strand a disconnect. Session teardown never abandons an accepted
removal due to caller cancellation. If the world is stopping, cleanup waits for partition shutdown
and authoritative state clearing. Runtime shutdown awaits registered session and admission
completion. The Generic Host owns that shutdown and propagates its failures.

## World Partitions

A `WorldPartition` is the unit of authoritative world execution.

A partition is not permanently defined as a map. The initial placement policy assigns one map
instance to one partition because maps are the natural locality boundary for Conquer gameplay.

The initial implemented map-local state is a character roster containing ownership tokens and
admitted coordinates. The broader target state (not yet implemented) is:

```text
WorldPartition
└── MapInstance
    ├── Players
    ├── Monsters
    ├── NPCs
    ├── Ground items
    ├── Portals
    ├── Combat state
    ├── Visibility state
    └── Timers
```

This distinction leaves room for future placement policies without changing the ownership model.

Small maps could eventually share a partition. A particularly expensive workload could receive
dedicated execution resources.

Splitting a single map across multiple authoritative partitions is not part of the initial design.
That adds significant complexity to movement, combat, visibility, AI, and boundary interactions and
should only be considered if profiling demonstrates a real need.

## Execution Model

A partition does not own a dedicated operating system thread.

Partitions execute through a shared scheduler using normal .NET worker threads.

```mermaid
flowchart TD
    Router["World Router"]

    Router --> A["Partition A Mailbox"]
    Router --> B["Partition B Mailbox"]
    Router --> C["Partition C Mailbox"]

    A --> Ready["Runnable Partition Queue"]
    B --> Ready
    C --> Ready

    Ready --> Scheduler["World Scheduler"]

    Scheduler --> Worker1["Worker"]
    Scheduler --> Worker2["Worker"]
    Scheduler --> Worker3["Worker"]
```

Different partitions may execute concurrently.

The same partition may not execute two mutation turns concurrently.

```text
Partition A + Partition B = allowed concurrently

Partition A + Partition A = never allowed concurrently
```

The runtime must preserve:

```text
ConcurrentMutationTurns(partition) <= 1
```

This is logical ownership, not thread affinity. A partition may execute on different worker threads
over its lifetime.

## Cooperative Scheduling

A busy partition must not monopolize execution resources.

Each scheduled partition receives a bounded execution turn.

The current scheduler uses the configured `MaxCommandsPerTurn` count. A time-budget quantum or
other scheduling limits require separate implementation and measurement.

```text
Runnable
   ↓
Claim exclusive execution
   ↓
Process bounded work
   ↓
Work remaining?
   ├── Yes -> requeue
   └── No  -> idle
```

The command-count quantum is configurable rather than an architectural constant. Production
values still require benchmarking and load testing.

A partition must only appear once in the runnable queue while it is already queued or executing.

## Command Flow

Client packets represent requests.

They do not directly change world state.

```mermaid
flowchart LR
    Client["Client"]
    Transport["Transport"]
    Protocol["Protocol"]
    Adapter["GameServer Adapter"]
    Command["World Command"]
    Router["World Router"]
    Mailbox["Bounded Mailbox"]
    Executor["Partition Executor"]
    State["Authoritative State"]

    Client --> Transport
    Transport --> Protocol
    Protocol --> Adapter
    Adapter --> Command
    Command --> Router
    Router --> Mailbox
    Mailbox --> Executor
    Executor <--> State
```

Typical commands include:

```text
MoveCharacter
JumpCharacter
AttackEntity
UseItem
EquipItem
InteractWithNpc
EnterPortal
```

Commands contain identifiers and values needed to express intent.

They do not contain mutable `GameSession` references, EF Core entities, `DbContext` instances, or
direct references to state owned by another partition.

The partition resolves the authoritative entity and decides whether the requested action is valid.

## Server Authority

The client is not trusted as the source of consequential game state.

A movement packet means:

```text
Request this movement.
```

It does not mean:

```text
Set the character position to this value.
```

Movement validation can use:

- current authoritative position
- map bounds
- passability
- movement rules
- character state
- elapsed simulation time
- active restrictions or abilities

The same rule applies to combat, inventory, equipment, trading, NPC interaction, and other gameplay
systems.

The client requests an action. The server determines the result.

## Partition Owned State

A world partition owns mutable spatial state associated with the gameplay it executes.

Typical partition owned state includes:

```text
Characters
Monsters
NPC runtime state
Ground items
Movement
Combat
Visibility indexes
Status effects
Respawns
Map timers
Map events
```

Ordinary mutable collections can be used inside the partition because ownership prevents concurrent
mutation.

Code outside the partition must not obtain these objects and modify them directly.

Cross partition communication uses immutable commands, messages, identifiers, or snapshots.

```mermaid
flowchart LR
    A["Partition A"]
    Message["Immutable Message"]
    B["Partition B"]

    A --> Message
    Message --> B
```

Thread safe collections are still appropriate for infrastructure such as routing tables when needed,
but they are not a replacement for authoritative ownership.

## Non Spatial State

Not all server state belongs to a world partition.

Systems such as these may require different ownership boundaries:

```text
Guilds
Realm presence
Global chat
Rankings
Account state
Cross map services
```

The general rule remains the same:

> **Mutable state must have an explicit authoritative owner.**

A world partition is the default owner for spatial gameplay. It is not the universal owner for every
subsystem in the server.

## Ordering

Ordering guarantees are local.

### Connection Input

TCP provides an ordered byte stream for one connection.

Transport and protocol processing preserve that order while producing complete client messages.

### Partition Execution

Commands accepted by one partition execute in a defined order.

A partition local sequence number can be assigned when work enters authoritative execution.

```text
4101
4102
4103
4104
```

Sequence information can be recorded for diagnostics and replay.

### Connection Output

Each client connection has one ordered send progression.

This is required for protocol ordering and connection specific stateful cryptography.

### Cross Partition Operations

There is no global total order across the entire realm.

Cross partition operations use explicit messages, operation identifiers, and ownership rules.

## Bounded Work

All asynchronous queues have finite capacity.

Partition mailboxes must never grow without limit.

```mermaid
flowchart LR
    Producers["Command Producers"]
    Queue["Bounded Mailbox"]
    Executor["Partition Executor"]

    Producers --> Queue
    Queue --> Executor
```

The full queue policy depends on the type of work.

Critical gameplay commands cannot silently disappear.

Superseded or low value work may support coalescing or controlled shedding if the protocol and game
semantics allow it.

Queue capacities are selected using measured:

```text
command rate
processing latency
queue age
memory consumption
peak map population
overload behavior
```

## Timers and Simulation Time

OpenConquer does not require one global high frequency game loop.

Partitions execute when commands or scheduled gameplay work are ready.

Timed systems include:

- monster AI
- regeneration
- status effects
- cooldowns
- respawns
- delayed actions
- map events

```mermaid
flowchart TD
    Commands["Commands"]
    Timers["Due Timers"]

    Commands --> Scheduler["Partition Scheduler"]
    Timers --> Scheduler

    Scheduler --> Partition["Partition Execution"]
```

Idle maps should not consume continuous simulation time for no reason.

Catch up work must also be bounded. A partition that falls behind must not execute an unlimited
backlog of overdue timer work in one turn.

## Time Sources

Simulation durations use monotonic time.

Examples include:

```text
movement timing
attack cooldowns
buff duration
AI timing
scheduler measurements
```

These use `TimeProvider` timestamp and elapsed time APIs.

Wall clock time is reserved for calendar based behavior such as:

```text
ticket expiration
scheduled events
daily resets
audit timestamps
```

Gameplay code should not read arbitrary global clocks throughout the codebase.

Controlled time sources also make simulation tests reproducible.

## Randomness and Replay

Gameplay randomness should use an explicit game RNG abstraction where the result affects
authoritative behavior.

Important replay inputs include:

```text
Initial state
Command sequence
Controlled time
RNG state
Stable entity identifiers
```

A deterministic test can then reproduce a world transition from known inputs.

```text
Initial State
+
Commands
+
Time
+
Random State
=
Events + Final State
```

The goal is deterministic gameplay behavior where useful.

It is not necessary to reproduce ThreadPool scheduling.

## External I/O

Authoritative world turns do not await uncontrolled external I/O.

This includes:

```text
database operations
socket writes
HTTP requests
filesystem operations
external services
```

A world turn that requires external work produces a request and completes.

```mermaid
flowchart LR
    Partition["World Partition"]
    Request["Persistence Request"]
    Infrastructure["Infrastructure"]
    Database["MySQL"]

    Partition --> Request
    Request --> Infrastructure
    Infrastructure --> Database

    Database --> Completion["Completion Command"]
    Completion --> Partition
```

The result returns later as another command.

This keeps database or network latency from holding a map execution turn open.

## Durable Operations

Some operations cannot be acknowledged until persistence succeeds.

Examples include item ownership transfers and critical currency changes.

These operations use an explicit pending state.

```text
Gameplay request
   ↓
Validate
   ↓
PendingCommit
   ↓
Submit transaction
   ↓
Persistence completion
   ├── Success -> finalize and acknowledge
   └── Failure -> restore or reject
```

Other characters and unrelated systems in the same partition can continue executing while the
transaction is pending.

## World Events and Networking

The world does not send packets directly.

World execution produces events such as:

```text
CharacterMoved
EntitySpawned
EntityRemoved
DamageApplied
InventoryChanged
```

The GameServer converts relevant events into protocol messages for the affected connections.

```mermaid
flowchart LR
    Partition["World Partition"]
    Event["World Event"]
    Replication["Interest / Replication"]
    Adapter["GameServer Adapter"]
    Protocol["Protocol Encode"]
    Transport["Transport"]

    Partition --> Event
    Event --> Replication
    Replication --> Adapter
    Adapter --> Protocol
    Protocol --> Transport
```

This keeps socket lifetime, send buffering, protocol state, and encryption outside the authoritative
simulation.

## Visibility

Visibility belongs to the authoritative world.

The server must not use realm wide broadcast as the normal replication mechanism.

Each partition maintains an appropriate spatial or visibility index.

```mermaid
flowchart LR
    Changed["Changed Entity"]
    Spatial["Spatial Index"]
    Candidates["Nearby Candidates"]
    Rules["Visibility Rules"]
    Recipients["Relevant Observers"]

    Changed --> Spatial
    Spatial --> Candidates
    Candidates --> Rules
    Rules --> Recipients
```

The initial implementation can use map appropriate cells or buckets.

The exact spatial structure should be chosen and optimized through profiling.

The architectural requirement is that visibility work scales primarily with relevant nearby entities
rather than total connected population.

## Character Ownership

A character active in the world has one partition owner and one ownership generation.

Conceptually:

```text
CharacterId: 1000001
Partition:   1002
Generation:  42
```

A successful ownership transfer increments the generation.

```text
CharacterId: 1000001
Partition:   1011
Generation:  43
```

Commands associated with stale ownership cannot mutate the new owner.

This protects against delayed work, transfer races, reconnect races, and stale messages.

## Map Transfers

Moving a character between map partitions is an ownership transfer.

The invariant is:

> **A character may have at most one active world owner.**

Transfers use an operation identifier and explicit state.

```mermaid
sequenceDiagram
    participant Source as Source Partition
    participant Destination as Destination Partition
    participant Router as World Router

    Source->>Source: Mark Transferring
    Source->>Destination: Prepare Transfer
    Destination-->>Source: Prepared
    Source->>Source: Relinquish Ownership
    Source->>Destination: Commit Transfer
    Destination->>Destination: Activate New Generation
    Destination->>Router: Publish New Owner
```

The transfer identifier makes retries and duplicate messages detectable.

Ownership generations prevent stale commands from modifying the character after ownership changes.

The initial transfer implementation operates inside one GameServer process.

If partitions are later distributed across processes or machines, additional recovery and fencing
mechanisms may be required. The in process transfer protocol should not be treated as a complete
distributed transaction design.

## Scaling

The initial GameServer hosts many partitions inside one process.

```text
GameServer
├── WorldPartition A
├── WorldPartition B
├── WorldPartition C
└── WorldPartition D
```

The scheduler distributes runnable partitions across available CPU resources while preserving
exclusive execution per partition.

This allows maps to execute in parallel without introducing concurrent mutation inside each map.

A future deployment may distribute partitions across GameServer processes:

```text
WorldPartition A -> GameServer 1
WorldPartition B -> GameServer 1
WorldPartition C -> GameServer 2
WorldPartition D -> GameServer 3
```

That step should only be taken when measurements justify the additional operational and distributed
systems complexity.

Useful signals include:

```text
partition scheduling lag
CPU usage
hot partition concentration
allocation and GC pressure
visibility fanout
working set
network throughput
```

Connected player count alone is not a sufficient capacity signal.

## Hot Partitions

Single owner execution places a serial mutation limit on one partition.

This is intentional.

The initial response to a hot map should be measurement and optimization, including:

```text
visibility queries
replication fanout
allocations
AI scheduling
combat processing
timer work
packet generation
database interactions
```

A map should not be split into multiple concurrent owners simply because additional CPU cores are
available.

Finer grained ownership should only be introduced if profiling proves that a single partition's
execution ceiling is a real production limitation.

## Runtime Rules

The world execution model follows these rules:

```text
1. Every mutable runtime aggregate has an explicit authoritative owner.

2. Spatial world state is owned by a WorldPartition.

3. A partition normally owns one map instance in the initial implementation.

4. A partition never executes two mutation turns concurrently.

5. Busy partitions yield through bounded execution turns.

6. Network sessions submit commands and do not own gameplay state.

7. Cross partition communication uses messages, not shared mutable world objects.

8. Partition mailboxes and other asynchronous boundaries are bounded.

9. World turns do not await uncontrolled external I/O.

10. Persistence results return to the world as commands.

11. World execution produces events rather than sending packets directly.

12. Visibility is interest filtered and partition owned.

13. Simulation durations use monotonic time.

14. Authoritative randomness and time are controllable for testing.

15. Character ownership changes use operation identifiers and ownership generations.

16. Multi process distribution is an evolution of the ownership model, not a prerequisite for it.
```

These rules are the concurrency and execution foundation for the GameServer runtime.
