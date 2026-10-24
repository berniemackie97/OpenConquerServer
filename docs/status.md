# Implementation status

This page is the one place that lists what is and is not implemented. Other documents explain how
the implemented parts work and link here instead of repeating the list.

## Implemented

### AccountServer

- A runnable host. It validates its configuration, waits until the database is ready, and has a
  supervised shutdown.
- Connection limits per source address and in total, a fixed worker pool, and a timeout that covers
  the whole connection.
- The standard account login sequence: seed, credential request, authentication, a GameServer
  ticket stored in the database, the response, and the two client reports sent after login.
- Rate limiting and lockout for authentication. Password storage that migrates older hashes to the
  current format transparently. Account security changes revoke any outstanding tickets.
- Scheduled cleanup of expired tickets, and low-cardinality login metrics.
- Before authentication, the server name from 5517 packet 1060 is validated and looked up in the
  realm registry with a live, read-only query. It must match a registered name exactly. An unknown
  or unavailable realm never gets a ticket. After the password is verified, a second lookup checks
  that the realm is still available and that its identity has not changed. A successful redirect
  in 5517 packet 1055 carries the IPv4 address and port registered for the selected realm. A
  refused selection gets the 5517 "server down" (10) or "server locked" (70) code. The
  AccountServer has no fixed GameServer endpoint of its own.
- Realm load is checked at login, both before and after the password is verified. A realm whose
  GameServer is not reporting is refused with code 10, a busy one with 20 and a full one with 21.
- An account plays in one realm at a time. A login for an account that is still playing in another
  realm is refused with code 11, and that realm is asked to disconnect it.
- Opt-in in-client account registration (OpenConquer packets `0xF001`/`0xF002`) through a dedicated
  database identity, with per-source quotas and bounded password hashing. See
  [account registration](architecture/account-registration.md).

### GameServer

- A runnable host that checks both databases, loads map and item content, and starts the world.
- A TCP listener with connection limits per source address and in total, a fixed set of login
  workers, and a login timeout.
- Diffie-Hellman key exchange, CAST5 encrypted framing, and single-use ticket redemption.
- Login with an existing character. The server resolves the character, sends the initial bootstrap
  packets, and answers the full client request sequence (map entry, items, friends, weapon skills,
  magic, syndicate, client reports).
- The world runtime: one partition per map, a shared scheduler, and size-limited command queues.
  Characters are admitted and removed with a generation number, so a request that belongs to an
  older admission is rejected.
- A completed login is handed off to a supervised world session. The session's presence in the
  realm can be revoked.
- Account session records in the Accounts database keep each account in one realm at a time.
  Logins are refused while another realm holds the account. A kick request from another realm
  disconnects the session with the 5517 kick notice (1115). A realm's records are cleared when its
  GameServer starts and again when it stops.
- Movement (5517 packet 10005) in eight and 24 directions. Only an authenticated world session can
  move, pacing follows the server clock, and replayed packets are rejected. The map partition that
  owns the character applies the move and then acknowledges it to the client. The selector-9
  direction tables match the client, but live selector-9 movement is refused until the server owns
  riding and vigor state. The server owns the dead (5) and ghost (10) primary-status bits, so a
  corpse cannot move and a ghost can walk. Client action 145 is handled after the corpse delay, and
  10017 attributes 25/11 and the 10014 spawn show the ghost state. The 5517 trade quit that the
  client sends at death (1056/2) is accepted and does nothing until trade state exists. A corpse
  cannot fake its facing or a standing pose. See
  [movement eligibility](architecture/authoritative-movement-eligibility.md).
- Ground jumps (5517 packet 10010 subtype 137) to an absolute target 1 to 16 tiles away, measured
  as a rounded distance. The map partition checks the map's no-jump flag and the collision of the
  composed landing tile. Jumps share pacing with walking. A committed jump is echoed to the player
  and relayed to nearby players through size-limited queues. Jump limits for mounted characters
  and jump animation/elevation status are not implemented yet (see
  [ground jumping](architecture/native-jumping.md)).
- Position reports (10010 subtypes 100 and 146) from authenticated sessions. Each session keeps
  the last 64 tiles the server accepted. After three reports in a row name a tile outside that
  history, the server sends a 108 position correction, at most once per second. Reports are never
  echoed.
- Walking in eight and 24 directions, applied by the map partition that owns the character.
  Directions use the 5517 order, and each step is checked against the composed static collision
  and the admission generation. A walk command can be cancelled until the partition accepts it.
  Walking packets from the client are connected. Nearby players see each other: peer spawns,
  walking relay and removal on disconnect all go through a size-limited delivery queue for each
  session.
- Player turning (5517 action 79) and pose/emote selection from the fixed list of poses (action
  81). Both are authenticated, echoed to the player and relayed to visible players. Later player
  spawns include the current facing and pose.
- In-world chat over 5517 packet 1004: local speech, whispers to a named character in the same
  realm, and temporary role words of up to 64 bytes. Chat needs an authenticated session and uses
  size-limited delivery. A player who arrives later is sent the current role words (see
  [chat](architecture/native-chat.md)).

### Persistence and content

- Accounts schema version 6 and Game schema version 13, both checked at startup.
- Realm status reporting. Each GameServer with networking enabled writes its load and online count
  to the Accounts database on a fixed interval, and withdraws the report on shutdown. The load is
  normal, busy (a map partition is saturated) or full (the character limit is reached). While full
  or busy, the GameServer refuses logins with a 5517 login-channel talk message.
- Before it publishes the world, the GameServer checks its configured realm ID against the realm
  binding that the operator stored in its Game database.
