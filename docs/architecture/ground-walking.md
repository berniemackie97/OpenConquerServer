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
Selector `9` selects the native 24-entry movement delta table. Every other full-width selector
selects the eight-direction table. The low direction byte is normalized modulo 24 or 8
respectively; opaque upper bits, map state, and client tick are preserved for exact echoes.
See [native 24-direction movement](native-24-direction-movement.md).

Each session independently applies a monotonic server-clock cadence gate (at least 125 ms
between ordinary accepted attempts, conservatively doubled when either adjacent accepted move
uses the two-tile table), plus modulo-`uint32` client-tick duplicate/backwards rejection.
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

## Native facing and avatar action (Slice #146)

Authenticated fixed-layout native `10010` actions `79` (turn) and `81` (pose)
now update the per-map peer presentation state, acknowledge the local player,
and relay only to existing observers. Player spawn `10014` now carries the
current action at native offset `+0x5B`, rather than always zero. Walking
resets the cached pose to standing, exactly as the 5517-era walk handler does.
See [native avatar actions](native-avatar-actions.md).

## Remaining production gates

This is an **eight- and 24-direction movement ingress**, not a complete movement or multiplayer release.
Mounted-status authorization, jump requests (including jump-specific elevation gates), movement
replication for NPCs/monsters/ground items, and authoritative position persistence across relog
remain unimplemented. Player spawn and local walking replication are now implemented in Slice #145. Position correction is limited to repeated divergence reports. Do not claim complete gameplay support or deploy as an MMO movement
system until those requirements are satisfied and tested. No new schema, speculative wire fields,
or cross-partition movement are introduced in this slice.

This bounded slice supplies the actual safe map-local coordinate mutation needed by that later
network route. It introduces no database tables, item mutations, realm assumptions, new queues,
or speculative network packets. It deliberately does not reject previously persisted spawn
positions merely because they fail a newly introduced movement predicate.
