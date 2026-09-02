# Networking Architecture

OpenConquer separates network transport mechanics from Conquer Online protocol semantics.

The boundary is simple:

```text
OpenConquer.Transport
    moves bytes

OpenConquer.Protocol
    interprets bytes
```

Transport owns connection lifetime, buffering, asynchronous I/O, ordering, backpressure, and network
resource limits.

Protocol owns framing, packet layouts, encodings, handshakes, cryptography, and other
Conquer-specific wire behavior.

> **Implementation status:** This document defines the transport architecture that future networking
> slices must follow. The complete transport runtime is not implemented yet.

## Goals

The transport layer is designed for:

- explicit resource ownership
- asynchronous socket I/O
- ordered per-connection input and output
- bounded buffering and queues
- explicit overload behavior
- deterministic connection shutdown
- compatibility with stateful protocol processing
- observability without gameplay coupling
- scalability without one operating-system thread per connection

## Project Boundary

`OpenConquer.Transport` owns:

```text
TCP listeners
accepted sockets
connection lifetime
asynchronous reads
asynchronous writes
input buffering
output progression
backpressure
transport cancellation
connection admission
transport resource limits
```

It does not own:

```text
packet identifiers
packet layouts
frame semantics
authentication rules
game commands
world state
persistence
```

The detailed wire contract belongs to [`OpenConquer.Protocol`](../protocol/README.md).

## Data Flow

Inbound traffic follows this boundary:

```text
Conquer client
      ↓
TCP socket
      ↓
Transport
      ↓
Protocol
      ↓
host adapter
      ↓
Application
      ↓
authoritative world
```

Outbound traffic travels in the opposite direction:

```text
authoritative world
      ↓
Application / replication
      ↓
host adapter
      ↓
Protocol
      ↓
Transport
      ↓
TCP socket
```

Gameplay code does not read or write sockets directly.

## Connection Ownership

Each transport connection owns its network resources for its lifetime.

Conceptually:

```text
TransportConnection
├── Socket
├── input state
├── output state
├── lifetime cancellation
└── transport diagnostics
```

The connection does not own authoritative gameplay state.

A connection may be associated with an account or character session, but character state, map state,
inventory, combat state, and other gameplay aggregates belong to the authoritative runtime.

## Connection Lifetime

Connection state progresses in one direction:

```text
Accepted
    ↓
Running
    ↓
Stopping
    ↓
Closed
```

Once shutdown begins, the connection cannot return to `Running`.

Shutdown may be triggered by:

- remote closure
- host shutdown
- read failure
- write failure
- protocol rejection
- authentication rejection
- timeout
- admission policy
- resource pressure
- administrative disconnect

Multiple shutdown signals may race.

Cleanup must therefore be idempotent, and final socket disposal must have one clear owner.

## Cancellation

A connection has one lifetime cancellation boundary representing termination of that transport
resource.

Individual operations may link:

- host shutdown
- semantic timeouts
- operation-specific cancellation

Connection cancellation is not a substitute for protocol state transitions or gameplay cancellation.

## Input Model

TCP is an ordered byte stream and does not preserve packet boundaries.

One socket read may contain:

```text
part of one frame
```

or:

```text
exactly one frame
```

or:

```text
several frames
```

Transport therefore buffers an ordered stream rather than treating socket reads as application
messages.

Conceptually:

```text
socket read
    ↓
transport input buffer
    ↓
Protocol framing
    ├── incomplete frame -> retain bytes
    └── complete frame   -> consume bytes
```

Transport owns the buffered memory.

Protocol may borrow that memory only within the lifetime guaranteed by the caller.

## Input Ordering

Input ordering is preserved per connection.

Later data from one TCP stream must not overtake earlier data when handed through protocol
processing.

This is required for:

- framing
- handshake progression
- authentication state
- stateful cryptography
- gameplay request ordering

Concurrency comes from different connections and different world partitions, not from arbitrarily
reordering one connection's byte stream.

## Protocol Framing Boundary

Transport provides ordered bytes.

Protocol determines what those bytes mean.

Transport must not contain rules such as:

```text
packet 1004 means chat
0x400 is the maximum header-declared 5517 game packet length
this offset contains a character identifier
```

Those are Protocol concerns.

The exact inbound frame extraction API will be established when the receive pipeline is implemented.

It should:

- avoid unnecessary copies
- preserve buffer ownership
- distinguish incomplete from invalid input
- apply protocol-specific limits in Protocol
- support ordered stateful decoding

See [TQ Framing](../protocol/framing.md).

## Output Model

Each connection has one logical outbound progression.

If the server produces:

```text
Frame A
Frame B
Frame C
```

the wire must preserve:

