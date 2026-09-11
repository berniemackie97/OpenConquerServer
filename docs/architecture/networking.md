# Networking Architecture

OpenConquer separates transport, protocol, server-session behavior, and application rules.

```text
Transport
    moves bytes and owns network resources

Protocol
    interprets Conquer wire data

Server adapter
    owns connection/session behavior

Application
    owns use cases and authorization
```

## Transport Ownership

`OpenConquer.Transport` owns:

- TCP listeners and accepted connections;
- asynchronous receive/send operations;
- connection lifetime;
- input/output buffering;
- bounded admission;
- backpressure;
- cancellation;
- transport resource limits.

Transport does not interpret Conquer packet identifiers or gameplay semantics.

## Runtime Flow

```text
client
    ↓
TCP
    ↓
Transport
    ↓
Protocol
    ↓
server session / adapter
    ↓
Application
    ↓
authoritative runtime
```

Outbound traffic follows the reverse path.

## Stream Model

TCP is an ordered byte stream.

A receive operation may contain:

```text
partial frame
one frame
multiple frames
```

Transport therefore does not treat socket receives as packet boundaries.

Protocol framing accepts segmented `ReadOnlySequence<byte>` input where appropriate.

Protocol does not own or advance transport buffers.

## Output Ordering

Each connection has one logical outbound progression.

```text
Frame A
Frame B
Frame C
    ↓
single ordered send progression
    ↓
TCP
```

Independent writers must not race on one connection.

Secured GameServer output permits exactly one active frame write per connection. An overlapping
write is rejected immediately rather than queued, so the connection layer cannot accumulate an
unbounded set of waiting writers.

Higher-level gameplay output scheduling, including any bounded mailbox, priority, coalescing, or
drop policy, belongs to the future gameplay runtime rather than the secured frame writer.

This preserves packet ordering, stream-cipher state, partial-write handling, shutdown semantics,
backpressure, and bounded connection-level resource usage.

## AccountServer Admission

AccountServer ingress is explicitly bounded before authentication begins:

```text
kernel backlog
    ↓
TCP accept
    ↓
per-source connection admission
    ↓
bounded global admission queue
    ↓
fixed worker pool
    ↓
bounded authentication protection
    ↓
database
```

Per-source connection admission executes immediately after TCP accept and before the connection can
consume global admission-queue or worker capacity.

Source identity is normalized consistently across connection admission and authentication
protection:

- IPv4 addresses are tracked individually;
- IPv4-mapped IPv6 addresses share the corresponding IPv4 source;
- native IPv6 addresses are grouped by `/64`.

An admitted connection carries its per-source admission lease through the global queue and login
worker. The lease is released when the connection is disposed, so queued and actively processed
connections both count toward the source concurrency limit.

When the per-source connection limit is exhausted:

- the new connection is rejected before entering the global admission queue;
- the rejected connection is disposed before the listener accepts another connection;
- source rejection is recorded as a metric;
- no source address or endpoint is attached as a metric dimension.

`TransportConnectionAdmissionQueue` owns globally admitted connections until a worker receives them.

When global admission capacity is exhausted:

- the new connection is rejected;
- the rejected connection is disposed before rejection reporting;
- capacity rejection is recorded independently from source rejection;
- no per-rejection warning or error log is emitted.

Connection-level admission protects pre-authentication runtime capacity. It does not replace the
separate authentication request, concurrency, failure, and lockout controls applied once a login
request arrives.

This prevents slow pre-authentication peers and reconnect storms from becoming unbounded queued
work, worker exhaustion, or attacker-controlled log amplification.

## AccountServer Host Lifecycle

The AccountServer runs as a Generic Host with sequential hosted-service startup.

Startup order:

```text
load and validate configuration
    ↓
compose dependencies
    ↓
verify account database/schema readiness
    ↓
start expired-ticket maintenance
    ↓
construct TCP listener
    ↓
start accept loop and fixed workers
```

The TCP listener is not constructed until database readiness succeeds.

A failed readiness check therefore fails startup without exposing the public login socket.

Hosted-service shutdown occurs in reverse order, so the login runtime stops before maintenance.

Host startup and shutdown are bounded by explicit timeouts.

Unexpected background-service failure requests graceful host shutdown.

Fatal background-service failures are recorded before they escape their service. After the host
finishes shutting down, the AccountServer returns a nonzero process exit code when such a failure
was recorded. Normal host shutdown returns success.

Startup or shutdown failures that escape the host lifecycle continue to propagate normally.

## Login Runtime Supervision

`LoginRuntimeHostedService` owns:

- the login listener;
- the login runtime cancellation boundary;
- accept-loop lifetime;
- worker-pool lifetime;
- admission completion during shutdown/failure.

The AccountServer-specific admission listener decorates the raw TCP listener before the generic
transport accept loop begins. The generic accept loop therefore receives only connections that have
already passed per-source admission.

The accept loop and worker pool form one failure domain.

