# Native 5517 turn and pose actions — Slice #146

Authenticated world-session `MsgAction` (`10010`) subtypes **79** (facing) and **81**
(pose) update peer presentation without mutating world position. Their packet layouts
are corroborated by `20_notes/Network/01_core_packets.md`,
`FoxConquer5517/MsgServer/Network/GameServer/Handlers/10010-MsgAction.cs`, and
`LongV6270/src/Long.Kernel/Network/Game/Packets/MsgAction.cs`. The original 5517
client consumes spawn facing at `10014 +0x5A` and action at `+0x5B`.

Only exactly 38-byte actions with zero strings and a nonzero actor ID are parsed.
The actor must match the authenticated generation-fenced world owner. Both kinds
require native 0–7 direction at `10010 +22`; pose 81 additionally requires a
validated 5517-era `EntityAction` value in the low word of `+8`, with the high word
zero. Unsupported directions/poses, forged actor IDs and malformed actions fail
closed. Other client-controlled action fields are normalized before relay.

## Important 5517 subtype 81 role-resolution behavior

Native `CMsgAction__Process` at `0x6FF5B6..0x6FFA24` builds a role-action command
from the low word of `10010 +8` and the direction at `+22`. Alongside the actor
resolved from `+4`, it can independently resolve the DWORD at `+16` as a second
role identity. Although the generic 10010 builder calls `+16` a timestamp, it is
**not safe to replay an untrusted client timestamp for subtype 81**. Our normalized
outgoing pose packet sets `+16` to **zero** (the sentinel for no secondary role),
retains the session-owned actor ID at `+4`, and forwards the validated direction.
This intentionally prevents an authenticated actor from selecting another entity
by supplying their character ID as a timestamp. Direction 79 retains its timestamp
because the verified 79 handler does not use `+16` as a secondary role selector.

This is a server trust-boundary policy based on the client control-flow evidence;
it is not a claim that retail servers emitted identical timestamp values.

## Ordering, fanout and resource protection

Each admitted action is echoed locally and replicated only to currently visible
players by the existing bounded per-session FIFO visibility writers. The per-map
lock protects both presentation-state updates and membership transitions. No
socket write or await occurs under that lock. Future observers receive the
current facing and action in `10014`. Pose 81 updates both action and facing;
successful walking resets action to `STAND` (100), refused walking leaves both
fields unchanged.

Cosmetic requests are capped at eight accepted actions per one-second monotonic
server-clock window **per authenticated session**. When that budget is exhausted,
the existing single session reader waits, cancellation-aware, for its next slot;
no action is silently thrown away, and no per-player background timer, unbounded
queue, or extra fanout task is created. The session read loop therefore applies
normal socket backpressure to excess incoming activity, preserving authoritative
state convergence and packet order. This is an operational resource limit,
**not** a verified retail 5517 emote cadence.

## Scope and production gates

Actions 79 and 81 are handled; other gameplay action subtypes, mounted walking,
jumping, combat, position persistence, NPC replication, and creation defaults remain
out of scope. This slice changes no schema, credentials, realm routing, or walking
speed policy. The older retail 15.10 187 ms movement limiter is not imported.

Unit tests cover wire offsets, ownership-sensitive output normalization, accepted
pose values, and budget exhaustion/retry. End-to-end encrypted transport and
actual connected two-client visual verification remain separate release gates;
passing CI alone does not establish complete native gameplay parity.
