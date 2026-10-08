# Architecture

OpenConquer Server is a modular monolith built around explicit ownership, bounded work, strict
dependency direction, and server-authoritative state.

## Solution Structure

```mermaid
flowchart TD
    Domain["OpenConquer.Domain"]
    Application["OpenConquer.Application"]
    Infrastructure["OpenConquer.Infrastructure"]

    Protocol["OpenConquer.Protocol"]
    Transport["OpenConquer.Transport"]
    Assets["OpenConquer.Assets"]

    AccountServer["OpenConquer.AccountServer"]
    GameServer["OpenConquer.GameServer"]

    Application --> Domain

    Infrastructure --> Application
    Infrastructure --> Domain

    AccountServer --> Application
    AccountServer --> Infrastructure
    AccountServer --> Protocol
    AccountServer --> Transport

    GameServer --> Application
    GameServer --> Infrastructure
    GameServer --> Protocol
    GameServer --> Transport
```

## Projects

| Project                      | Responsibility                                                                                                                                 |
| ---------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| `OpenConquer.Domain`         | Domain rules, state, value objects, and invariants.                                                                                            |
| `OpenConquer.Application`    | Use cases, orchestration, authorization, and persistence contracts.                                                                            |
| `OpenConquer.Infrastructure` | MySQL persistence, EF Core mappings, password storage, security infrastructure, and external adapters.                                         |
| `OpenConquer.Protocol`       | Packet layouts, framing, serialization, text encoding, protocol cryptography, and client compatibility.                                        |
| `OpenConquer.Transport`      | TCP, connections, buffering, asynchronous I/O, admission, backpressure, and connection lifetime.                                               |
| `OpenConquer.Assets`         | Native client-derived static asset formats and parsers used by offline ingestion/tooling.                                                       |
| `OpenConquer.AccountServer`  | Runnable 5517 account-login host and authentication-handshake orchestration.                                                                   |
| `OpenConquer.GameServer`     | GameServer connection handoff, existing-character login/bootstrap orchestration, native compatibility behavior, and future gameplay hosting.   |
| `OpenConquer.GameData.Tool`  | Offline deterministic conversion of verified native client data into canonical server release content.                                        |

## Dependency Rules

Core direction:

```text
Domain
  ↑
Application
  ↑
Infrastructure
```

Additional rules:

```text
Domain
    does not depend on Application, Infrastructure, Protocol, Transport, or hosts

Application
    depends on Domain
    does not depend on Infrastructure

Protocol
    does not depend on Transport, Application, gameplay, persistence, or hosts

Transport
    does not depend on Protocol, Application, gameplay, or persistence

Hosts
    compose concrete implementations and protocol/transport adapters

Assets
    owns native client-derived static data decoding for offline ingestion/tooling
    does not own mutable live-world state
    is not a runtime GameServer dependency
```

A new assembly requires a real dependency, ownership, deployment, provider, or reuse boundary.

## Protocol and Transport

```text
Protocol
    interprets and produces Conquer wire data

Transport
    moves bytes and owns network resources
```

Runtime flow:

```mermaid
flowchart LR
    Client["Client"]
    Transport["Transport"]
    Protocol["Protocol"]
    Adapter["Server Adapter"]
    Application["Application"]
    Runtime["Authoritative Runtime"]

    Client <--> Transport
    Transport <--> Protocol
    Protocol <--> Adapter
    Adapter <--> Application
    Application <--> Runtime
```

Transport must not know packet semantics.

Protocol must not own sockets, connection lifetime, transport queues, or backpressure.

See [Networking Architecture](networking.md).

## AccountServer

The standard 5517 AccountServer transaction is implemented:

```text
TCP connection
    ↓
per-source connection admission
    ↓
bounded global admission
    ↓
LoginConnectionSession
    ↓
1059 seed
    ↓
1060 credentials
    ↓
AccountAuthenticator
    ↓
durable GameLoginTicket grant
    ↓
1055 handoff
    ↓
1100 MAC report
    ↓
1052 res.dat report
```

Ownership is split by boundary:

