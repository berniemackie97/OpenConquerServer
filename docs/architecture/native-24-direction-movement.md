# 24-direction movement (5517)

## Client behavior and wire format

In the retail `Conquer.exe`, the message handler `CMsgEntityAction__Process` at `0x7405E7` uses the
24-entry delta tables at `0x86C804` / `0x86C864` only when the full 32-bit selector at packet offset
`+12` equals `9`. All other selectors use ordinary eight-direction movement. Only the low byte of
the direction dword at `+4` is reduced modulo the table length. The upper bits and the other packet
fields the server does not interpret must come back unchanged in the acknowledgement sent after the
move commits.

The 24-element delta arrays are recorded in
`OpenConquerPublic/src/OpenConquer.Protocol/Game/Packets/MsgEntityAction10005.cs`.
`20_notes/Server/movement.md` identifies the same retail tables independently and describes the
retail behavior in which the client's move stands once the server echoes it. FoxConquer5517 also
treats selector 9 as the old riding movement path, not as general directional walking.

## Server-side handling

The authenticated session accepts 10005 only for its own character ID. It normalizes the direction
and passes the full-width selector and normalized index to the character's map partition, which is
the only writer of positions and rejects commands from older session generations. Before committing
the position, the partition checks the map bounds and the map's composed static collision at the
destination. Mount authorization is not available yet, so to stop a two-tile move from cutting
across a blocked cell, the partition also checks the static cells in between. For asymmetric
two-tile deltas it checks both possible intermediate cells. This is a server-side safety rule. The
retail client's handling of intermediate cells has not been confirmed. A refused move gets
correction 108, which undoes the position the client predicted.

After the commit, the original packet is echoed byte for byte to the local player, nearby observers
receive the same 10005, and the visibility graph is recomputed from the committed tile. Spawn
packets for players who arrive later use the existing eight-direction facing field, with the facing
index kept within its valid range. No new wire field is added. Position reconciliation records only
tiles the server committed.

## Riding and remaining work

`WorldRuntime` currently rejects all selector-9 movement, even from authenticated sessions. The
server has no trusted source for the active ride status (`0x32`) or vigor (`1033` mode 2), so a
well-formed selector-9 request is not enough to allow a two-tile move. The session sends correction
`10010/108` to undo the client's predicted move. The 24-entry direction tables and their collision
tests stay in place for when the server can authorize riding. The server does not fake a ride flag
or guess riding from equipment, and the client packet is unchanged. The stricter pacing limit for
selector 9 is kept, but passing it does not grant permission to ride. See
[movement eligibility](authoritative-movement-eligibility.md).

Not implemented yet: checking whether a character has a valid mount or status, showing mounts to
other players, mounted vigor, and full retail mounted gameplay. Each must be authorized by the
server and match the client's behavior before release. Ground jumping is implemented. Teleportation,
saving positions on the server, and NPC and monster replication are separate work. Encrypted tests
with two clients are required before public release. This feature reuses the existing world
visibility and movement code instead of duplicating it.
