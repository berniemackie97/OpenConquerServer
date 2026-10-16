# Native 5517 player visibility — Slice #145

This slice connects existing authenticated world sessions, eight-direction authoritative ground
walking and generation-fenced residency to native peer presentation. Source evidence:
`20_notes/Server/entity-visibility.md` (client xrefs 0x61ED75, 0x755F64, 0x70051A, 0x7407B4
and 0x740730) and `20_notes/Server/movement.md`; the separate `OpenConquerPublic` reference is
used to cross-check field offsets, **not** as a substitute for the native client trace.

## Visibility contract

A player sees peers on the same world map when `abs(dx) <= 18 && abs(dy) <= 18`.
This is Chebyshev distance and is not the rounded Euclidean jump metric. One per-map
uniform-grid interest index evaluates nine nearby cells and records symmetric peer membership.
Both sides of a pair are updated within a single map lock. The 18-tile radius is deliberately
fixed: the 5517 client removes more distant peers in its own frame-tick cull, so enlarging the
server radius would create persistent disagreement.

- At completed world admission, players already in range exchange `MsgPlayerSpawn` (`10014`)
  using real persisted appearance, level, profession, life, syndicate ID and equipped **catalog
  item type IDs** (not item-instance UIDs). Their spawn tiles are the authoritative admission tiles.
- After a committed, acknowledged eight-direction walk, already-visible peers receive the native
  `10005` movement packet exactly once. A newly visible peer receives a `10014` spawn at the
  destination instead; sending both would move the peer one additional tile in the client.
- When a committed move crosses beyond radius 18, a *previously observing* client receives
  that final native movement packet. This updates its last-known remote tile so the client's
  per-frame distance cull can remove the out-of-range role. A movement packet must not be
  silently dropped solely because the observer was removed from the new interest set.
  Disconnect/kick/world shutdown while still in view instead sends `MsgAction` (`10010`)
  subtype `135`, with the departed entity ID at wire `+4`.
- The client's `MsgAction` subtype `102` query identifies the unresolved peer in `ParameterPair`
  (wire `+8`) and the authenticated local player at `+4`. It receives `10014` only when the
  sender still has that peer in its recorded visibility set. No arbitrary ID lookup is exposed.

## Output isolation and bounded work

Per-map membership operations use a short lock for index bookkeeping and enqueuing only.
Movement commits and their visibility transitions enqueue in the same exclusive
map-partition turn; no socket operation is awaited between them. The local movement
echo and peer movement/spawn packets use the same per-recipient FIFO as other
gameplay acknowledgements, so subsequent application responses cannot overtake a
pending movement echo. No transport write occurs under the lock. Each world session has one bounded 256-packet FIFO
for peer visibility and a single writer using its existing outbound write semaphore. A full
mailbox or transport failure closes only the slow recipient, releasing its world ownership;
it cannot stall movement or disconnect the broadcaster. Shutdown cancels the sender before
closing transport. Same-account kicks retain their native 1115 path and remain independent
of peer visibility.

All reported movements must first commit in the existing partition via `WorldRuntime`; the
visibility service never changes character position. Stale world ownership/session references
cannot update the current map registration. Maps remain isolated. No network packet contains
internal realm IDs or database credentials.

## Limits / evidence still required

This is **player** entry/walking/exit replication, not complete multiplayer gameplay.
Native 24-direction movement and jumping, map transfers, dynamic occupancy and elevation,
durable position persistence, NPC/monster/item visibility and nonwalking action broadcasts
remain separate work. The spawn uses a native-inert zero for the syndicate **rank** field to
avoid inventing life-scaling values; the syndicate ID itself is populated from persisted state.
Movement facing is taken from the committed walk direction, with initial facing zero until an
authoritative persisted facing model exists. No change to Accounts v6 / Game v10 schemas or
existing secret configuration is necessary.

Full encrypted transport tests with two real authenticated clients, slow recipients, interleaved
join/move/leave and reconnect remain a deployment gate. These tests should be added in the normal
integration suite before describing visibility as public-release ready.
