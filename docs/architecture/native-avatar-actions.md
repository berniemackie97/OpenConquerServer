# Native 5517 turn and pose actions — Slice #146

After authenticated world admission, `MsgAction` (`10010`) subtypes **79** and **81**
are supported as player presentation operations. Native 5517-era corroboration comes from
`FoxConquer5517/MsgServer/Network/GameServer/Handlers/10010-MsgAction.cs`, with
`LongV6270/src/Long.Kernel/Network/Game/Packets/MsgAction.cs` as a later corroborating
implementation. The original 5517 client evidence in `20_notes/Network/01_core_packets.md`
establishes the direction at `10014 +0x5A` and action type at `+0x5B`.

Only exact 38-byte, zero-string actions are admitted. The actor ID must match the
session's authenticated, generation-fenced world ownership. Direction 79 accepts only
0–7 at `10010 +22`; pose 81 accepts only the enumerated native 5517
`EntityAction` codes from `10010 +8`. Unsupported poses or a forged actor ID fail
closed. Neither action can move the world character or alter the character's account,
map, equipment or privileges. Unrecognized gameplay messages remain fail-closed;
this slice does **not** add an unbounded ignore-anything fallback.

The reply preserves the proven subtype, actor ID, requested direction or pose, and
timestamp. All other client-controlled action fields are normalized to zero so
unused input is never relayed as authority. A matching local action acknowledgement
is sent to the actor, and the same native packet is queued only to currently
visible peers, using the existing bounded per-session FIFO visibility writers.
No socket write or await occurs beneath the per-map visibility lock.

A per-session limit of eight cosmetic changes per second prevents a single player
from flooding every nearby peer. Excess cosmetic actions are dropped without
applying state; this is an explicit operational resource-protection bound, not a
claim about retail 5517 animation cadence. Movement speed policy is unaffected.

The visibility index holds the current direction and pose. A newly visible player
is spawned at its real tile with both values in native `10014`, including the
previously unused but client-proven `+0x5B` action field. Successful walking updates
facing and returns the cached pose to `STAND` (100), matching the 5517-era
walking reference; refused walking does not mutate either field.

## Deliberate boundaries

Actions 79 and 81 are fully wired end to end; other gameplay action subtypes, mounted
24-direction walking, jumping, timing/status effects, creation defaults, and
position persistence are **not** changed. In particular, the 187 ms timing rule
reported for the older retail 15.10 server is **not** treated as proven 5517
behavior. The existing movement limiter is unchanged and needs a separate
native-5517-evidence-backed movement policy. No migration, secret, or realm change.

The native 5517 client packet end-to-end behavior should also be confirmed on a
real connected pair of clients before a public gameplay release.