| Boundary                         | Owner                            |
| -------------------------------- | -------------------------------- |
| TCP and generic admission        | Transport                        |
| Per-source admission policy      | Infrastructure                   |
| Login runtime orchestration      | AccountServer                    |
| 5517 wire behavior               | Protocol / AccountServer adapter |
| Authentication and ticket rules  | Application                      |
| Persistence and abuse protection | Infrastructure                   |

### Host Lifecycle

The AccountServer is a runnable Generic Host.

Startup is ordered:

```text
load and validate configuration
    ↓
compose services
    ↓
verify account database/schema readiness
    ↓
start expired-ticket maintenance
    ↓
create TCP listener
    ↓
start bounded login runtime
```

The TCP listener is created only after database readiness succeeds.

Login ingress is bounded by:

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

Capacity exhaustion rejects connections instead of growing unbounded work.

Fatal listener or worker failure faults the supervised login runtime and triggers graceful host
shutdown. Fatal background-service failure results in a nonzero process exit code.

Shutdown stops login ingress before maintenance and disposes queued connections through explicit
ownership.

Expired-ticket cleanup is bounded maintenance. It does not determine ticket authorization.

See:

- [Networking Architecture](networking.md)
- [Authentication](authentication.md)

## GameServer Connection and Existing-Character Handoff

The GameServer connection handoff is implemented through authenticated application-session ownership
over the native 5517 encrypted compatibility channel.

```text
accepted transport
    ↓
GameConnectionSession
    ↓
native Diffie-Hellman exchange
    ↓
CAST5 encrypted framing
    ↓
1052 login proof
    ↓
GameLoginTicketRedeemer
    ↓
AuthenticatedGameConnection
    ↓
CharacterLoginHandoffProcessor
    ↓
CharacterCreation | ExistingCharacter
```

The connection session owns transport pumps, pipelines, handshake state, encrypted framing, cipher
state, cancellation, and disposal.

The transport byte stream remains continuous across the handshake-to-encrypted transition so
fragmented and coalesced input is handled without discarding already-buffered data.

The native GameServer channel preserves stock 5517 compatibility. It is not modern authenticated
transport and does not authenticate server endpoint identity.

Application identity is established separately through successful single-use ticket redemption. The
live session transfers to `AuthenticatedGameConnection` only after authorization succeeds.

After authentication, `CharacterLoginHandoffProcessor` resolves the canonical account ID through the
application character-login boundary. An account without a persisted character routes to character
creation. An account with a persisted character receives a validated `CharacterLoginProfile` and
continues through the ownership-safe existing-character bootstrap path.

The implemented existing-character progression is:

```text
CharacterLoginHandoffResult
    ↓
ExistingCharacterBootstrapProcessor
    ↓
bootstrap packet sequence
    ↓
AwaitingEnterMapConnection
    ↓
client MsgAction 0x4A EnterMap
    ↓
ExistingCharacterEnterMapProcessor
    ↓
map metadata + weather + 0x4A acknowledgement
    ↓
EnteredMapConnection
    ↓
client MsgAction 0x198 client-state-applied
    ↓
ExistingCharacterMapStateAppliedProcessor
    ↓
AwaitingItemSetConnection
    ↓
client MsgAction 0x4B GetItemSet
    ↓
bounded CharacterItemSet persistence hydration
    ↓
CharacterItemSetResolver
    ↓
canonical item catalog resolution
    ↓
wire projection
    ↓
MsgItem 1008 snapshots
    ↓
optional MsgTick 1009 subtype-46 main-equipment snapshot
    ↓
MsgAction 0x4B acknowledgement
    ↓
AwaitingFriendListConnection
    ↓
client MsgAction 0x4C GetGoodFriend
    ↓
bounded social-relation persistence hydration
    ↓
MsgFriend 1019 snapshots
    ↓
MsgAction 0x4C acknowledgement
    ↓
AwaitingWeaponSkillSetConnection
    ↓
client MsgAction 0x4D GetWeaponSkillSet
    ↓
bounded weapon-skill persistence hydration
    ↓
MsgWeaponSkill 1025 snapshots
    ↓
MsgAction 0x4D acknowledgement
    ↓
AwaitingMagicSetConnection
    ↓
client MsgAction 0x4E GetMagicSet
    ↓
bounded magic persistence hydration
    ↓
MsgMagicEffectSimple 1103 snapshots
    ↓
optional MsgFlushExp 1104 magic-experience snapshots
    ↓
MsgAction 0x4E acknowledgement
    ↓
AwaitingSyndicateAttributesConnection
```

