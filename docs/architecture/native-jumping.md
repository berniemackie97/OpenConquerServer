# Ground jumping (5517)

## Sources and packet format

`20_notes/Server/movement.md` documents MsgAction `10010` subtype `137` (`0x89`) and
`CMsgAction__Process` `0x700352..0x7003DE`. The absolute target is packed at wire `+8` (low u16 X,
high u16 Y), not in the correction packet's position fields `+24/+26`. The 5517 client starts
animating to a predicted working tile at once. Its separate server-confirmed coordinate changes only
when the server echoes the jump or sends position correction `108`. A rejected jump therefore needs
a correction; staying silent is not enough. The retail `Role_RequestJumpToTile` check (`0x658965`)
requires a rounded Euclidean distance of `1..16` and refuses maps with flag `0x0200_0000`. The
rounding algorithm is recorded at `Map_CalculateRoundedTileDistanceByAdjustmentMode` `0x61E5F0`
(mode 1). The OpenConquerPublic reference, an independent source, documents the same target, range,
and movement and echo behavior. The server does not invent any client packet fields.

## Server-side handling

Only fixed-length subtype 137 packets with no strings reach the jump path. The world session checks
the authenticated character ID, reads the character's current snapshot from its map partition, and
applies the movement pace and replay budget it shares with walking. It then submits the absolute
target to the same partition that handles walking, under the same ownership-generation check. The
partition repeats the rounded range and map-flag checks against the character's actual current
tile. It rejects landing tiles that are blocked or out of bounds in the composed static-collision
layer.

Ground jumps also compare the highest elevation on the rounded straight line, taken from the
composed terrain, with the starting tile (a climb of at most 200 units). They refuse to land on a
tile occupied by a living player. Living occupancy is kept in a per-map index with constant-time
lookups, filled from each player's life state at world admission. When combat is implemented,
later life-state changes must update this index. Subtype `137` with wire byte `+36` set to `1` is a
mounted charge, not a ground jump. The server does not track mounted state, so it cannot echo a
charge and refuses it. Unknown nonzero variants are refused the same way.

A rejected but well-formed movement request is never echoed. The server schedules a `10010/108`
correction to the character's current server-side tile instead. The 5517 client predicts movement
on its working tile, so withholding the echo alone may not undo a refused local animation.
Corrections for rejected movement are limited to 32 per second per session, measured on the server
clock. A session that goes over that limit is closed rather than left out of position. A request
whose identity does not match the authenticated character still closes the session.

A successful jump adds the landing tile to the session's position-reconciliation history, queues
the unchanged fixed-size packet as the sender's acknowledgement, and updates peer visibility
through the existing size-limited per-recipient mailboxes. Movement is published inside the map
partition's synchronous command turn, and socket writes happen asynchronously after that turn.
This keeps a later move on the map from overtaking the visibility updates of an earlier one.
Observers who stay in range or come into range get the usual movement or spawn packets. An observer
who goes out of range also receives the final jump action, so its client can update the remote
tile and run its normal distance cull. Jumps do not transfer maps or write to the database.

A separate per-session gate with fixed memory refuses jump requests beyond eight per second,
including ones with invalid targets, before the target is checked. Like any refused movement, a
refused jump gets a 108 position correction, and a session that needs more than 32 corrections
in one second is closed.

Walking (10005) and jumping (10010) share one movement pacer. A jump of `d` rounded tiles needs at
least `d * 125 ms` after the previous accepted movement and the same interval before the next one.
The existing two-tile 250 ms minimum still applies to selector-9 walking. These are cautious
server-side speed limits. They say nothing about the timing of the retail jump animation.

## Not implemented yet

The character profile does not store riding or other active status.
Life state at admission is enough for the basic rule against landing on a living player, but death
and ghost changes, and the occupancy updates they cause, still have to be connected when combat and
status are added. Flying uses a different climb limit of 100 units and must stay disabled until
the server tracks fly status. Whether the rounded line rasterizer breaks ties the same way as the
client still has to be checked against client traces.

The 5517 mounted jump limit uses status flag `0x32` and `MOUNT_JUMP_MOVEPOINT_LIMIT`. Deriving it
from whether a steed is equipped would grant permission the server has no basis for, so it is not
done. Only ordinary (non-mounted) jumps are implemented, and mounted jumps do not match retail yet.
Jump-specific status effects and animation timing need more research and retail tests with two
clients. Movement as a whole is not ready for a public release.
