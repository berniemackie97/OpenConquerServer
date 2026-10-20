# Position reconciliation (5517)

The original client sends unprompted `MsgAction` (`10010`) subtype 100 (idle position report) and
answers a server correction with subtype 146. Both carry the position the client believes it has,
packed in the `uint32` at wire offset `+8` (X in the low 16 bits, Y in the high 16 bits). Neither is
a movement command. The client treats a subtype 108 from the server as a coordinate correction to
the tile at wire `+24`/`+26`, then sends 146. The server never echoes an inbound 100 or 146: the
client has a response branch for 146, so an echo could start an endless exchange.

The client behavior is documented in `20_notes/Server/position-sync.md`, verified against
Conquer.exe 5517. Its `What this server does` section describes the separate OpenConquerPublic
reference, not earlier behavior of this repository. This server implements only the part that the
client research confirms and that matters for the existing eight-direction walking path.

## Handling

- Only 38-byte `10010` reports with no strings and subtype 100 or 146 reach this path, and only
  after single-use ticket redemption, completed character login, world admission and the
  per-session ownership check. Unexpected subtypes, spoofed entity IDs and malformed reports end
  the session. A position report never moves the character in the world.
- Each session keeps its spawn tile and the 64 most recent tiles the server committed for it. A
  report that matches any kept tile is treated as in flight, even if it is far from the latest
  tile. This avoids false alarms from a distance check when several movement echoes are delayed.
- Three reports in a row that match no kept tile trigger a correction. The correction reads the
  current snapshot from the world partition (under the ownership-generation check), sends subtype
  108 with that X/Y, and never treats the location claimed in the packet as true. At most one
  correction is sent per second of monotonic server time, and a matching report resets the count.
  A correction does not add a map transfer, change the position or send a second 146 reply.
- The history is per session and fixed in size. There is no shared mutable map state,
  client-selected identity, SQL write, cache, timer worker or new ingress listener. Closing a
  session discards its history. Only the world session's sequential packet reader updates the
  history.

## Limits

A fixed-size history cannot cover packets that are delayed for an arbitrary time. NPC and monster
visibility, running, jumping, cross-map transitions, occupancy and elevation rules, keeping the
position across relogins, and anti-cheat action against lasting divergence are not implemented.
The three-report threshold and one-second correction cooldown are cautious choices by this server,
not settings recovered from the TQ server. The server-to-client challenge form of subtype 100 is
not implemented either, because no payload format for it has been confirmed.

No database migration, user-secret change, realm change or protocol extension is needed. Full
end-to-end tests over encrypted transport are still required before gameplay is deployed publicly.