- A realm registry in the Accounts database, managed by the operator. Each realm is keyed by its
  5517 protocol server name. Names are case-sensitive and must be unique. A realm also has an
  availability setting, a stable internal realm ID and an IPv4 endpoint. The servers only read the
  registry at runtime. The login handshake checks the selected realm against the registry first and
  refuses anything it cannot match. New tickets are bound to a realm, and a GameServer redeems only
  tickets for its own realm.
- Characters, items (with lifetimes), friends and enemies, weapon skills, magic, and syndicates.
- Item type catalog (14,981 types), 282 map definitions, 185 base terrains, and static collision for
  every terrain, all validated before the GameServer starts.
- Parsing and validation of the 5517 character-creation request.
- Character-creation persistence that can be tested on its own. Inserts are atomic, the database
  enforces uniqueness, a duplicate request is resolved against the stored account and name, and
  writes use their own least-privilege database credentials.
- A character-creation application service. The account comes from the authenticated session, the
  server validates the selections, the starting state is resolved into an immutable value, and
  every persistence result is handled.
- An optional starting-state configuration that rejects unknown settings. Every profession family
  must be present, and the starting map and coordinates are checked to be in bounds at startup,
  before the world is published.
- A catalog of candidate map transitions covering 373 exit identities from the 5517 data, each
  tied to its source. It is loaded and validated against the same world snapshot object that the
  runtime uses, before the GameServer publishes the world, and it is unavailable once the world
  stops. Connected action-85 travel uses the 322 resolved, source-tagged destinations. The 51
  unresolved exits, and invalid or ambiguous markers, always fail closed.
- An immutable resolver for historical revival destinations. It checks 207 reborn/portal records
  from the Long lineage against the loaded 5517 map identities and composed terrain. A record that
  cannot be resolved returns an "unsupported" result instead of assumed spawn
  coordinates. See [revival destinations](architecture/revival-destination-authority.md). This
  does not enable action 94, revival, or map transfer.
- An opt-in compare-and-swap writer for a character's saved location, and an observer that reads
  the saved location for one account and character. Both share one dedicated database identity
  that can reach only the location columns. Each write checks the location revision, which stops a
  stale write from succeeding after the coordinates have changed and changed back (ABA). A
  successful write advances the revision by one and stores the operation ID of the winning write
  with it. An observation that matches confirms that a specific operation completed. A missing or
  different operation ID cannot settle a write that is still in flight. The connected portal
  coordinator now uses this writer; ordinary movement remains in-memory only. See
  [durable character location](architecture/durable-character-location.md).
- Preflight for 5517 action-85 portal requests. Validated DMap exit markers and transition data are
  resolved only against the current world's immutable map-content snapshot. A match then takes a
  reversible destination reservation tied to the admission generation. The authenticated action-85
  handler now uses it for connected portal travel.
- Internal portal preparation that uses the saved source location. It pairs the trusted login
  identity and the reversible destination reservation with the latest saved location, read for
  that account and character only. A mismatch, cancellation or failure returns the reserved
  capacity. This step does not commit the location with compare-and-swap, move the character
  between partitions or handle live packets.
- An internal portal transaction for characters without a session. It runs the authenticated
  5517 exit preflight and reads the saved source location. It then takes a write seal that cannot
  be released, makes one compare-and-swap write that carries the revision and an operation ID,
  confirms that write by reading it back, and hands the character to the destination partition
  under its admission generation. Before writing, it rotates a per-character writer generation in
  MySQL. The rotation waits for earlier compare-and-swap writes, and every write through the
  dedicated identity must match the current generation. Any failure after the seal stops the
  world. This does not yet give the realm exclusive ownership of the character, and it does not
  transfer a live session. See [world transfer preflight](architecture/world-transfer-preflight.md).
- Internal relocation between map partitions for characters without an active session. Departure
  from the source map is checked against the admission generation. The destination is staged first
  with the character's status kept, capacity is accounted for, and a failed relocation is rolled
  back. If the cutover fails and cannot be repaired, the world shuts down. Client packets are not
  connected, nothing is saved, and no 5517 packets are sent; see
  [world transfer preflight](architecture/world-transfer-preflight.md).
- Reversible map-transfer preflight. The world runtime reserves capacity on the destination map,
  blocks new position and avatar changes on the source map, and waits until a barrier has drained
  the source map's pending commands. It does not move the character, save anything, change
  visibility or send client packets. See
  [transfer preflight](architecture/world-transfer-preflight.md).

## Not yet implemented

- Automated realm provisioning. An operator adds and changes realms by following
  [Realm operations](operations/realms.md).
- Staff tooling.
- Character creation in the running GameServer: a spawn point confirmed to be walkable,
  provisioning of the creation credentials, the 5517 control-message exchange, creation for the
  authenticated session, and continuing into the world on the same connection.
- Remaining gameplay: server-owned mounted status, jump limits for mounted characters, replication
  of non-player entities, saving positions, combat, NPCs, complete trading, and nonlocal and group
  chat channels. The 5517 position correction already handles refused predicted movement and drift
  reports within its limited history.
- World simulation: monsters, timers, visibility of NPCs, monsters and ground items, and
  non-portal map transfers (revival, NPC and administrative teleports). Elevation and
  status effects specific to jumping are still open. Normal walking has
  no dynamic player-occupancy collision check, and adding one would be speculative.
- Seven map identities from the original data that could not be resolved (see
  [content](content/README.md)).
- Tooling to rotate verification keys, and provisioning of least-privilege credentials for
  production.
