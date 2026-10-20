# In-world chat (5517)

The 5517 `Conquer.exe` chat send path and `CMsgTalk__Process` are documented in
`20_notes/Server/chat.md` (verified 2026-08-28) and `20_notes/Network/04_social_packets.md`. On the
authenticated world session, the server implements the operations those sources confirm: local
chat, whispers to a character, and role words. FoxConquer5517 was used to check how 5517-era
channels are classified; its implementation and trust model are not copied.

## Wire format and authorization

`MsgTalk` (1004) has a fixed 24-byte header and body followed by four length-prefixed ANSI strings:
sender, recipient, suffix and text. The client's receive buffers hold at most 15 encoded bytes for
each name and 255 for the text. An inbound packet must have four strings, correct framing, no
embedded NUL and no trailing bytes. CR and LF are replaced with spaces. The server ignores the
sender, identity, color, style and suffix supplied by the client. Outgoing packets always use the
session's authenticated ID and stored character name, white color, style zero and an empty suffix.

Supported operations:

- `0x7D0` and `0x7D7`: local speech with the literal recipient `ALL`. It is echoed to the author and
  relayed only to authenticated players on the same map who can currently see the author. On
  receipt, the client rewrites the legacy `0x7D0` to `0x7D7`.
- `0x7D2`: a whisper, delivered when the recipient names a character online in this GameServer
  realm. Delivery uses the recipient's stored spelling so the client's exact name comparison
  matches. There is no cross-realm delivery and no made-up "offline" reply. The normal chat input
  sends `ALL` even when a tab is selected; a whisper with that recipient is ignored rather than
  broadcast.
- `0x836` / `0x838`: temporary role-word state (recipient `ALL`). Up to 64 bytes are forwarded. The
  latest role word is relayed to current observers, and players who come into view later receive
  it after the speaker's spawn packet. It is discarded when the session leaves the map and is never
  saved.

Well-formed messages on other channels are recognized but neither relayed nor echoed. Permission to
use team, guild, global, service, staff and other restricted channels cannot be taken from the tab
the client selected. In particular, an authenticated player cannot forge the startup/control
channels `0x834` and `0x835` or the system channel `0x7D5`. Channel support is not complete.

## Ownership and rate limits

Before publishing, the world visibility service checks that the sending session is the one
currently registered for the map. Whispers find their recipient through a case-insensitive,
in-process index of online names, and each recipient is checked again against its current
registration before delivery. Registering a name that is already in the index fails. When a session
leaves, its entry is removed only if it still points to that session, so cleanup of a stale
session cannot delete a newer registration. Nothing writes to a socket or awaits under the map
lock. The existing size-limited peer FIFO disconnects slow consumers.

Each session accepts up to four correctly framed chat messages per one-second window. Messages on
unsupported channels and whispers addressed to `ALL` count too: messages are counted on arrival,
before routing or rejection, so ignored traffic cannot get around the limit. When the budget is
used up, the session's single reader waits (honoring cancellation) instead of dropping messages or
growing an unbounded queue. The limit protects the server; it says nothing about how fast retail
chat was.

## Testing

Repeatable multiplayer tests run real admitted world sessions over fake transports with negotiated
encryption. They cover local visibility and map isolation, cross-map whispers, offline, stale and
duplicate names, role words for late observers and spawn ordering, rate limiting on unsupported
channels, and disconnecting slow recipients. Checking the result in two retail clients is still a
separate step before release. No database migration, new secret or runtime configuration change is
required.

## Not implemented

Party, guild and world routing, chat commands, staff privileges, cross-realm chat, chat history,
censorship rules and offline notifications are not implemented. Before a public release, full
encrypted tests with two clients still need to confirm how local speech, whispers, role words and
late-observer ordering are displayed.
