# Networking Architecture

OpenConquer separates network transport from Conquer protocol and server behavior.

```text
Transport
    moves bytes

Protocol
    interprets bytes

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
- transport cancellation;
- transport resource limits.

Transport does not interpret Conquer packet identifiers or gameplay semantics.

See [Protocol Reference](../protocol/README.md).

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

This preserves:

- packet ordering;
- stream-cipher state;
- partial-write handling;
- deterministic shutdown;
- backpressure.

## AccountServer Login Session

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

Disposal terminates the owned connection resources and observes the transport pumps.

## AccountServer Login Workers

`LoginConnectionWorkerPool` consumes `TransportConnectionAdmissionQueue` with fixed concurrency.

Each worker owns one admitted connection, transfers ownership to `LoginConnectionSession`, applies
the whole-connection timeout, and disposes the session before consuming another connection.

Client failures and connection timeouts are reported and isolated to that connection. Caller
cancellation stops the pool. Reporter failures are pool-fatal.

Connection admission remains owned by `OpenConquer.Transport`; AccountServer does not define a
duplicate queue.

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

Inbound and outbound login cipher positions are independent.

## Standard AccountServer Transaction

The standard 5517 AccountServer transaction is implemented:

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

Outcomes are classified as:

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

Current AccountServer mappings:

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

Ticket issuance revalidates the authenticated account and credential revisions transactionally.

A stale authentication snapshot cannot receive a successful handoff.

## Post-Authentication Reports

After successful `1055`, the native client sends:

```text
1100 MAC report
    ↓
1052 res.dat report
```

`LoginPostAuthenticationReportReader` enforces:

- packet ordering;
- expected session UID;
- MAC format;
- exact `res.dat` resource name.

Each report receives its own bounded read timeout.

These reports are telemetry, not authorization.

Failure, timeout, or absence of post-authentication telemetry does not revoke the already-issued
GameServer ticket.

## Login Timeouts

Two independent timeout scopes apply:

| Scope            | Boundary                                                          |
| ---------------- | ----------------------------------------------------------------- |
| Whole connection | Session open through complete `LoginHandshakeProcessor` execution |
| Handshake phase  | Individual `1060`, `1100`, and `1052` reads                       |

Phase deadlines are fresh per expected frame. Authentication and durable ticket persistence are not
bounded by a phase deadline but remain inside the whole-connection deadline.

Caller cancellation remains distinct from timeout expiration.

Both timeout configurations reject values beyond the finite delay supported by
`CancellationTokenSource.CancelAfter`.

## Authentication Protection

Authentication work is bounded independently of transport admission.

Implemented controls include:

- global authentication concurrency;
- per-source request rate;
- per-source concurrency;
- per-account password concurrency;
- account/source failure tracking;
- lockout;
- bounded protection state.

Transport admission does not replace authentication abuse protection.

See [Authentication](authentication.md).

## Game-Login Redemption

Durable GameServer login tickets support atomic single-use redemption.

The persistence boundary:

```text
locks ticket
    ↓
checks database-authoritative expiration
    ↓
verifies AuthenticationKey
    ↓
deletes exact ticket
    ↓
commits
    ↓
returns authorized identity
```

Concurrent successful redemption of the same ticket is impossible.

The GameServer network session that invokes this boundary is not yet implemented.

## Redemption Protection

Game-login redemption has its own bounded attempt protection:

- global concurrency;
- per-source rate and concurrency;
- per-session concurrency;
- per-session failure tracking and lockout;
- bounded tracked state.

This boundary exists in Infrastructure but is not yet wired into a runnable GameServer host.

## Ticket Revocation

Authentication-invalidating account mutations revoke outstanding GameServer login tickets in the
same durable transaction as the security mutation.

Examples that revoke:

```text
password reset
suspension
ban
soft delete
```

Grant and mutation persistence share compatible lock ordering so a stale authentication result
cannot race past a committed invalidating mutation.

See [Authentication](authentication.md).

## Expired-Ticket Cleanup

Bounded expired-ticket cleanup is implemented.

Cleanup:

- uses MySQL time;
- removes a bounded number of rows per invocation;
- applies an expiration grace period;
- does not determine authorization.

Scheduling the cleaner belongs to future host composition.

## Current Host Status

Implemented network-facing AccountServer components:

- TCP transport foundation;
- bounded connection admission;
- input/output pumps;
- login connection session;
- login framing and stream cryptography;
- standard credential decoding;
- authentication orchestration;
- durable `1055` handoff;
- post-authentication telemetry handling;
- per-read handshake deadlines;
- fixed login connection workers;
- whole-connection timeout and failure isolation.

Still not implemented:

- runnable AccountServer composition root;
- AccountServer listener/worker host composition;
- production worker observability wiring;
- GameServer connection/session boundary;
- GameServer DH/CAST5 handshake;
- GameServer `1052` login proof;
- game packet signatures;
- gameplay networking;
- production database identity and verification-key deployment orchestration.

The executable entry points must remain non-public until the required host lifecycle and operational
configuration are complete.

## Related Documentation

- [Architecture Overview](README.md)
- [Authentication](authentication.md)
- [World Execution](world-execution.md)
- [Protocol Reference](../protocol/README.md)
- [TQ Framing](../protocol/framing.md)
