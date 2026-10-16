# Native 5517 authoritative ground jumping — Slice #149

## Evidence and native packet

`20_notes/Server/movement.md` establishes MsgAction `10010`, subtype `137` (`0x89`), and
`CMsgAction__Process` `0x700352..0x7003DE`: the absolute target is packed at wire `+8`
(low u16 X, high u16 Y), not at the correction packet's position fields `+24/+26`.
The 5517 client animates a predicted working tile immediately; the separate
authoritative coordinate gate updates on server echo or position correction `108`.
Rejected intents therefore require correction rather than silent non-acknowledgement.
The retail `Role_RequestJumpToTile` gate (`0x658965`) requires **rounded Euclidean distance**
`1..16` and denies map flag `0x0200_0000`. The rounding algorithm is recorded
at `Map_CalculateRoundedTileDistanceByAdjustmentMode` `0x61E5F0` (mode 1).
The supplied OpenConquerPublic reference independently documents the same target,
range, and movement/echo semantics. No new client packet fields are invented.

## Authoritative execution

Only fixed-length, no-string subtype 137 packets enter the jump path. The
world session checks the authenticated character ID, reads its current
partition-owned snapshot, applies a shared movement pace/replay budget, and
submits the absolute target to the **same generation-fenced partition** as walking.
The partition repeats the rounded range and map-flag checks against its actual
current tile and rejects blocked/out-of-bounds landing cells using the composed
static-collision layer. Ground jumps additionally check the composed-terrain
rounded direct-line peak elevation against the origin (200-unit climb limit)
and refuse landing on a tile occupied by a living player. Living occupancy is
maintained in a per-map constant-time index using the life state at world
admission; later life-state transitions must update this index when combat is
implemented. Subtype `137` with wire byte `+36` set to `1` is a mounted charge,
not a ground jump, and cannot be echoed without mounted-state authority;
unknown nonzero variants are refused likewise.
Rejected well-formed movement intents schedule a native
`10010/108` correction to the current authoritative tile; they are never echoed.
The 5517 client predicts movement in its working tile, so withholding an echo
**does not** necessarily undo a refused local animation. Rejection corrections
are per-session server-clock limited to 32 per second; excess movement-denial
traffic closes the offending session rather than leaving the client displaced.
Unauthenticated identity mismatches still fail closed. Successful
jumps record a legitimate reconciliation tile, enqueue an unchanged native fixed
packet for local acknowledgement, and update peer visibility using the
existing bounded per-recipient mailboxes. Movement publication executes inside the
map partition's synchronous command turn; socket writes occur asynchronously after
that turn. This prevents subsequent map moves overtaking their visibility updates. Retained and newly visible observers
receive the existing appropriate movement/spawn treatment; a departing observer
also receives the final native jump action so its client can update the remote
tile and perform its normal distance cull. No map transfers or DB writes occur.

A separate per-session fixed-memory ingress gate drops jump intents exceeding eight
per second before authoritative snapshot reads, including invalid targets.
The movement pacer is shared across 10005 walking and 10010 jumping: a jump of
`d` rounded tiles requires at least `d * 125 ms` from the previous accepted
movement **and** before the next one; the existing two-tile 250 ms floor still
applies to selector-9 walking. This is an explicitly conservative server-side
speed budget, **not** a claim about retail jump animation cadence.

## Unresolved release gates

Active riding/status state is not persisted in the current character profile.
Admission-time life enables the basic living-occupant restriction, but dynamic
death/ghost state and its occupancy transitions remain an explicit combat/status
integration requirement. Flying uses a different 100-unit climb cap and must not
be enabled without authoritative fly status. Exact tie-case parity for the
rounded line rasterizer remains to be checked against client traces.
The native mounted jump limit uses status flag `0x32` and
`MOUNT_JUMP_MOVEPOINT_LIMIT`; implementing it from equipped steed presence
would be fabricated authorization. This first slice implements the common
(non-mounted) jump envelope, but is **not complete mounted-jump parity**.
Native jump-specific status effects and animation cadence still
require additional grounded evidence and two-client retail acceptance tests.
Avoid representing this slice as public-release-complete movement.
