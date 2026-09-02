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
- ordered perconnection input and output
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

so future buffered transport integration can expose segmented memory without coalescing it merely
for framing.

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

## Related Documentation

- [Architecture Overview](README.md)
- [World Execution](world-execution.md)
- [Protocol Reference](../protocol/README.md)
- [TQ Framing](../protocol/framing.md)