Every transition takes exclusive ownership of the prior state and transfers the same authenticated
connection exactly once only after its protocol boundary succeeds.

Invalid packets, identity mismatches, persistence failures, projection failures, cancellation, and
write failures close the owned connection rather than exposing a partially advanced session.

The `0x4B` request is authorized against the trusted character identity already carried in
`CharacterLoginProfile`. Client-supplied identity never selects the persistence lookup key.

The item-set response is fully hydrated and projected before the first response packet is written.
The response order is deterministic:

```text
1008 item snapshots in ascending ItemId order
    ↓
optional 1009 subtype-46 active main-equipment snapshot
    ↓
0x4B acknowledgement
```

`CharacterItemSetRepository` performs bounded persistence reconstruction without owning static-content
policy. It uses the resolver-supplied UTC instant to exclude structurally valid, already-expired
active-lifetime rows in SQL before applying the hydration cap. The database cutoff is rounded down
to `datetime(6)` precision, never forward. Malformed lifetime payloads remain eligible for bounded
hydration and fail closed. `CharacterItemSetResolver` combines the resulting persisted state with
the immutable canonical `ItemTypeCatalog` and the same UTC authority before GameServer projection.
Resolution fails closed for unknown item types, incompatible static-lifetime state, and persisted
stack quantities above the item type effective stack capacity. Native `itemtype.dat` decoding remains
behind the offline asset/tooling boundary and is not a GameServer runtime dependency.

Already-expired active-lifetime items are excluded before bounded database hydration, with defensive
expiration filtering retained in Application before catalog lookup and wire projection.
Pending-activation items remain pending and are not activated by login resolution. `ExistingCharacterItemSetWireProjection` owns only native wire placement, equipment
snapshot construction, and wire-lifetime range conversion.

The existing-character bootstrap continues through bounded social-relation, weapon-skill, and magic
hydration. Each client request is validated against the authenticated character identity before
persistence is queried. Social relations are projected to native packet 1019 snapshots, weapon
skills to packet 1025 snapshots, and magic state to packet 1103 plus packet 1104 when persisted
magic experience is nonzero. Each rung completes with the matching MsgAction acknowledgement before
connection ownership advances.

The resulting `AwaitingSyndicateAttributesConnection` carries the validated item, social-relation,
weapon-skill, and magic runtime state into the next native bootstrap rung. The protocol foundation
for syndicate attributes exists, but syndicate persistence, hydration, and runtime bootstrap
processing are not yet implemented.

The same authenticated connection remains continuously owned through character-login resolution,
bootstrap, map entry, map-state application, item resolution, social hydration, weapon-skill
hydration, and magic hydration. Cancellation or failure closes the owned connection instead of
exposing partial state.

Secured GameServer output permits one active frame writer per connection. Overlapping writes are
rejected immediately rather than queued.

The runnable GameServer host, connection admission runtime, character creation transaction,
syndicate-attributes and later bootstrap rungs, gameplay routing, and authoritative world integration
remain future boundaries.

See:

- [Networking Architecture](networking.md)
- [Authentication](authentication.md)

## Authoritative World

Clients submit requests; they do not directly mutate gameplay state.

Planned world execution:

```mermaid
flowchart LR
    Packet["Client Request"]
    Adapter["GameServer Adapter"]
    Command["Command"]
    Router["World Router"]
    Mailbox["Bounded Partition Mailbox"]
    Executor["Partition Executor"]
    State["Authoritative State"]

    Packet --> Adapter
    Adapter --> Command
    Command --> Router
    Router --> Mailbox
    Mailbox --> Executor
    Executor <--> State
```

