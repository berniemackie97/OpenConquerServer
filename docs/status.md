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
  advertise the selected registration's IPv4 address and port.

### GameServer

- Runnable host that verifies both databases, loads map and item content, and starts the world.
- TCP ingress with per-source and global admission, fixed login workers, and a login timeout.
- Diffie-Hellman key exchange, CAST5 encrypted framing, and single-use ticket redemption.
- Existing-character login: character resolution, the initial bootstrap packets, and the full client
  request sequence (map entry, items, friends, weapon skills, magic, syndicate, client reports).
- World runtime: one partition per map, a shared scheduler, bounded command queues, and
  generation-fenced character admission and removal.
- Hand-off from a completed login into a supervised world session with revocable realm presence.
- Native 5517 eight-direction walk packet 10005 parsing, authenticated world-session gating,
  server-clock pacing, replay resistance, partition-owned movement and post-commit self-acknowledgement.
- Partition-owned eight-direction walking state mutation with native 5517 direction ordering,
  composed static collision, generation fencing, and a cancellable-before-acceptance command API.
  This is not connected to gameplay packets and does not provide movement pacing or replication.

### Persistence and content

- Accounts schema version 4 and Game schema version 10, both checked at startup.
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
  is validated offline and does not enable transfers.

## Not yet implemented

- End-to-end multi-realm deployment/operations: additional GameServer processes, independently
  provisioned Game databases, publicly reachable endpoints, lifecycle/health management and
  authoritative world-content readiness for each destination. Packet 1055 selection is implemented;
  actual realm deployment remains explicitly operator-controlled.
- Account registration and staff tooling.
- Hosted character creation: verified walkable spawn, creation credential provisioning, native
  control exchange, authenticated creation processing, and same-session continuation.
- Remaining gameplay routing: native 24-direction movement, jumping, movement replication,
  position persistence/correction, combat, NPCs, trading, and chat.
- World simulation: monsters, timers, visibility, and map transfers. Dynamic occupancy and elevation
  constraints for movement also remain unresolved.
- Seven map identities from the original data that could not be resolved (see
  [content](content/README.md)).
- Verification-key rotation tooling and production least-privilege credential provisioning.