```text
accept loop fails
    ↓
complete admission
    ↓
cancel worker pool
    ↓
observe sibling task
    ↓
dispose listener
    ↓
fault runtime

worker pool fails
    ↓
complete admission
    ↓
cancel accept loop
    ↓
observe sibling task
    ↓
dispose listener
    ↓
fault runtime
```

A child task that terminates unexpectedly is fatal even if it returns successfully.

Fatal runtime failure does not leave the TCP listener bound while the host is shutting down.

## Login Session

`LoginConnectionSession` owns one opened AccountServer login connection.

It owns:

- the transferred transport connection;
- input and output pipelines;
- login stream cipher state;
- frame reader and writer;
- transport pumps;
- connection-scope cancellation.

Opening the session sends encrypted packet `1059`.

Input uses the AccountServer 524-byte complete-frame limit.

Disposal terminates owned connection resources and observes transport pumps.

## Login Workers

`LoginConnectionWorkerPool` consumes `TransportConnectionAdmissionQueue` with fixed concurrency.

Each worker:

```text
receive admitted connection
    ↓
open LoginConnectionSession
    ↓
run LoginHandshakeProcessor
    ↓
dispose session
    ↓
receive next connection
```

A whole-connection timeout bounds each admitted login.

Client processing failures and timeouts are isolated to that connection.

Reporter failures are worker-pool fatal.

There is no task-per-connection worker model and no duplicate AccountServer-specific admission
queue.

## Login Framing

`LoginFrameReader`:

- handles fragmented and coalesced TCP input;
- decrypts incrementally;
- validates the AccountServer frame limit;
- returns owned plaintext frames;
- permits only one active read;
- becomes terminal when a failure may have advanced cipher state.

`LoginFrameWriter`:

- serializes one frame at a time;
- encrypts using independent outbound cipher state;
- preserves ordered writes;
- becomes unusable after a potentially partial failed write.

Inbound and outbound cipher positions are independent.

## Standard 5517 AccountServer Transaction

```text
connection opened
    ↓
1059 seed
    ↓
1060 credentials
    ↓
AccountAuthenticator
    ↓
durable GameLoginTicket grant
    ↓
1055 authentication result
    ↓
1100 MAC report
    ↓
1052 res.dat report
```

`LoginHandshakeProcessor` owns this orchestration for an already-open `LoginConnectionSession`.

A successful `1055` is not written until durable ticket persistence succeeds.

## Credential Request

`LoginAccountRequestReader` accepts standard packet `1060`.

Outcomes:

```text
Success
EndOfStream
UnexpectedPacket
InvalidAccountRequest
```

Decoded password memory is mutable and disposable.

Only the standard 5517 credential format is implemented.

Known protected/mobile variants are recognized but fail closed rather than entering standard
decoding.

## Authentication Failure Mapping

| Condition                                        | Native failure |
| ------------------------------------------------ | -------------: |
| Invalid credentials                              |            `1` |
| Unsupported protected/mobile login               |            `1` |
| Authentication state changed before ticket grant |            `1` |
| Banned account                                   |           `12` |
| Malformed standard credential request            |           `57` |
| Unknown login packet                             |           `57` |

Transport, database, and application exceptions are not converted into fake authentication
responses.

## AccountServer Game Handoff

Successful authentication is followed by durable ticket issuance.

The AccountServer sends `1055` only after the ticket grant commits.

The success response contains:

```text
SessionUid
AuthenticationKey
GameServerPort
AuthenticationKey as AdditionalSessionField
GameServer IPv4 address
```

Ticket issuance transactionally revalidates the authenticated account and credential revisions.

A stale authentication snapshot cannot receive a successful handoff.

## Post-Authentication Reports

After successful `1055`, the native client sends:

```text
1100 MAC report
    ↓
1052 res.dat report
```

`LoginPostAuthenticationReportReader` enforces packet ordering, expected session UID, MAC format,
and the exact `res.dat` resource name.

Each report receives its own bounded read timeout.

These reports are telemetry, not authorization.

Failure, timeout, or absence of this telemetry does not revoke the already-issued GameServer ticket.

## Login Timeouts

Two timeout scopes apply:

| Scope            | Boundary                                           |
| ---------------- | -------------------------------------------------- |
| Whole connection | Session open through complete handshake processing |
| Handshake phase  | Individual `1060`, `1100`, and `1052` reads        |

Phase deadlines are fresh per expected frame.

Authentication and durable ticket persistence remain inside the whole-connection deadline but are
not limited by a phase deadline.

Caller cancellation remains distinct from timeout expiration.

## Authentication Protection

Authentication work is bounded independently of connection admission.

Implemented controls:

- global authentication concurrency;
- per-source request rate;
- per-source concurrency;
- per-account password concurrency;
- account/source failure tracking;
- lockout;
- bounded protection state.

Pre-authentication connection admission and authentication protection defend different resource
boundaries. Neither replaces the other.

See [Authentication](authentication.md).

## Runtime Observability

