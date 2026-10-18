# Implementation status

This is the single source for what is and is not implemented. Other documents describe how the
implemented parts work and link here rather than repeating this list.

## Implemented

### AccountServer

- Runnable host with validated configuration, database readiness gating, and supervised shutdown.
- Per-source and global connection admission, a fixed worker pool, and a whole-connection timeout.
- The standard account login sequence: seed, credential request, authentication, durable GameServer
  ticket grant, response, and the two post-login client reports.
- Authentication rate limiting and lockout, password storage with transparent migration, and account
  security mutations that revoke outstanding tickets.
- Scheduled cleanup of expired tickets and low-cardinality login metrics.
- Exact native 1060 protocol-name validation and live read-only realm lookup before authentication;
  unknown or unavailable selections cannot receive a ticket. After credential verification, a second
  lookup checks continued availability and immutable realm identity; successful native 1055 redirects
  advertise the selected registration's IPv4 address and port. Refused selections reply with the
  native "server down" (10) and "server locked" (70) codes; the AccountServer has no static
  GameServer endpoint.
- Realm load at login: a realm whose GameServer is not reporting is refused with 10, one reporting
  busy with 20 and one reporting full with 21, before and again after credential verification.
- One realm at a time: a login for an account still playing in another realm is refused with 11 and
  that realm is asked to disconnect it.

### GameServer

- Runnable host that verifies both databases, loads map and item content, and starts the world.
- TCP ingress with per-source and global admission, fixed login workers, and a login timeout.
- Diffie-Hellman key exchange, CAST5 encrypted framing, and single-use ticket redemption.
- Existing-character login: character resolution, the initial bootstrap packets, and the full client
  request sequence (map entry, items, friends, weapon skills, magic, syndicate, client reports).
- World runtime: one partition per map, a shared scheduler, bounded command queues, and
  generation-fenced character admission and removal.
- Hand-off from a completed login into a supervised world session with revocable realm presence.
- Account session records in the Accounts database, so an account plays in one realm at a time:
  logins are refused while another realm holds the account, kick requests from other realms
  disconnect the session with the native kick notice (1115), and a realm's records are cleared when
  its GameServer starts and stops.
- Native 5517 10005 eight- and 24-direction movement parsing, authenticated world-session gating,
  server-clock pacing, replay resistance, partition-owned movement and post-commit self-acknowledgement.
  Selector-9 tables are verified, but live selector-9 movement is denied until server-owned riding/vigor
  authority exists. Server-owned dead (5) and ghost (10) primary-status bits distinguish
  immobile corpses from walking ghosts. Client action 145 is handled after the
  corpse delay; 10017 attributes 25/11 and 10014 spawn reflect ghost state.
  The native 1056/2 death-time trade quit is accepted as an idempotent no-op
  until trade state exists; corpses cannot spoof facing or standing pose.
  See [authoritative movement eligibility](architecture/authoritative-movement-eligibility.md).
- Native 5517 10010 subtype 137 absolute-target ground jump (1–16 rounded tiles), enforced
  by authoritative map partition, map jump-prohibition flags, composed landing collision,
  shared jump/walk pacing, post-commit self-echo and bounded peer replication; mounted-specific
  caps and animation/elevation status remain unimplemented (see [native jumping](architecture/native-jumping.md)).
- Authenticated 10010 position reports (subtypes 100/146), bounded authorized-tile history,
  and conservative native 108 correction after repeated divergence; no report is echoed.
- Partition-owned native eight- and 24-direction walking state mutation with native 5517 direction ordering,
  composed static collision, generation fencing, and a cancellable-before-acceptance command API.
  Native walking ingress is connected; nearby player visibility, peer-spawn, walking relay and
  disconnect removal are now wired through bounded per-session delivery.
- Native 5517 player turning (action 79) and enumerated pose/emote selection (action 81),
  authenticated, echoed and relayed to visible peers, with current state in future player spawns.
- Native 5517 in-world 1004 local speech, directed same-realm character whispers, and
  transient 64-byte role words, authenticated with bounded delivery and late-observer
  synchronization (see [native chat](architecture/native-chat.md)).