A world partition has one mutation owner at a time.

Different partitions may execute concurrently. The same partition may not execute concurrent
mutation turns.

See [World Execution](world-execution.md).

## Persistence

The database is durable storage, not live gameplay state.

```text
database
    ↕
Infrastructure
    ↕
Application
    ↕
authoritative runtime
```

Accounts and Game persistence are separate durable boundaries.

Accounts persistence owns account identity, credentials, security state, and game-login tickets.

Game persistence currently owns durable character-login, item, social-relation, weapon-skill, and
magic state. The current Game schema provides:

- one persisted character per account;
- unique character names;
- player entity IDs beginning at `1,000,000`;
- appearance and hair state;
- level, experience, profession, and rebirth state;
- attributes, current life, and current mana;
- silver, Conquer Points, and bound Conquer Points;
- PK points, title, and enlightenment points;
- persisted map ID and position;
- character-owned inventory and equipment items;
- main and alternate equipment placement;
- item durability and verified native compatibility fields;
- item lock/unlock state;
- stack quantity;
- permanent, pending-activation, and active-expiry item lifetime state;
- directed friend/enemy social relationships;
- persisted weapon-skill level and experience;
- persisted magic level and experience.

The Game database does not establish a cross-database foreign key to Accounts. Successful single-use
ticket redemption establishes the trusted authenticated account identity used for character
resolution.

Character-login, item-set, social-relation, weapon-skill, and magic reads use bounded, no-tracking
`DbContext` operations and do not expose persistence records to the GameServer boundary.

Character item-set persistence hydration has an explicit operational maximum independent of gameplay
inventory capacity. Persistence corruption and impossible aggregate state fail closed at the
infrastructure boundary. Catalog-dependent item invariants are resolved separately in Application
against the canonical `ItemTypeCatalog`; they are not duplicated as database constraints.

Long-running `DbContext` instances do not own active world state.

Persistence latency must not hold authoritative world execution open.

## Resource Ownership

| Resource                | Owner                                                                  |
| ----------------------- | ---------------------------------------------------------------------- |
| TCP listener            | Host runtime                                                           |
| Accepted connection     | Transport / admission / connection session according to transfer state |
| Transport buffers       | Transport                                                              |
| Login session           | AccountServer connection scope                                         |
| Game connection session | GameServer connection scope                                            |
| Protocol frame memory   | Owning caller/session boundary                                         |
| `DbContext`             | Bounded infrastructure operation                                       |
| Durable transaction     | Infrastructure operation                                               |
| Mutable world partition | Partition executor                                                     |

Borrowed memory must not outlive its owner.

Ownership transfers must be explicit, including failure paths.

## Bounded Work

Potentially unbounded producer/consumer boundaries require explicit capacity and overload behavior.

Examples:

- accepted connections;
- authentication work;
- transport I/O;
- maintenance batches;
- gameplay outbound work;
- world partition commands;
- scheduled world work;
- persistence work;
- replication work.

Allowed overload strategies depend on semantics:

```text
backpressure
rejection
disconnect
coalescing
replacement
controlled shedding
```

Unbounded accumulation is not an acceptable default.

## Failure Boundaries

Failures must not expose misleading partial state.

Current examples:

```text
PacketReader failure
    -> cursor remains valid

PacketWriter validation/capacity failure
    -> committed position unchanged

WireFrameEncoder failure
    -> attempted frame region cleared

game-login ticket grant
    -> success returned only after durable commit

game-login ticket redemption
    -> authorization succeeds only after durable single-use consumption

account security mutation
    -> required ticket revocation commits transactionally

AccountServer database readiness failure
    -> listener is never exposed

AccountServer runtime failure
    -> sibling runtime work is stopped and host shutdown is requested

AccountServer fatal background-service failure
    -> host shuts down and returns a nonzero process exit code

GameServer connection handoff failure
    -> owned session is closed instead of exposing partial authentication state

GameServer character-login resolution failure
    -> owned authenticated connection is closed instead of exposing partial character state

GameServer existing-character world-entry failure
    -> owned authenticated connection is closed instead of exposing a partially advanced bootstrap state

GameServer item-set resolution/projection failure
    -> no item response is written and the owned authenticated connection is closed

GameServer overlapping secured write
    -> rejected immediately instead of queued
```

