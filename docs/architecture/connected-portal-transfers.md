# Connected portal transfers (retail 5517)

## Authority and admission

The authenticated world-session reader accepts exactly the fixed 38-byte MsgAction 10010
subtype 85. The low/high 16-bit halves of the packet's parameter pair name a *candidate exit
marker tile*, not the hero's location or a client-chosen destination. A mismatched hero identity,
malformed request, ambiguous marker, unsupported route or unavailable destination is refused.
The client enters its indefinite waiting state after sending action 85: an unsuccessful request
therefore closes the connection, never silently leaves the client waiting.

The shipped catalog contains 322 cross-version historical candidates and no
release-approved routes. Server preflight does not activate those candidates until a
route-specific, reviewed content release promotes them. The trusted map and exit catalog
are frozen at world startup. The coordinator reuses
`WorldPortalPreflight` to reserve one destination slot and establish a FIFO barrier against
source-map commands. The resulting source snapshot must represent the current, alive, non-ghost
authoritative owner adjacent to the marker. The destination must be statically passable. A route's release review includes the
arrival-to-exit loop check; the native client immediately requests another portal if its
arrival is within one tile of an exit. There is no speculative runtime tile relocation or
re-arm packet.
No untrusted packet coordinates, flags or passway indices affect the world or SQL predicates.

## Durable transaction and linearization

The source partition reservation is initially reversible. After the barrier, a scoped location
read must match the session's most recently confirmed persisted position, revision, operation
marker and v13 writer token. The session's **existing** writer token is used; it is not rotated
on every portal. A connected seal then verifies the exact tracked session completion task and
prevents competing removals or normal source-map commands.

Only after sealing does the coordinator send one revision-qualified character-location CAS:
account ID, character ID, previous map/tile, revision, fresh operation ID and writer token all
participate in authorization. It confirms the next revision, operation ID, destination tile
and token through an independent read. A matching coordinate without the operation receipt is
not a commit proof. Cancellation or timeout after dispatch is uncertain, never retryable.

The partition cutover stages the destination occupant, removes the source occupant, and switches
the runtime's logical owner, tracked completion task and session location generation under the
same world-runtime lock. The destination capacity reservation is consumed once and the source
slot refunded. The session updates its map snapshot and resets walk/reconciliation histories
at the committed arrival tile.

If any step after sealing fails before confirmed visibility cutover, partition execution is
stopped. This deliberately favors a controlled realm shutdown over accepting writes under
an uncertain owner; it is not an operation-level retry strategy. A disconnect after a fully
committed cutover releases the destination generation normally. No database lock is held
across the two map partitions or the socket. The writer token fences late MySQL operations,
but it is not a cross-process world lease or a general crash-recovery journal.

## Native response and visibility

The 5517 client invokes `CMsgAction__Process` branch `0x6FF652` for server subtype 86.
Its entity ID must match the hero. Field `+8` is the **map data/terrain identity**, fields
`+0x16/+0x18/+0x1A` carry direction and arrival X/Y. A valid 86 ends waiting
mode `0x3F0` and enters world mode `0x3F2`; it clears the previous map's non-pet roles.
A new 74 would incorrectly return a connected client to login mode.

After the durable and runtime cutover, the session queues action 86 and MapInfo 1110
as **one encrypted FIFO output batch and one flush**. Packet 1110 carries the destination world
MapId, MapDataId and 64-bit flags. Old-map visibility membership is removed and observers
receive native removal 135. New-map membership is installed without dropping the online-name
identity; action 86 and destination-peer spawns use the same last accepted facing direction.
New peer spawns arrive **after** the 86/1110 batch. Already queued old-map packets
precede the batch, not follow it. Slow-client output remains subject to the bounded session
mailbox and normal connection teardown. Client action 408 following certain 1110 flag changes
is an all-zero state-applied notification; it is validated and requires no response.

The packet order is anchored in `20_notes/Server/portal-transition-5517.md`
(retail client facts C, historical server observations S/T, Fox community X, retail-server
unknowns U). This feature intentionally does not assume optional retail 104/1016 messages or
invent refusal, restoration or cross-process network packets.

## Validation and remaining scope

The deterministic regression suite exercises a tracked world owner handed between partitions,
source slot accounting, sealed removal and stale completion refusal, and real encrypted-game
transport ordering for action 86, 1110 and destination spawns. Gameplay tests cover source
observer removal, destination peer membership and chat, a rejected marker and durable revision
advance. Native client testing remains a required deployment gate; CI and emulator observations
cannot prove every retail world rule.

Ordinary walk/jump remains in-memory until a separate durable movement milestone. Revival,
NPC teleports, GM moves, cross-process handoff, live combat restrictions and historically
unresolved passways are not enabled by this path. Gameplay-state policy must become more
specific as authoritative combat and status systems are introduced.
