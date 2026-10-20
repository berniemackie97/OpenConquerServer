# Turn and pose actions (5517)

On an authenticated world session, `MsgAction` (`10010`) subtypes 79 (facing) and 81 (pose) change
how other players see a character without changing its world position. The packet layouts are
confirmed by `20_notes/Network/01_core_packets.md`,
`FoxConquer5517/MsgServer/Network/GameServer/Handlers/10010-MsgAction.cs` and
`LongV6270/src/Long.Kernel/Network/Game/Packets/MsgAction.cs`. The original 5517 client reads spawn
facing from `10014 +0x5A` and the action from `+0x5B`.

Only 38-byte actions with no strings and a nonzero actor ID are parsed. The actor must be the
session's authenticated world owner for the current admission generation. Both subtypes require a
direction of 0-7 at `10010 +22`. Pose 81 also requires a known 5517-era `EntityAction` value in
the low word of `+8`, with the high word zero. Unsupported directions or poses, forged actor IDs and
malformed actions close the session. The server normalizes the other client-controlled action
fields before relaying them.

## Subtype 81 role resolution in the 5517 client

In the 5517 client, `CMsgAction__Process` at `0x6FF5B6..0x6FFA24` builds a role-action command from
the low word of `10010 +8` and the direction at `+22`. Besides the actor resolved from `+4`, it can
separately resolve the DWORD at `+16` as a second role. The generic 10010 builder calls `+16` a
timestamp, but relaying an untrusted client timestamp for subtype 81 is not safe: a player could
put another character's ID there and make clients select that entity. The outgoing pose packet
therefore sets `+16` to zero (meaning no secondary role), keeps the session's actor ID at `+4` and
forwards the checked direction. Facing action 79 keeps its timestamp because the client's 79
handler does not use `+16` to select a second role.

This is a server-side safety rule based on the client's control flow. It does not claim that retail
servers sent the same timestamp values.

## Ordering, fan-out and rate limits

Each accepted action is echoed to the sender and relayed only to players who can currently see the
character, through the existing size-limited per-session FIFO visibility writers. The per-map lock
covers both presentation-state updates and membership changes. Nothing writes to a socket or awaits
while holding it. Players who come into view later receive the current facing and action in
`10014`. Pose 81 updates both action and facing. A successful walk resets the action to `STAND`
(100), and a refused walk leaves both fields unchanged.

Each authenticated session may have at most eight of these cosmetic actions accepted per one-second
window, measured on the server's monotonic clock. When the budget runs out, the session's single
reader waits for its next slot (the wait honors cancellation). No action is silently dropped, and
there is no per-player background timer, unbounded queue or extra fan-out task. Excess input
therefore backs up through normal socket backpressure, and server state and packet order stay
consistent. The limit protects server resources; it is not a confirmed retail 5517 emote rate.

## Scope

Only actions 79 and 81 are handled. Other gameplay action subtypes, mounted walking, jumping,
combat, position persistence, NPC replication and creation defaults are out of scope here. This
feature does not change the schema, credentials, realm routing or walking speed policy. The older
retail 15.10 187 ms movement limiter is not used.

## Testing

Unit tests cover wire offsets, output normalization that depends on ownership, accepted pose
values, and budget exhaustion and retry. End-to-end tests over encrypted transport and a visual
check with two connected clients are still needed before release. Passing CI alone does not show
that gameplay matches the retail client.