`LoginRuntimeMetrics` records low-cardinality counters for:

- per-source connection admission rejection;
- global admission-capacity rejection;
- rejected-connection disposal failure;
- whole-connection timeout;
- connection processing failure.

Source rejection and global capacity rejection remain distinct so pre-authentication source pressure
can be distinguished from exhaustion of total AccountServer admission capacity.

No account, player, session, endpoint, or IP value is used as a metric dimension.

Logging policy:

| Event                                | Level       |
| ------------------------------------ | ----------- |
| Database readiness succeeded         | Information |
| Login runtime started/stopped        | Information |
| Connection timeout                   | Debug       |
| Connection processing failure        | Error       |
| Rejected-connection disposal failure | Error       |
| Per-source admission rejection       | Metric only |
| Global admission capacity rejection  | Metric only |

## GameServer Connection Handoff

`GameConnectionHandoffProcessor` owns the transition from an accepted transport connection to an
authenticated secured GameServer connection.

```text
accepted transport
    ↓
GameConnectionSession
    ↓
Diffie-Hellman handshake
    ↓
secured framing
    ↓
1052 login proof
    ↓
single-use ticket redemption
    ↓
AuthenticatedGameConnection
```

`GameConnectionSession` owns the transferred connection, transport pumps, pipelines, handshake
transition, secured framing, and connection-scope lifetime.

The same input pipeline is preserved across the handshake transition. Fragmented key exchange is
reassembled, while secured bytes coalesced with the key-exchange response remain available for the
first secured frame.

Successful authentication transfers the live secured session to `AuthenticatedGameConnection`.
Expected rejection and failed handoff paths dispose the owned session.

The raw `AuthenticationKey` is used for ticket redemption and is not retained by the authenticated
connection.

A runnable GameServer listener, admission/worker runtime, gameplay routing, and authoritative world
integration remain future boundaries.

## Game-Login Redemption

Durable GameServer login tickets support atomic single-use redemption.

```text
lock ticket
    ↓
check database-authoritative expiration
    ↓
verify AuthenticationKey
    ↓
delete exact ticket
    ↓
commit
    ↓
return authorized identity
```

Concurrent successful redemption of the same ticket is impossible.

GameServer authentication invokes this boundary after validating the first secured `1052` login
proof and supplies the remote IP address for redemption protection.

## Redemption Protection

Game-login redemption has independent bounded attempt protection:

- global concurrency;
- per-source rate and concurrency;
- per-session concurrency;
- per-session failure tracking and lockout;
- bounded tracked state.

The GameServer connection handoff uses this protection boundary. Runnable GameServer ingress and
worker composition are not yet implemented.

## Ticket Revocation

Authentication-invalidating account mutations revoke outstanding GameServer login tickets in the
same transaction as the security mutation.

Examples:

```text
password reset
suspension
ban
soft delete
```

Grant and mutation persistence share compatible lock ordering so a stale authentication result
cannot race past a committed invalidating mutation.

## Expired-Ticket Cleanup

Expired-ticket cleanup runs as bounded AccountServer maintenance.

Each maintenance run:

- uses database-authoritative expiration;
- deletes bounded batches;
- has a bounded maximum number of batches;
- stops early when a partial batch is returned.

Transient cleanup failures are logged and retried on the next scheduled run.

An impossible cleaner result is treated as an invariant failure and faults the background service.

Cleanup does not participate in authorization correctness.

## Current Status

Implemented AccountServer network/runtime components:

- TCP transport;
- per-source pre-authentication connection admission;
- bounded global connection admission;
- lease-coupled admission lifetime;
- login pipelines and connection session;
- login framing and stream cryptography;
- standard credential decoding;
- authentication orchestration;
- durable `1055` handoff;
- post-authentication telemetry;
- handshake and whole-connection timeouts;
- fixed login worker pool;
- database-readiness startup gate;
- delayed listener construction;
- runtime supervision;
- fatal background-service failure exit propagation;
- bounded expired-ticket maintenance;
- low-cardinality runtime metrics;
- runnable AccountServer Generic Host composition.

Implemented GameServer connection-handoff components:

- accepted-connection session ownership;
- native Diffie-Hellman handshake;
- secured CAST5 framing;
- single-owner secured outbound frame writes;
- immediate rejection of overlapping secured writes;
- fragmented and coalesced stream handling;
- first secured `1052` login-proof validation;
- protected single-use ticket redemption;
- authenticated live-session handoff;
- coordinated cancellation and disposal.

Not yet implemented:

- runnable GameServer Generic Host;
- GameServer listener, admission queue, and worker runtime;
- gameplay outbound scheduling and bounded mailbox policy;
- character bootstrap;
- gameplay packet routing;
- authoritative world simulation and replication.

## Related Documentation

- [Architecture Overview](README.md)
- [Authentication](authentication.md)
- [World Execution](world-execution.md)
- [Protocol Reference](../protocol/README.md)
- [TQ Framing](../protocol/framing.md)
