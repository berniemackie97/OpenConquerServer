# Ground walking

Character positions are owned by the map partition. `WorldRuntime.TryWalkCharacterAsync` sends an
immutable ownership token and an eight-direction index to the character's partition, and only the
partition's own turn can change the character's position on the map. The position stays the same for
an invalid direction, a stale ownership token, a blocked or out-of-bounds destination, or a position
the 5517 protocol cannot represent. The method returns the committed snapshot, or `null` when the
step is refused.

## Client sources

`20_notes/Network/15_msgwalk_10005_direction_index_order.md` records two functions in the original
5517 client: `Role_CommandType03_UpdateMotionPathAndSelectAction` (`0x7350B3`) and
`Role_ApplyGroundMoveActionAndPoseFromPathCursor` (`0x736315`). Their X/Y direction tables
(`data_86C608` / `data_86C628`) define indices 0 to 7 as S, SW, W, NW, N, NE, E, SE.
`20_notes/Server/movement.md` documents the 10005 packet and how it is acknowledged. Ground steps
are checked against composed static collision, not just base terrain, as the earlier reconciliation
with retail collision data showed. The two kinds of source, client research and collision data, are
kept separate.

The coordinate update is a single partition command that runs exclusively. It does not change the
character's map or generation. Other commands see the position from before or after the step, never
a half-updated pair. The caller can cancel before the command is accepted. Once it is accepted, the
step goes ahead and its result is visible. Cleanup uses the existing reserved removal lane, which
runs independently, and commands queued for an old generation cannot move a session that replaced
it.

## Eight-direction walk packets

Once an existing character has finished logging in, its session accepts `10005` walk packets only in
the fixed 24-byte layout. The packet's entity ID must match the session's character ID. The parser
keeps the upper bits of the packed direction, the movement tick, the selector, and the map-state
fields for the response, but never trusts them as a position or as permission. Selector `9` picks
the 24-entry movement delta table. Every other full-width selector picks the eight-direction table.
The low direction byte is reduced modulo 24 or 8 respectively. The upper bits, map state, and client
tick are kept so the echo matches the original packet. See
[24-direction movement](native-24-direction-movement.md).

Each session paces walks by the server's monotonic clock: at least 125 ms between ordinary accepted
attempts, doubled when either neighboring accepted move uses the two-tile table. It also rejects a
client tick that repeats or goes backwards, compared modulo `uint32`. A valid attempt goes to the
partition's `TryWalkCharacterAsync` with the session's ownership token, which is tied to the session
generation. The original `10005` fields are echoed to that client only after the partition commits
an allowed move. A refused move, or one rejected by pacing, is not acknowledged. Malformed or
unsupported gameplay traffic closes the session. The server does not trust the client for its own
entity ID or its tile position.

## Position reconciliation

The world session accepts 10010 subtypes 100/146 as the client's reports of its position. They never
move the character. The session keeps a size-limited history of tiles the server committed. When the
client keeps reporting positions the server never allowed, the session corrects it with subtype 108.
See [position synchronization](position-synchronization.md).

## Facing and avatar actions

`10010` actions `79` (turn) and `81` (pose) in their fixed layout update how the player appears to
others on the map. They are acknowledged to the local player and relayed only to players who already
see them. The player spawn packet `10014` carries the current action at offset `+0x5B` instead of
always zero. Walking resets the stored pose to standing, as the 5517 walk handler does. See
[avatar actions](native-avatar-actions.md).

## Not yet implemented

This covers eight- and 24-direction walking. It is not complete movement or multiplayer support.
Mounted-status authorization, elevation checks specific to jumping, movement replication for NPCs,
monsters and ground items, and saving the position on the server across relogs are not implemented.
Player spawn and walking replication to nearby players are implemented, and ground jumps are covered
in [ground jumping](native-jumping.md). Position correction happens only after repeated reports that
diverge. Do not present this as complete gameplay support or run it as an MMO movement system until
those pieces exist and are tested.

`TryWalkCharacterAsync` provides the position change within one map that the packet handling above
relies on. It adds no database tables or schema, item changes, realm assumptions, queues,
unconfirmed packets or wire fields, or movement across partitions. Spawn positions saved earlier are
not rejected just because they fail the new movement check.