```text
Frame A
Frame B
Frame C
```

Multiple independent socket writers must not race on the same connection.

Conceptually:

```text
protocol output
      ↓
bounded connection output
      ↓
single send progression
      ↓
socket
```

This simplifies:

- ordering
- partial writes
- shutdown
- encryption state
- failure handling
- backpressure

A single send progression does not require a dedicated thread.

## Stateful Cryptography

Some Conquer protocol paths use connection-specific stateful cryptography.

Transport must preserve the ordered byte progression Protocol requires for that state.

In particular:

```text
input ordering
output ordering
cipher state progression
```

must remain consistent.

Protocol owns the cryptographic transformation.

Transport owns ordered delivery.

## Partial Writes

The transport implementation must correctly handle APIs that make only partial write progress.

Conceptually:

```text
bytes remaining
    ↓
write some bytes
    ↓
advance sent position
    ↓
bytes remaining?
    ├── yes -> continue
    └── no  -> next output
```

Partial transport progress must never reorder or corrupt logical protocol output.

## End of Stream

A zero-byte read from an established TCP stream represents remote closure.

It is not an empty application message.

EOF transitions the connection toward shutdown.

## Bounded Output

A slow client must not be able to create unlimited server memory growth.

Connection output must therefore be bounded.

When capacity is exhausted, the response must be explicit.

Depending on the semantic data, possible policies include:

```text
wait
reject
disconnect
coalesce
replace superseded state
controlled shedding
```

The policy is determined by the owner of the work.

Critical protocol data must not be silently discarded merely to preserve a connection.

## Capacity

Queue capacities are tuning values, not architectural constants.

They should eventually be selected from measured:

- burst size
- production rate
- client drain rate
- latency
- memory cost
- serialization cost
- encryption cost
- overload behavior

A large bounded queue is still a buffer and should not become a hidden substitute for backpressure.

## Slow Clients

Persistent inability to drain output is a transport health problem.

Signals may include:

- sustained queue saturation
- excessive queue age
- write latency
- repeated write timeouts
- inability to make forward progress

The connection may be terminated when it remains unhealthy.

Slow-client transport policy does not belong to authoritative gameplay state.

## Admission Control

Accepting a socket consumes server resources.

Admission controls should therefore run before expensive connection work begins.

Potential policies include:

- global connection limit
- per-IP connection limit
- server shutdown state
- pending authentication load
- abuse controls
- resource pressure

The accept loop must remain small.

It should not perform:

```text
database authentication
expensive handshake work
gameplay initialization
world mutations
```

inline with socket acceptance.

Conceptually:

```text
accept
  ↓
basic socket configuration
  ↓
admission
  ├── reject -> close
  └── accept -> hand off
```

## Listener Ownership

Executable hosts decide which endpoints exist.

For example:

```text
AccountServer
    login endpoint

GameServer
    game endpoint
```

Transport supplies the listener and connection mechanics.

The host supplies:

- endpoint configuration
- protocol integration
- application integration

## Asynchronous I/O

Transport uses asynchronous socket I/O.

The architecture does not allocate one blocked operating-system thread per connection.

Mostly idle clients should therefore remain inexpensive from a scheduler perspective.

Specific .NET primitives should be chosen during implementation using correctness, ownership
clarity, profiling, and benchmarks.

## Buffer Ownership

Transport buffers require explicit owners.

Possible future implementations include:

- `System.IO.Pipelines`
- pooled arrays
- `MemoryPool<byte>`
- other bounded memory abstractions

The architecture does not require one specific mechanism.

The ownership rule does:

```text
Transport owns transport memory.

Protocol may borrow transport memory while its lifetime is guaranteed.

Protocol serializers write into caller-owned memory.

Application and gameplay code do not retain transport-buffer references.
```

Borrowed memory must never outlive the owner's reuse or release boundary.

## Allocation Strategy

Networking and protocol processing are hot paths.

The design favors:

- caller-owned buffers
- spans and sequences
- pooling where ownership is safe
- avoiding unnecessary intermediate copies
- avoiding avoidable per-packet allocations

This is not a zero-allocation mandate.

Correctness and understandable ownership come first.

Optimization should follow profiling.

The current `PacketWriter` already follows this model by borrowing caller-owned memory instead of
allocating or pooling its own buffer.

## Error Boundaries

Different failures belong to different layers.

Examples:

```text
connection reset
    -> Transport

invalid frame
    -> Protocol

packet invalid in current session state
    -> Protocol / host adapter

invalid gameplay request
    -> Application / authoritative world
```

Raw socket exceptions should not leak arbitrarily into gameplay code.

Expected client disconnects should also be distinguished operationally from server defects.

