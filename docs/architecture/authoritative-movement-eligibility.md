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
server-clock pacing, and a bounded FIFO for client/peer delivery. It owns
primary dead/ghost state for this slice, but not active riding status or the
vigor writer for packet `1033` mode 2. Therefore it cannot accept selector-9
movement simply because the client requested that packet format. Within the generation-fenced `WorldRuntime`
entry point, a selector-9 request is rejected before enqueuing a movement command.
The verified 24-direction tables remain in `MapInstance.TryWalk` as the
map-displacement primitive, covered by unit tests. Only the runtime is the
client-reachable movement admission API; any future 24-direction authorization
must be added there using server-owned state, never client-supplied flags.

`MapInstance.TryWalk` accepts a living character or a corpse with a server-owned ghost
flag, but no other dead character. Both status bits (dead index 5 and ghost index
10) live in a native-width 128-bit primary mask on the partition-owned snapshot.
Dead-without-ghost refuses movement; dead-plus-ghost permits only selector 0/ordinary
walking. Ground jumping still requires life. The login HP value only establishes
initial life/death; inbound movement does not set status. The verified 3-second
client request 145 is now accepted idempotently after a monotonic corpse-delay
check. It commits ghost status in the same exclusive partition execution lane.
The initial dead mask is sent to the local client after login reset; the transition
then publishes 10017 attribute 25 (full 128-bit mask), followed by attribute 11
(lookface family 98/99), to the local client and current observers. The 10014
spawn includes status mask and the verified corpse lying pose for late observers.
Future death/combat/status systems must use this same owner and serialization path;
no unauthorized client status writer exists.

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
- A corpse remains immobile and a dead-plus-ghost snapshot can walk via the
  ordinary selector; neither can use unauthenticated mounted movement or jump.
- The encrypted world-session test verifies an unauthenticated selector-9 walk
  yields native correction 108 and is never echoed or published to other clients.
- Stale generation checks and the existing ground movement/visibility test suite
  remain valid.

## World-session integration and corpse pose safety

The 5517 client emits 1056 mode 2 when dead status is added, including
when status is hydrated after login. The authenticated session now recognizes
only the native 12-byte trade-quit form and checks the entity ID. There is no
server trade state yet: accepting this idempotent notification performs no
inventory, currency, partner, or account mutation and sends no fabricated ack.
Other 1056 modes and malformed layouts remain unsupported and close the session.
The native +10..11 bytes are opaque and deliberately not treated as zero-only.

Corpse avatars cannot change facing or standing pose. Authorization and
observer fanout occur in the same serialized map-partition turn as the ghost
transition, preventing a corpse pose from being cached for late observers.
Authenticated ghosts and living characters retain the existing avatar actions.

## Deliberate future work / release gates

- General server-authoritative primary-status mutation beyond dead/ghost, including
  blessing, freeze, stun, fly, and secondary status details (2075).
- Combat-driven death, self/spell revival, freeze, stun, flustered auto-wander, transformation,
  flying, status-dependent cadence, and event-driven updates to living occupancy.
- Server-owned mount equip/skill authorization, ride flag `0x32`, vigor current/max,
  cost/recovery rules, 1033-mode-2 update, selector-9 admission, and charge handling.
- Client/server two-client acceptance tests for native 5517 status effects and
  correction/animation behavior. Later-version Long/Canyon/Fox implementations
  may inform server mechanics but are not evidence of retail 5517 packet layout.

This slice implements the client-reachable corpse→ghost movement envelope, not
combat-driven death, full status effects, self/spell revival, or riding. Initial
zero-HP login status is a documented compatibility policy; retail 5517 reconnect
handling is not proven. Native wire fields and the ghost walking gate are client
verified; the 3-second server enforcement and lifecycle from persisted zero HP
are deliberate defensive reconstruction decisions.
