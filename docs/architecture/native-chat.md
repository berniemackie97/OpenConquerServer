# Native 5517 in-world speech — Slice #147

The original 5517 `Conquer.exe` chat send path and `CMsgTalk__Process` are documented
in `20_notes/Server/chat.md` (verified 2026-08-28) and
`20_notes/Network/04_social_packets.md`. This slice implements the evidence-backed
**local chat, character whisper, and role-word** operations on the existing
authenticated world session. FoxConquer5517 provides supporting 5517-era channel
classification, not an implementation or trust model to copy.

## Wire and authorization

`MsgTalk` (1004) contains a fixed 24-byte header/body followed by four length-prefixed
ANSI strings: sender, recipient, suffix and text. Client destination buffers permit
at most 15 encoded bytes for each name and 255 for the text. Inbound packets require
exactly four entries, exact framing, no embedded NUL, and no trailing bytes. CR/LF is
normalized to spaces. The client's sender, identity, colour, style, and suffix are
**not** authorities; outgoing packets always use the session's authenticated ID and
canonical character name, fixed white colour, zero style, and empty suffix.

Supported in-world operations:

- `0x7D0` and `0x7D7`: local speech with literal recipient `ALL`, echoed to the
  author and relayed only to authenticated currently visible players in the same map.
  The client's receiver rewrites legacy `0x7D0` to `0x7D7` on receipt.
- `0x7D2`: a directed whisper when the recipient names an online character in
  this GameServer realm. Delivery uses the recipient's canonical spelling so
  client-side exact name comparison succeeds. No cross-realm messaging or
  invented "offline" response. The ordinary chat edit sends `ALL` even on
  selected tabs; such an unaddressed whisper is ignored, not broadcast.
- `0x836` / `0x838`: transient role-word state (recipient `ALL`). Up to 64 bytes
  are forwarded; the bounded last word is relayed to current observers and
  supplied **after the corresponding player spawn** to newly entering observers.
  It is discarded when the session leaves the map and is not persisted.

Well-formed chat in other channels is recognized but **not** relayed or echoed:
team, guild, global, service, staff and other channel-specific authorization
must not be inferred from the client's selected tab. In particular the native
startup/control `0x834` and `0x835` and system presentation `0x7D5` cannot be
forged by an authenticated player. This does not claim complete channel support.

## Ownership and resource safety

The world visibility service checks the exact session registered for the map
before publishing. Directed lookup is through a case-insensitive, in-process
online-name index, but every recipient is revalidated against its current
registration before delivery. Name registration fails closed on collisions, and
session leave removes the mapping by **identity**, preventing stale-session cleanup
from deleting a newer registration. No sockets or awaits occur under the map
lock. The existing bounded peer FIFO disconnects slow consumers.

Each session accepts up to four correctly framed chat messages per one-second
accounting window, **including unsupported channels and unaddressed whispers**.
This ingress accounting happens before routing or rejection so ignored traffic
cannot bypass the budget. When exhausted, the serialized session reader delays
with cancellation instead of losing messages or growing an unbounded queue.
This is a protective operational bound, not a claim about retail chat cadence.

Deterministic multiplayer tests exercise actual admitted world sessions over
negotiated encrypted fake transports, covering local visibility and map isolation,
cross-map whispers, offline/stale/duplicate name handling, late-observer role
words and spawn ordering, unsupported-channel pacing, and slow-recipient teardown.
Retail two-client presentation verification remains a separate release gate. No database migration,
new secret, or runtime configuration change is required.

## Deliberate release gates

This slice does not invent party/guild/world routing, chat commands, staff
privileges, cross-realm chat, chat history, censorship rules or offline
notifications. Full encrypted two-client testing is still required to establish
rendered local speech, whispers, role words and late-observer ordering before
public gameplay release.
