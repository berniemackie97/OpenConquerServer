# Native 5517 position reconciliation — Slice #144

The original client sends unsolicited `MsgAction` (`10010`) subtype **100** (idle position report),
and replies to a server correction with subtype **146**. Both carry the *client's opinion* of its
position in the packed `uint32` at wire offset `+8` (X low 16 bits, Y high 16 bits). Neither is an
authoritative movement command. The client consumes a server-originated subtype **108** as a
coordinate correction using the tile at wire `+24`/`+26`, then sends 146. Do **not** echo an inbound
100 or 146: 146 has a response branch and an echo can produce an indefinite exchange.

The native client evidence is `20_notes/Server/position-sync.md`, verified against Conquer.exe 5517;
its `What this server does` section describes the separate OpenConquerPublic reference, **not**
previous behavior in this repository. This slice implements only the part backed by native evidence
and relevant to the existing eight-direction walking path.

## Handling

- Only exact 38-byte, zero-string `10010` reports with subtype 100 or 146 enter this path, after
  single-use ticket redemption, completed character login, world admission and per-session
  ownership validation. Unexpected subtypes, spoofed entity IDs and malformed reports terminate the
  session. A position report can never move the authoritative world character.
- Each session holds its spawn tile and 64 most recent **committed server-authorized tiles**. A
  report matching any retained tile is considered in flight, even if far from the latest tile. This
  prevents distance-based false positives when several movement echoes are delayed.
- Three consecutive reports naming no retained tile request a correction. The correction reads
  the current generation-fenced world partition snapshot, sends subtype 108 with that X/Y, and
  never accepts the packet's claimed location as authority. One correction at most per second of
  monotonic server time; a matching report resets the divergence sequence. A correction carries no
  invented map transfer, position mutation, or second 146 reply.
- The history is bounded and per session. No shared mutable map state, client-selected identity,
  SQL write, cache, timer worker, or new ingress listener is introduced. Closing a session discards
  its history automatically. The sequential world-session packet reader owns all history updates.

## Limits

A finite history cannot cover arbitrarily delayed packets. Live visibility replication, running,
jumping, cross-map transitions, occupancy/elevation rules, position durability across relogins,
and anti-cheat action against persistent divergence remain **unimplemented**. The three-report
threshold and one-second correction cooldown are conservative server policies, not recovered TQ
server configuration. Native subtype 100's *server-to-client challenge* remains unimplemented;
there is no proven payload contract for one here.

No database migration, user-secret change, realm change or protocol extension is needed. Full
end-to-end encrypted transport tests are still required before public gameplay deployment.