## Timeouts

Timeouts belong to the subsystem that understands the operation being timed.

Examples include:

```text
login handshake timeout
authentication timeout
stalled output timeout
idle-session policy
```

A login-handshake deadline should not become a blind generic read timeout applied to every
established game connection.

## Host Adapter Boundary

Executable hosts connect Transport and Protocol to application behavior.

Inbound game traffic:

```text
Transport bytes
      ↓
Protocol
      ↓
GameServer adapter
      ↓
Application command
```

Outbound game traffic:

```text
world/application result
      ↓
GameServer adapter
      ↓
Protocol
      ↓
Transport
```

This prevents:

- Transport from knowing gameplay
- Protocol from knowing application services
- Application from knowing socket APIs

## Gameplay Isolation

Network sessions are not world-state owners.

A session may contain or reference:

```text
connection identity
account identity
character identity
protocol state
```

but gameplay mutation is routed to the authoritative world owner.

See [World Execution](world-execution.md).

## World-to-Network Flow

The world emits state changes or events rather than writing connections directly.

Conceptually:

```text
WorldPartition
      ↓
world event
      ↓
replication / interest
      ↓
GameServer adapter
      ↓
Protocol encode
      ↓
Transport
      ↓
client
```

This keeps socket lifetime, encryption, packet serialization, buffering, and disconnect handling
outside simulation code.

## End-to-End Boundedness

Boundedness must exist across the complete asynchronous path.

The architecture must eventually account for:

```text
accepted sockets
transport input memory
pending decoded work
application routing
world mailboxes
replication work
connection output
```

Making one queue bounded does not protect the process if another layer can accumulate work without
limit.

Every asynchronous ownership transition must have a finite or explicitly controlled resource policy.

## Observability

Transport should expose operational signals such as:

- accepted connections
- rejected connections
- active connections
- disconnect reasons
- bytes received
- bytes sent
- input pressure
- output queue depth
- output queue age
- read failures
- write failures
- admission failures

High-volume events should favor metrics and structured aggregation over verbose per-packet logging.

## Security Boundary

Network clients are untrusted.

Validation occurs in layers:

```text
untrusted client
      ↓
Transport
resource and byte-stream constraints
      ↓
Protocol
wire structure and compatibility
      ↓
Application / Domain
gameplay semantics and authority
```

Validation in one layer never removes the need for validation in the next.

## Host Shutdown

Server shutdown should stop network ownership in a controlled sequence.

Conceptually:

```text
stop accepting
      ↓
signal active connections
      ↓
stop new output production
      ↓
finish or abort transport work
      ↓
release sockets and buffers
```

The exact graceful-drain policy will be decided when host networking is implemented.

No transport operation should retain resources after the host considers that connection stopped.

## Scaling Model

The initial server remains a modular monolith.

One AccountServer or GameServer process may support many connections using asynchronous I/O.

The architecture does not require:

- one process per connection
- one process per map
- one thread per connection
- a distributed message broker

Those mechanisms should only be introduced if measured deployment requirements justify them.

## Transport Invariants

Future transport implementation must preserve these rules:

```text
1. Transport moves bytes; Protocol interprets them.

2. Every connection has an explicit lifetime owner.

3. Shutdown is monotonic and idempotent.

4. Input order is preserved per connection.

5. Output order is preserved per connection.

6. Each connection has one logical send progression.

7. Transport buffers and queues are bounded.

8. Slow clients cannot cause unlimited memory growth.

9. Borrowed memory cannot outlive its owner.

10. Socket I/O is asynchronous.

11. Partial read/write progress is handled correctly.

12. EOF closes the connection.

13. Protocol-specific limits remain in Protocol.

14. Stateful cipher ordering is preserved.

15. Network sessions do not own gameplay state.

16. World execution does not write sockets directly.
```

These are architectural contracts, not suggestions for one specific implementation.

## Implemented vs Planned

### Implemented

The repository currently establishes:

- the `OpenConquer.Transport` project boundary
- Protocol/Transport separation
- caller-owned protocol frame memory
- allocation-free bounded `PacketWriter`
- common outbound frame encoding
- caller-supplied outbound frame limits

### Planned

Future networking slices still need to implement:

- TCP listener runtime
- accepted connection abstraction
- receive buffering
- inbound frame extraction
- connection lifetime management
- bounded output
- send progression
- admission control
- transport diagnostics
- login networking
- game networking

This document defines the constraints those implementations must satisfy. It does not claim those
systems already exist.

## Related Documentation

- [Architecture Overview](README.md)
- [World Execution](world-execution.md)
- [Protocol Reference](../protocol/README.md)
- [TQ Framing](../protocol/framing.md)
