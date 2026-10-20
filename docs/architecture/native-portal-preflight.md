# Portal preflight

## Scope

`WorldPortalPreflight` is a trusted, server-side preflight check for the candidate exit tile that
the retail 5517 client sends in MsgAction subtype 85. It uses the reversible reservation API from
[World transfer preflight](world-transfer-preflight.md). It only checks and reserves: no network
handler calls it, and it does not change world residency, save a location to the database, change
the session's map, publish visibility, acknowledge a map transition or start a revival.
**Portals do not work yet.** This resolver does not replace the map-transfer cutover that is still
needed.

## How a candidate is resolved

In the 5517 `Conquer.exe`, `CHero_TickPortalDetectionAndMapChangeRequest` at `0x65D9B2` samples
nearby DMap exit marker tiles and sends subtype 85 with the candidate marker X/Y packed in the
parameter pair. The client does not send a passway index. This is recorded in
`20_notes/Server/map-transitions.md` (client analysis of `0x65D9B2`, `0x637700` and `0x63777D`).
The server does not trust the hero coordinates or facing in the packet as the character's
location.

`WorldPortalPreflight.TryReserveAsync` is internal and must only be called with the session's own
authenticated world ownership token. It:

1. Requires the transition catalog to have been validated against the same
   `WorldMapContentCatalog` instance that was used to build the runtime. Equal map IDs, equal
   passway indices or an equivalent file loaded separately are not enough, because a mismatched
   catalog could allow a stale route. The check compares object references, not contents, and
   refuses on any mismatch.
2. Looks up the source map by the server-side `ownership.MapId`.
3. Matches the candidate tile against the map's normalized DMap exits. It refuses if no marker, or
   more than one marker, matches the tile.
4. Resolves the `(mapId, passwayIndex)` pair, which the server derived itself, through the
   immutable `MapTransitionCatalog`, where each entry records its resolution status and source. It
   refuses target coordinates that are unresolved or cannot be represented, and never falls back
   to a guessed default.
5. Uses the transfer preflight to reserve destination capacity and take a source snapshot behind a
   FIFO barrier. Older pending movement completes before the snapshot.
6. Confirms that the source snapshot shows the character alive and within one tile of the
   candidate marker on each axis. If the source is stale or has moved away, the reservation is
   refunded. The returned lease is temporary and tied to the ownership generation, and the caller
   must dispose it on every exit path.

The catalog's historical destinations come from other versions, so they are not complete proof of
what the 5517 server allowed. Whether a player may use a portal also depends on gameplay state the
server does not model yet. Only confirmed restrictions are enforced here. A transition cannot be
called fully authorized until the cutover state machine checks every condition that applies.

## Why the preflight stops here

`WorldSession` exposes a `Map` and `Ownership` that stay fixed for its lifetime, and
`WorldVisibilityService` keys membership by that map. The compare-and-swap (CAS) in
[Durable character location](durable-character-location.md) matches the last saved location, not
the latest in-memory snapshot of a moving character. Changing the map in memory before those parts
are redesigned would leave the systems disagreeing with each other. The preflight therefore stops
before any change that cannot be undone.

The cutover still has to handle the durable CAS and reconciliation of writes with unknown outcomes,
ownership handoff between partitions, session changes and revocation, position history reset,
departure and arrival visibility, and the action-86 and map-entry packet order confirmed against
the 5517 client. No distributed transaction spans MySQL, world partitions and TCP.

## Persisted-location preparation

`WorldPortalPersistedLocationPreflight.TryReserveAsync` combines this reversible portal reservation
with `ICharacterLocationObservationStore`, which reads the saved location of one account's
character. It takes the trusted, authenticated login profile and the world ownership token. It
rejects a character or login-map mismatch before it takes a reservation or queries the database,
then resolves the marker and sets up the barrier as above. Only after changes on the source are
blocked does it read the saved `(map_id, x, y)` for the profile's account and character. Missing
data, a changed saved map, a revoked generation or a canceled read stops the preparation and
refunds destination capacity. Read errors are rethrown after the refund. The wait is limited to 10
seconds, so even a store that ignores cancellation cannot hold the capacity reservation forever.

The result pairs the saved source location and its monotonic revision (an immutable snapshot) with
the revocable reservation and its separate live source snapshot. Saved coordinates can differ from
in-memory coordinates because normal movement is not saved to the database. The result is
`IDisposable`, and callers must release it in `finally` or `using`, including on shutdown. No
service or client input calls this internal preparation yet.

Reading the location does not authorize a commit. The observation is not a transfer receipt, a
monotonic revision or a permanent ownership lease, and it does not prove that an outstanding CAS
can no longer commit. This step adds no database write, residency handoff, network
acknowledgement, client visibility change or position-save pipeline. Before 5517 map transitions
are enabled, the future coordinator must serialize durable writes across generations on its own,
resolve writes with unknown outcomes, and change ownership, session and visibility together in one
atomic step.

## Testing

The tests check that a catalog built from a different world snapshot is rejected and that a live
reservation takes up destination capacity. They also cover marker matching, unresolved routes,
requests outside the one-tile radius, duplicate markers on one tile, stale ownership, a dead source
character, destination reservation accounting, and movement queued ahead of the barrier. There is
no schema migration, runtime configuration switch or new protocol layout. Nothing in production
calls the resolver from the network yet.
