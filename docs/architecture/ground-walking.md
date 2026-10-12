# Authoritative ground-walking state (first execution slice)

The map-partition implementation owns character positions. `WorldRuntime.TryWalkCharacterAsync`
submits an immutable ownership token and an eight-direction index to the character's partition.
Only the partition turn may change the map-local position. Invalid directions, stale ownership,
blocked or out-of-bounds destinations, and positions not representable in the 5517 protocol yield
no position change. The method returns the committed authoritative snapshot or `null` for refusal.

## Native evidence

`20_notes/Network/15_msgwalk_10005_direction_index_order.md` records the original 5517 client's
`Role_CommandType03_UpdateMotionPathAndSelectAction` (`0x7350B3`) and
`Role_ApplyGroundMoveActionAndPoseFromPathCursor` (`0x736315`). Their X/Y direction tables
(`data_86C608` / `data_86C628`) establish indices 0–7 as S, SW, W, NW, N, NE, E, SE.
`20_notes/Server/movement.md` documents the native 10005 packet and acknowledgement behavior.
Ground steps use composed static collision, not merely base terrain, as established by the
historical retail collision reconciliation. Both categories of evidence are intentionally distinct.

The coordinate update is a single exclusive partition command. It does not change the character's
map or its generation. Other command readers observe the position before or after the step, never
a partially updated pair. The caller can cancel before command acceptance; after acceptance its
result remains observable. Cleanup uses the existing independent reserved removal lane, and queued
commands for old generations cannot move replacement sessions.

## Deliberate boundary

**No live gameplay frame is accepted by this slice.** The login worker continues rejecting all
post-login gameplay packets. In particular, do not connect an untrusted `10005` packet directly
to `TryWalkCharacterAsync`: the complete ingress feature must validate authenticated entity
identity, movement pacing and replay handling, the selector for 24-direction movement, packet
acknowledgement and broadcast ordering, correction, and persistence of the resulting position.
It must also establish the required rules for elevation and dynamic occupancy. This slice does
not imply those behaviors are implemented, and does not acknowledge or echo any movement packet.

This bounded slice supplies the actual safe map-local coordinate mutation needed by that later
network route. It introduces no database tables, item mutations, realm assumptions, new queues,
or speculative network packets. It deliberately does not reject previously persisted spawn
positions merely because they fail a newly introduced movement predicate.
