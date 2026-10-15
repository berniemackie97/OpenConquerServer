# Native 5517 authoritative ground jumping — Slice #149

## Evidence and native packet

`20_notes/Server/movement.md` establishes MsgAction `10010`, subtype `137` (`0x89`), and
`CMsgAction__Process` `0x700352..0x7003DE`: the absolute target is packed at wire `+8`
(low u16 X, high u16 Y), not at the correction packet's position fields `+24/+26`.
The client moves on server echo, not when sending its request. The retail
`Role_RequestJumpToTile` gate (`0x658965`) requires **rounded Euclidean distance**
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
static-collision layer. No packet is echoed for rejected jumps. Successful
jumps record a legitimate reconciliation tile, echo the validated native fixed
packet without changing its payload fields, and update peer visibility using the
existing bounded per-recipient mailboxes. Retained and newly visible observers
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
The native mounted jump limit uses status flag `0x32` and
`MOUNT_JUMP_MOVEPOINT_LIMIT`; implementing it from equipped steed presence
would be fabricated authorization. This first slice implements the common
(non-mounted) jump envelope, but is **not complete mounted-jump parity**.
Native jump-specific elevation/status effects and animation cadence still
require additional grounded evidence and two-client retail acceptance tests.
Avoid representing this slice as public-release-complete movement.
