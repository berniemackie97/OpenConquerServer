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

### GameServer

- Runnable host that verifies both databases, loads map and item content, and starts the world.
- TCP ingress with per-source and global admission, fixed login workers, and a login timeout.
- Diffie-Hellman key exchange, CAST5 encrypted framing, and single-use ticket redemption.
- Existing-character login: character resolution, the initial bootstrap packets, and the full client
  request sequence (map entry, items, friends, weapon skills, magic, syndicate, client reports).
- World runtime: one partition per map, a shared scheduler, bounded command queues, and
  generation-fenced character admission and removal.
- Hand-off from a completed login into a supervised world session with revocable realm presence.
- Partition-owned eight-direction walking state mutation with native 5517 direction ordering,
  composed static collision, generation fencing, and a cancellable-before-acceptance command API.
  This is not connected to gameplay packets and does not provide movement pacing or replication.

### Persistence and content

- Accounts schema version 3 and Game schema version 10, both checked at startup.
- GameServer validates a configured realm ID against its Game database's operator-provisioned binding before world publication.
- Operator-controlled Accounts DB realm registry keyed by the exact native protocol server name,
  with case-sensitive uniqueness, explicit availability, a stable internal realm ID, IPv4 endpoint,
  and read-only runtime lookup. It is not yet used in the login handshake or ticket issuance.
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
  is validated offline and does not enable transfers.

## Not yet implemented

- Realm resolution during AccountServer login, realm-bound ticket issuance/redemption, and Game DB
  binding to a configured realm. The current login destination remains single-server configuration.
- Account registration and staff tooling.
- Hosted character creation: verified walkable spawn, creation credential provisioning, native
  control exchange, authenticated creation processing, and same-session continuation.
- Gameplay packet routing after login: movement request parsing, pacing, acknowledgements,
  replication, combat, NPCs, trading, and chat.
- World simulation: monsters, timers, visibility, and map transfers. Dynamic occupancy and elevation
  constraints for movement also remain unresolved.
- Seven map identities from the original data that could not be resolved (see
  [content](content/README.md)).
- Verification-key rotation tooling and production least-privilege credential provisioning.
