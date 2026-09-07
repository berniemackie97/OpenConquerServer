# Networking Architecture

OpenConquer separates network transport mechanics from Conquer Online protocol semantics.

The boundary is simple:

```text
OpenConquer.Transport
    moves bytes

OpenConquer.Protocol
    interprets bytes
```

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
- scalability

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

The detailed wire contract belongs to [OpenConquer.Protocol](../protocol/README.md).

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

## Input Model

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

Transport therefore treats inbound data as an ordered byte stream rather than treating socket reads
as application messages.

The Protocol framing boundary now accepts:

```text
ReadOnlySequence<byte>
```

so buffered transport integration can expose segmented memory without coalescing it merely for
framing. The login path already uses pipelines: `LoginFrameReader` incrementally decrypts into
bounded owned frame memory, then passes the complete frame to `WireFrameDecoder`.

Conceptually:

```text
socket reads
    ↓
transport-owned input buffer
    ↓
WireFrameDecoder
    ├── IncompleteHeader / IncompleteFrame -> retain bytes
    ├── invalid frame                      -> protocol/session failure policy
    └── Success                            -> process borrowed frame
                                             then advance buffer
```

`WireFrameDecoder` does not own or advance transport memory.

Transport owns the buffered memory and controls its lifetime. Protocol may borrow that memory only
within the lifetime guaranteed by the caller.

This keeps:

```text
buffer ownership and advancement
    -> Transport

TQ header interpretation and frame validity
    -> Protocol

disconnect/session reaction to invalid protocol input
    -> host/session integration
```

separate.

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

## Implemented Login Integration

`LoginConnectionSession` owns a connection, input/output pipes, a login cipher with independent
directional positions, both transport pumps, and lifetime cancellation. Opening sends the encrypted
1059 seed. Opening failure disposes transferred resources; disposal cancels I/O and observes both
pumps. Input buffering pauses at the 524-byte login limit; output backpressure waits for
consumption.

`LoginFrameReader` handles fragmented and coalesced encrypted input, validates the frame limit, and
transfers disposable plaintext frames. It rejects overlapping reads and becomes terminal after a
failure that may have advanced cipher state. `LoginFrameWriter` similarly serializes, encrypts, and
flushes one frame at a time, rejecting reuse after a potentially partial write.

`LoginAccountRequestReader` decodes standard 1060 and transfers disposable password ownership.
`LoginPostAuthenticationReportReader` checks 1100 then AccountServer 1052, correlates both session
UIDs, and validates MAC text and `res.dat`. These reports are untrusted telemetry, not
authorization.

Account authentication and the durable game-login authorization boundary are also implemented below
the host-composition layer. Successful authentication can issue a five-minute game-login ticket
after transactional account and credential-state revalidation. Ticket bearer secrets are protected
at rest with keyed verifiers rather than persisted directly.

Game-login redemption persistence provides atomic single-use consumption of those durable grants.
Redemption locks the ticket row, rejects missing, expired, invalid, or unverifiable credentials
without consuming the grant, and deletes a successfully verified ticket before committing the
authorized identity. Concurrent valid redemption attempts therefore cannot authorize the same ticket
more than once.

These persistence boundaries are not yet integrated into executable network hosts. The current
`AccountServer` and `GameServer` entry points do not compose the existing transport, protocol,
authentication, ticket-issuance, or ticket-redemption components into running servers.

The following host-facing and operational boundaries remain unimplemented:

- production authentication attempt-limiter implementation;
- pre-password-verification request/IP admission and global expensive-authentication concurrency
  control;
- authentication and game-login phase deadlines;
- worker ownership and host lifecycle orchestration;
- authentication-response orchestration;
- native 1055 AccountServer game-login handoff integration;
- native 1052 GameServer login-proof integration;
- production game-login redemption attempt limiting;
- bounded expired-ticket cleanup;
- transactional ticket revocation from future authentication-invalidating account mutations;
- production AccountServer/GameServer database identities and verifier-key deployment/rotation
  orchestration.

A bounded transport queue alone does not replace host-level authentication admission, brute-force
protection, request deadlines, or expensive-work concurrency controls. The executable hosts must
remain unexposed until those security and operational boundaries are complete.

The broader baseline constraints are recorded in the
[server baseline audit](../audits/server-baseline.md).

## Related Documentation

- [Architecture Overview](README.md)
- [Authentication](authentication.md)
- [World Execution](world-execution.md)
- [Protocol Reference](../protocol/README.md)
- [TQ Framing](../protocol/framing.md)
