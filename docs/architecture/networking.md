# Networking

Both servers share the same connection pipeline:

```text
TCP accept → per-source admission → global admission queue → fixed worker pool → connection session
```

`OpenConquer.Transport` owns sockets, pipelines, buffering, and backpressure. It treats TCP as a
byte stream: a receive can contain part of a frame, one frame, or several, and frame boundaries
are found by the protocol layer. `OpenConquer.Protocol` decodes from caller-owned
`ReadOnlySequence<byte>` input and never advances transport buffers itself. Wire formats are in the
[protocol documentation](../protocol/README.md).

## Admission

Each server limits connections in two stages before any login work starts.

1. **Per source.** Immediately after accept, the connection takes a lease against its source
   address. The lease travels with the connection through the queue and the worker and is
   released when the connection is disposed, so queued and active connections both count toward
   the limit. IPv4 addresses are tracked individually, IPv4-mapped IPv6 addresses count as their
   IPv4 address, and other IPv6 addresses are grouped by `/64`.
2. **Global.** `TransportConnectionAdmissionQueue` holds admitted connections until a worker takes
   them. When it is full, the new connection is rejected.

Rejected connections are disposed before the listener accepts another. Rejections are counted in
metrics, never logged individually, and never tagged with an address, so a flood of connections
cannot amplify logging or create unbounded metric series.

Admission protects runtime capacity. It is separate from the authentication rate limits described
in [Authentication](authentication.md); each guards a different resource.

## Workers and timeouts

A fixed pool of workers drains the admission queue. There is no task per connection. Each worker
opens a session for the connection, runs the login sequence, disposes the session, and takes the
next connection. A whole-connection timeout bounds every login. The AccountServer also applies a
fresh per-read timeout to each expected client packet; caller cancellation and timeout expiry are
reported separately. A failure or timeout affects only that connection, while a failure in the
worker pool itself is fatal to the runtime.

## Runtime supervision

A hosted service owns the listener, the accept loop, and the worker pool as one failure domain. If
the accept loop or the pool stops unexpectedly, even without an exception, the service completes
admission, cancels the sibling, waits for it, closes the listener, and faults. The host then shuts
down and exits with a nonzero code. Shutdown runs in reverse startup order, so ingress stops
before maintenance or the world.

## Output ordering

Each connection has a single ordered send path. Frames are written one at a time, which preserves
packet order and stream-cipher state. On the GameServer, a write that overlaps an active write is
rejected immediately instead of queued, so a slow client cannot accumulate waiting writers.
Scheduling and coalescing gameplay output belong to the future gameplay runtime, not to the frame
writer.

## AccountServer

Startup runs in order: validate configuration, compose services, verify the Accounts database,
start ticket cleanup, then create the listener and start the workers. The listener does not exist
until the database check passes.

`LoginConnectionSession` owns one connection's pipelines, stream cipher state, frame reader and
writer, and transport pumps. Opening a session sends the login seed. The frame reader handles
fragmented and coalesced input, decrypts incrementally, enforces the 524-byte login frame limit,
allows one read at a time, and becomes unusable after any failure that may have advanced the
cipher. The writer has its own cipher state and becomes unusable after a partial write.

`LoginHandshakeProcessor` then runs the login sequence:

```text
seed (1059) → credentials (1060) → authenticate → grant ticket → response (1055)
            → MAC address report (1100) → resource version report (1052)
```

The success response is sent only after the GameServer ticket has been committed. The two reports
after it are telemetry: their order, session ID, MAC format, and resource name are validated, but
a missing or malformed report does not revoke the ticket.

| Condition                                        | Failure code |
| ------------------------------------------------ | -----------: |
| Wrong credentials                                |          `1` |
| Protected or mobile login request (unsupported)  |          `1` |
| Account state changed before the ticket grant    |          `1` |
| Selected realm unknown, malformed or disabled    |         `10` |
| Banned account                                   |         `12` |
| Malformed credential request or unknown packet   |         `57` |
| Selected realm in maintenance                    |         `70` |

A failure response carries no GameServer endpoint.

Transport, database, and application exceptions close the connection; they are never turned into
a failure response.

| Metric (`OpenConquer.AccountServer.Login`)                           | Meaning                          |
| -------------------------------------------------------------------- | -------------------------------- |
| `openconquer.account_server.login.admission.source_rejections`      | Per-source limit reached          |
| `openconquer.account_server.login.admission.capacity_rejections`    | Admission queue full              |
| `openconquer.account_server.login.admission.rejection_disposal_failures` | Rejected connection failed to close |
| `openconquer.account_server.login.connection.timeouts`              | Whole-connection timeout          |
| `openconquer.account_server.login.connection.failures`              | Processing or cleanup failure     |

## GameServer

The GameServer opens its listener only after both databases, the world content, and the item
catalog have been verified, and only when `GameServer:Network:Enabled` is true. Per-source
occupancy covers the connection from accept until it is fully disposed, including while it is
queued, logging in, and resident in the world.

A login worker takes each connection through these stages:

```text
GameConnectionSession          key exchange, then encrypted framing on the same stream
GameConnectionAuthenticator    login proof (1052) and single-use ticket redemption
CharacterLoginHandoffProcessor character lookup by the authenticated account
ExistingCharacterBootstrapProcessor and the Login/Bootstrap stages
WorldSessionAdmissionProcessor admission into the world runtime
```

`GameConnectionSession` keeps one input pipeline across the key exchange and the switch to
encryption, so encrypted bytes that arrive in the same read as the key-exchange response are not
lost. The encryption is what the client supports; it does not authenticate the server or protect
message integrity. Identity comes from redeeming the ticket, and the raw authentication key is not
kept after redemption.

Character lookup uses only the account ID established by the ticket; nothing the client sends
selects which character is loaded. An account without a character is routed to character
creation, which is not implemented yet. An existing character goes through the bootstrap
sequence described in the [protocol documentation](../protocol/README.md#gameserver-login).
`GameExistingCharacterLoginProcessor` owns the connection for the whole sequence and keeps what each
step loads in a single login context. Each step validates the client's request against the
authenticated character, writes its response, and records its state. Any failure closes the
connection.

When the sequence completes, the worker hands the connection to a `WorldSession` and becomes free
immediately. The world session's I/O and cleanup are supervised separately from the workers.
Gameplay packets are not handled yet, so the session closes on the first unsupported frame without
sending a response.

A source's admission slot is released only after its connection has been fully disposed. If a
connection, including a rejected one, cannot be closed, the slot stays occupied, the failure is
recorded as fatal, and the host shuts down rather than keep running with sockets it cannot close.
Session cleanup failures are handled the same way.

| Metric (`OpenConquer.GameServer.Login`)                           | Meaning                         |
| ----------------------------------------------------------------- | ------------------------------- |
| `openconquer.game_server.login.admission.accepted`               | Connection admitted              |
| `openconquer.game_server.login.admission.source_rejections`      | Per-source limit reached         |
| `openconquer.game_server.login.admission.capacity_rejections`    | Admission queue full             |
| `openconquer.game_server.login.connection.completions`           | Login reached the world          |
| `openconquer.game_server.login.connection.not_admitted`          | Login ended without reaching the world (disconnect, rejected ticket, no character) |
| `openconquer.game_server.login.connection.failures`              | Login failed                     |
| `openconquer.game_server.login.connection.timeouts`              | Login exceeded its timeout       |
| `openconquer.game_server.login.connection.duration`              | Login duration in milliseconds   |
| `openconquer.game_server.world_sessions.active`                  | World sessions currently open    |
