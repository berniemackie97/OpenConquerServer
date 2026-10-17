# Native 5517 24-direction movement — Slice #148

## Evidence and wire contract

The retail `Conquer.exe` message handler `CMsgEntityAction__Process` at `0x7405E7`
selects the 24-entry delta tables at `0x86C804` / `0x86C864` **only** when the
full 32-bit selector at packet offset `+12` equals `9`. All other selectors use
native eight-direction movement. Only the low byte of the direction dword at
`+4` is normalized modulo the table length; upper bits and other opaque packet
fields must survive the post-commit acknowledgement unchanged.

The verified 24-element delta arrays are recorded in the supplied
`OpenConquerPublic/src/OpenConquer.Protocol/Game/Packets/MsgEntityAction10005.cs`;
`20_notes/Server/movement.md` independently identifies the retail tables and
client-authoritative-on-echo behavior. FoxConquer5517 corroborates selector-9
as the legacy ride movement path, not arbitrary directional walking.

## Authoritative execution

The authenticated session accepts 10005 only for its own character ID, normalizes
the direction and submits the full-width selector plus normalized index to the
existing generation-fenced, single-writer map partition. The partition checks
canonical composed static collision and bounds at the resulting destination
before committing the position. To avoid a two-tile shortcut across a blocked
cell when mount authorization is unavailable, it also conservatively checks
intermediate static cells, including both plausible intermediate cells for
asymmetric two-tile deltas. This is a **defensive server rule**, not a verified
native retail intermediate-path policy. Since slice #149, refusals send native correction 108 to undo the client's predicted position.

The original packet is echoed byte-for-byte to the local player after commit;
nearby observers receive the same 10005, and the visibility graph is recomputed
using the committed tile. Late-player spawn uses the existing eight-direction
presentation field with a bounded facing index; no new or fabricated wire field
is introduced. Position reconciliation records only server-committed locations.

## Abuse boundaries and release gates

As of slice #150, authenticated selector-9 movement is **denied at WorldRuntime**.
The current server has no trusted source of the active ride status (`0x32`) or
vigor (`1033` mode 2), so a well-formed selector-9 request is not sufficient
authorization to move two tiles. The existing session sends native correction
`10010/108` for the rejected predicted movement. The 24-entry native direction
tables and collision tests remain intact for future server-authorized riding;
there is no fake ride bit, equipment-based guess, or changed client packet.
The existing conservative selector-9 pacing limit is retained but is not used
to grant riding permission. See [movement eligibility](authoritative-movement-eligibility.md).

This slice does **not** establish whether a character has a valid mount/status,
replicate mount visuals, model mounted vigor, or claim fully native mounted gameplay.
These are explicit gameplay authorization and client-parity release gates.
Ground jumping was implemented in slice #149; teleportation, authoritative
position persistence, and NPC/monster replication remain separate work. Encrypted two-client acceptance testing is required
before public release. The existing world visibility and movement implementation
are reused rather than duplicated.