### Persistence and content

- Accounts schema version 6 and Game schema version 10, both checked at startup.
- Realm status reporting: each network-enabled GameServer reports its load (normal, busy at a
  saturated map partition, full at the character limit) and online count to the Accounts database
  on a fixed interval, and withdraws the report on shutdown. While full or busy it refuses logins
  with the native login-channel talk.
- GameServer validates a configured realm ID against its Game database's operator-provisioned binding before world publication.
- Operator-controlled Accounts DB realm registry keyed by the exact native protocol server name,
  with case-sensitive uniqueness, explicit availability, a stable internal realm ID, IPv4 endpoint,
  and read-only runtime lookup. The login handshake uses it for fail-closed selection preflight;
  newly issued tickets are realm-bound and redemption enforces GameServer realm identity.
- Characters, items (with lifetimes), friends and enemies, weapon skills, magic, and syndicates.
- Item type catalog (14,981 types), 282 map definitions, 185 base terrains, and static collision for
  every terrain, all validated before the GameServer starts.
- Native 5517 character-creation request parsing and validation.
- Independently testable character-creation persistence with atomic insertion, database-enforced
  uniqueness, duplicate-request resolution, and dedicated least-privilege write credentials.
- Character-creation application service with authoritative account identity, server-validated
  selections, immutable starting-state resolution, and persistence-result handling.
- Optional strict starting-state configuration with required profession-family validation and
  startup map/coordinate bounds verification before world publication.
- Evidence-qualified map-transition candidate catalog with 373 native exit identities; the catalog
  is loaded and validated against the exact canonical world snapshot before GameServer world
  publication, and is unavailable once the world stops. It does not enable transfers.
- Immutable historical revival-destination resolver: 207 Long-lineage reborn/portal records
  are checked against loaded 5517 map identities and composed terrain, with explicit unsupported
  results instead of assumed spawn coordinates. See
  [revival destination authority](architecture/revival-destination-authority.md).
  This does not enable action 94, revival, or map transfer.
- Opt-in durable character-location compare-and-swap writer and identity-bound location
  observer sharing one dedicated, column-restricted database identity. Observations expose
  only persisted coordinates, not proof of a prior uncertain commit; no world transfer or
  live position-save pipeline is enabled. See
  [durable character location](architecture/durable-character-location.md).
- Native action-85 portal candidate preflight: validated DMap exit-marker and transition
  evidence resolve only against the current world's immutable map-content snapshot, followed
  by a reversible generation-fenced reservation. No live packet ingress or transfer is enabled.
- Internal persisted-source portal preparation pairs a trusted login identity and
  generation-fenced reversible destination reservation with the latest identity-scoped
  persisted-location observation; mismatches, cancellation and failures refund capacity.
  This is not a durable CAS commit, residency transfer, or live packet handling.
- Reversible map-transfer preflight: the world runtime reserves destination capacity,
  fences new source positional/avatar mutations and drains a source-map command
  barrier. No residency handoff, persistence, visibility change or client packets
  are activated. See [transfer preflight](architecture/world-transfer-preflight.md).

## Not yet implemented

- Automated realm provisioning: realms are added and changed by an operator following
  [Realm operations](operations/realms.md).
- Account registration and staff tooling.
- Hosted character creation: verified walkable spawn, creation credential provisioning, native
  control exchange, authenticated creation processing, and same-session continuation.
- Remaining gameplay routing: mounted-status authority, mounted-jump caps, non-player replication,
  position persistence, combat, NPCs, complete trading, nonlocal and group chat channels.
  Native position correction already handles refused predicted movement and bounded drift reports.
- World simulation: monsters, timers, NPC/monster/ground-item visibility, and map transfers.
  Jump-specific elevation and status effects remain unresolved; do not introduce a speculative
  dynamic player-occupancy collision gate for normal walking.
- Seven map identities from the original data that could not be resolved (see
  [content](content/README.md)).
- Verification-key rotation tooling and production least-privilege credential provisioning.
