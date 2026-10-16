# Authoritative movement eligibility — slice #150

## Boundary and source confidence

This slice closes two known gaps in the authenticated 5517 world movement path.
The primary reference is `20_notes/Server/movement-eligibility-5517.md`, recorded
from the original 5517 `Conquer.exe`. The retail client's status `5` means dead,
its status `0xA` means ghost, and its status `0x32` means riding. A riding client
issues `10005` with full-width selector `9` while vigor is above 1; an alive
client without ride status uses ordinary eight-direction movement. The client's
walking gate denies dead movement unless ghosted. The original TQ 15.10 server
binaries are **not** retail 5517 authority and provide no mounted gameplay.

## Trusted authority and execution

The current world has authenticated character ownership, persisted life at
admission, exclusive map-partition execution, native movement packet framing,
server-clock pacing, and a bounded FIFO for client/peer delivery. It does **not**
currently own active ride/ghost statuses or the vigor writer for packet `1033`
mode 2. Therefore it cannot accept selector-9 movement simply because the
client requested that packet format. Within the generation-fenced `WorldRuntime`
entry point, a selector-9 request is rejected before enqueuing a movement command.
The verified 24-direction tables remain in `MapInstance.TryWalk` as the
map-displacement primitive, covered by unit tests. Only the runtime is the
client-reachable movement admission API; any future 24-direction authorization
must be added there using server-owned state, never client-supplied flags.

`MapInstance.TryWalk` also rejects movement for any admitted snapshot with
`IsAlive == false`, regardless of requested direction and selector. This check
runs inside the exclusive partition turn, so stale-session or out-of-order world
state cannot authorize dead ground walking. `WorldRuntime.AdmitCharacterAsync`
sets this field from the server's persisted life value, not from an inbound
movement packet. Dead-with-ghost movement is intentionally denied until the
server owns and synchronizes ghost status; this is a **fail-closed compatibility
limitation**, not a claim that retail 5517 prohibits ghosts from walking.

An authenticated refused command continues to use the existing native
`10010/108` correction path. The client predicts its working tile immediately,
so dropping a walk without correction is not sufficient. The same existing
per-session correction budget, replay guard, and visibility FIFO apply. No
new queues, timers, database fields, packet variants, or cross-thread mutation
paths are introduced.

## Regression contract

- All 24 native displacement vectors remain reproducible in isolated map tests;
  the network-reachable runtime denies selector 9 and does not mutate the tile.
- An authorized ordinary ground walk still commits, self-echoes, and transitions
  interest membership in partition order.
- A dead snapshot cannot walk, and a character admitted with life zero remains
  immobile through the runtime without mutating its authoritative coordinates.
- The encrypted world-session test verifies an unauthenticated selector-9 walk
  yields native correction 108 and is never echoed or published to other clients.
- Stale generation checks and the existing ground movement/visibility test suite
  remain valid.

## Deliberate future work / release gates

- A server-authoritative status owner, including 128-bit status-mask writers and
  secondary status details (packet 2075 replaces the mask wholesale).
- Lifecycle-coherent death/ghost, freeze, stun, flustered auto-wander, transformation,
  flying, status-dependent cadence, and event-driven updates to living occupancy.
- Server-owned mount equip/skill authorization, ride flag `0x32`, vigor current/max,
  cost/recovery rules, 1033-mode-2 update, selector-9 admission, and charge handling.
- Client/server two-client acceptance tests for native 5517 status effects and
  correction/animation behavior. Later-version Long/Canyon/Fox implementations
  may inform server mechanics but are not evidence of retail 5517 packet layout.

No claim of full status, ghost, or mounted parity is made by this slice.
