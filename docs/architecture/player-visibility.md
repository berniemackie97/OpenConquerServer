# Player visibility (5517)

Player visibility shows each player to the other players near them. It builds on authenticated
world sessions, eight-direction ground walking checked by the server, and world residency tied to
an ownership generation, and it sends the packets the 5517 client uses to show other players.
Sources: `20_notes/Server/entity-visibility.md` (client xrefs 0x61ED75, 0x755F64, 0x70051A,
0x7407B4 and 0x740730) and `20_notes/Server/movement.md`. The separate `OpenConquerPublic`
reference is used only to cross-check field offsets and does not replace the client trace.

## Visibility rules

A player sees other players on the same world map when `abs(dx) <= 18 && abs(dy) <= 18`. This is
Chebyshev distance, which is different from the rounded Euclidean distance used for jumps. Each map
has one uniform-grid interest index that checks the nine nearby cells and records visibility in
both directions. Both sides of a pair are updated under a single map lock. The 18-tile radius is
fixed on purpose: the 5517 client removes more distant players in its own per-frame cull, so a
larger server radius would leave server and client permanently out of step.

- When world admission completes, players already in range exchange `MsgPlayerSpawn` (`10014`)
  built from the saved appearance, level, profession, life, syndicate ID and equipped item type IDs
  from the catalog (not item instance UIDs). Each spawn uses the tile where the server admitted that
  player.
- After a committed and acknowledged eight-direction walk, players who could already see the
  walker receive the `10005` movement packet once. A player who can see the walker for the first
  time receives a `10014` spawn at the destination instead. Sending both would move the walker one
  extra tile on that client.
- When a committed move takes a player beyond radius 18 of an observer who could see them, that
  observer still receives the final movement packet. This updates the observer's last-known tile
  for that player, so the client's per-frame distance cull can remove the out-of-range role. The
  movement packet must not be dropped just because the observer has left the new interest set. If
  the player disconnects, is kicked, or the world shuts down while they are still in view, observers
  get `MsgAction` (`10010`) subtype `135` instead, with the departed entity ID at wire `+4`.
- The client's `MsgAction` subtype `102` query names the unknown player in `ParameterPair` (wire
  `+8`) and the authenticated local player at `+4`. The server answers with `10014` only if that
  player is still in the sender's recorded visibility set, so the query cannot look up arbitrary
  IDs.

## Output isolation and limits

Per-map membership changes hold a short lock that covers only index bookkeeping and queuing. A
movement commit and its visibility changes are queued in the same exclusive map-partition turn, and
no socket operation is awaited in between. The local movement echo and the movement and spawn
packets for other players go through the same per-recipient FIFO as other gameplay
acknowledgements, so later responses cannot overtake a pending movement echo. Nothing writes to
the transport under the lock.

Each world session has one 256-packet FIFO for peer visibility and a single writer that uses the
session's existing outbound write semaphore. If the mailbox fills or the transport fails, only the
slow recipient is closed and its world ownership released. Movement does not stall, and the player
whose update is being sent stays connected. Shutdown cancels the sender before closing the
transport. Kicks for the same account still use their 1115 path, separate from peer visibility.

Every reported movement must first commit in its partition through `WorldRuntime`. The visibility
service never changes a character's position. A stale world ownership or session reference cannot
update the current map registration. Maps stay isolated from each other. No network packet
contains internal realm IDs or database credentials.

## Limits and open work

This covers players entering view, moving and leaving view. It is not complete multiplayer
gameplay. Map transfers, dynamic occupancy and elevation, saving positions to the database, and
NPC, monster and item visibility are separate work. Jumps, turns and poses are relayed through the
same per-session writers; see [ground jumping](native-jumping.md) and
[turn and pose actions](native-avatar-actions.md). The spawn sends zero in the syndicate rank
field, a value with no effect in the client, to avoid inventing life-scaling values. The syndicate
ID itself comes from saved state. Facing and pose come from the last accepted walk, turn or pose
and are not saved to the database. Player visibility added no schema migrations and no new secret
configuration.

Before deployment, full tests over encrypted transport still need to cover two real authenticated
clients, slow recipients, interleaved join, move and leave, and reconnects. They should be added to
the normal integration suite before visibility is described as ready for a public release.
