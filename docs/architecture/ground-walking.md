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

## Native eight-direction gameplay ingress (Slice #138)

Completed existing-character sessions now accept **only** native `10005` walk packets with the
exact 24-byte fixed layout. The authenticated session's authoritative character ID must match
the packet entity ID. The wire parser preserves opaque packed-direction upper bits, movement tick,
selector and map-state fields for the response, but never trusts them as a position or authority.
Selector `9` denotes 24-direction movement and is explicitly unsupported; it is not treated as a
native eight-direction move. Only low-byte direction indices `0`–`7` are accepted.

Each session independently applies a monotonic server-clock cadence gate (at least 125 ms
between accepted attempts), plus modulo-`uint32` client-tick duplicate/backwards rejection.
A valid attempt enters the existing partition-owned `TryWalkCharacterAsync` path with the
session's generation-fenced ownership token. The original `10005` fields are echoed to that
client **only after** the partition commits a permitted move. A refused move or pacing attempt
is not acknowledged. Malformed or unsupported gameplay traffic closes the session. The client
is authoritative for neither its own entity ID nor its tile location.

## Native position reconciliation (Slice #144)

The authenticated world session now accepts only native 10010 subtype 100/146 as
client position reports, never movement authority. It remembers a bounded history of
server-committed tiles and uses native subtype 108 for conservative self-correction
after repeated reports of never-authorized positions. See
[position synchronization](position-synchronization.md).

## Remaining production gates

This is an **eight-direction walking ingress**, not a complete movement or multiplayer release.
Native 24-direction running, jump requests, dynamic occupancy/elevation restrictions, movement
replication to other characters, and authoritative position persistence across relog
remain unimplemented. Position correction is limited to repeated divergence reports. Do not claim complete gameplay support or deploy as an MMO movement
system until those requirements are satisfied and tested. No new schema, speculative wire fields,
or cross-partition movement are introduced in this slice.

This bounded slice supplies the actual safe map-local coordinate mutation needed by that later
network route. It introduces no database tables, item mutations, realm assumptions, new queues,
or speculative network packets. It deliberately does not reject previously persisted spawn
positions merely because they fail a newly introduced movement predicate.
