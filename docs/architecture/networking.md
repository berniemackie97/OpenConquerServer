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

This preserves packet ordering, stream-cipher state, partial-write handling, shutdown semantics, and
backpressure.

## AccountServer Admission

AccountServer ingress is explicitly bounded:

```text
kernel backlog
    ↓
TCP accept
    ↓
bounded admission queue
    ↓
fixed worker pool
    ↓
bounded authentication protection
    ↓
database
```

`TransportConnectionAdmissionQueue` owns accepted connections until a worker receives them.

When admission capacity is exhausted:

- the new connection is rejected;
- the rejected connection is disposed before rejection reporting;
- capacity rejection is recorded as a metric;
- no per-rejection warning or error log is emitted.

This prevents reconnect storms from becoming unbounded queued work or attacker-controlled log
amplification.

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

Unexpected background-service failure requests host shutdown.

## Login Runtime Supervision

`LoginRuntimeHostedService` owns:

- the login listener;
- the login runtime cancellation boundary;
- accept-loop lifetime;
- worker-pool lifetime;
- admission completion during shutdown/failure.

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

## GameServer Handoff

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

Authentication work is bounded independently of transport admission.

Implemented controls:

- global authentication concurrency;
- per-source request rate;
- per-source concurrency;
- per-account password concurrency;
- account/source failure tracking;
- lockout;
- bounded protection state.

Transport admission does not replace authentication abuse protection.

See [Authentication](authentication.md).

## Runtime Observability

`LoginRuntimeMetrics` records low-cardinality counters for:

- admission-capacity rejection;
- overload-rejection disposal failure;
- whole-connection timeout;
- connection processing failure.

No account, player, session, endpoint, or IP value is used as a metric dimension.

Logging policy:

| Event                                | Level       |
| ------------------------------------ | ----------- |
| Database readiness succeeded         | Information |
| Login runtime started/stopped        | Information |
| Connection timeout                   | Debug       |
| Connection processing failure        | Error       |
| Rejected-connection disposal failure | Error       |
| Admission capacity rejection         | Metric only |

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

The GameServer network session that invokes this boundary is not yet implemented.

## Redemption Protection

Game-login redemption has independent bounded attempt protection:

- global concurrency;
- per-source rate and concurrency;
- per-session concurrency;
- per-session failure tracking and lockout;
- bounded tracked state.

This boundary exists in Infrastructure but is not yet wired into a runnable GameServer host.

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
- bounded connection admission;
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
- bounded expired-ticket maintenance;
- low-cardinality runtime metrics;
- runnable AccountServer Generic Host composition.

Not yet implemented:

- GameServer connection/session lifecycle;
- GameServer DH/CAST5 handshake;
- GameServer `1052` login proof;
- game packet signatures;
- gameplay networking and authoritative simulation.

## Related Documentation

- [Architecture Overview](README.md)
- [Authentication](authentication.md)
- [World Execution](world-execution.md)
- [Protocol Reference](../protocol/README.md)
- [TQ Framing](../protocol/framing.md)