Ambiguous durable commit outcomes are not blindly retried.

## Time

Use the correct authority for each responsibility:

```text
simulation duration / scheduling
    -> monotonic time

durable ticket issue / expiration
    -> MySQL wall clock

maintenance cadence
    -> injected process time

persisted item expiration / login item resolution and wire projection
    -> explicit UTC wall-clock authority

calendar or persisted wall-clock events
    -> explicit wall-clock authority
```

Process time does not decide durable game-login ticket validity.

Item-set login processing captures one UTC instant and passes it through bounded persistence
hydration, resolution, and projection so active item lifetimes are evaluated against one coherent
wall-clock authority.

## Scaling Model

Initial deployment is a modular monolith.

The AccountServer contains no irreplaceable live player state and is designed to remain restartable.

Future GameServer processes may host multiple independently owned world partitions:

```text
GameServer
├── Partition A
├── Partition B
├── Partition C
└── Partition D
```

Distributed services or message brokers should be introduced only when measured deployment or
capacity requirements justify them.

## Current Runtime Status

Implemented:

- shared framing and serialization;
- TCP transport foundation;
- bounded global connection admission;
- per-source AccountServer pre-authentication admission;
- AccountServer login session and stream cryptography;
- standard 5517 AccountServer authentication transaction;
- fixed login worker pool and whole-connection timeout;
- bounded authentication abuse protection;
- durable GameServer login-ticket grant/redemption;
- transactional ticket revocation;
- database-readiness startup gating;
- delayed AccountServer listener creation;
- supervised AccountServer runtime lifecycle;
- fatal AccountServer background-service exit propagation;
- login runtime metrics;
- bounded expired-ticket maintenance;
- runnable AccountServer composition root;
- strict AccountServer deployment configuration;
- GameServer connection/session ownership;
- native GameServer Diffie-Hellman exchange and CAST5 compatibility encryption;
- GameServer encrypted framing;
- single-owner GameServer secured outbound writes;
- GameServer `1052` login-proof authentication;
- authenticated GameServer connection handoff;
- Game character, item, social-relation, weapon-skill, and magic persistence with schema-readiness verification;
- persisted character-login profile resolution;
- authenticated account routing to character creation or existing-character login;
- ownership-safe post-authentication character-login handoff;
- existing-character bootstrap packet sequence;
- native `0x4A` EnterMap processing;
- native `0x198` client-state-applied transition;
- bounded character item-set persistence hydration and catalog-aware runtime resolution;
- native `0x4B` GetItemSet validation and response progression;
- native 1008 local item snapshots;
- native 1009 subtype-46 active main-equipment snapshot;
- canonical item-catalog-backed runtime item resolution;
- native `0x4C` friend-list bootstrap with bounded social-relation hydration and packet 1019 projection;
- native `0x4D` weapon-skill bootstrap with bounded hydration and packet 1025 projection;
- native `0x4E` magic bootstrap with bounded hydration and packet 1103/1104 projection;
- ownership-safe handoff to the syndicate-attributes bootstrap stage;
- MySQL account persistence;
- MySQL Game character, item, social-relation, weapon-skill, and magic persistence.

Not yet implemented:

- account registration;
- runnable GameServer Generic Host;
- GameServer listener, admission queue, and worker runtime;
- character creation request processing and durable creation;
- syndicate-attributes and later native login bootstrap rungs;
- gameplay outbound scheduling and bounded mailbox policy;
- authoritative world simulation;
- gameplay networking and simulation.

## Documentation

- [Networking Architecture](networking.md)
- [Authentication](authentication.md)
- [World Execution](world-execution.md)
- [Protocol Reference](../protocol/README.md)
- [TQ Framing](../protocol/framing.md)
- [TQ Text Encoding](../protocol/encoding.md)

Architecture documents describe ownership and runtime boundaries.

Protocol documents describe client-visible 5517 wire behavior.
