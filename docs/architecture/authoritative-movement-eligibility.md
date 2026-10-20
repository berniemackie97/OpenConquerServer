# Movement eligibility

## Scope and sources

This document covers two movement checks for logged-in 5517 clients: whether a dead character may
move, and whether a character may use riding movement. The main reference is
`20_notes/Server/movement-eligibility-5517.md`, taken from the original 5517 `Conquer.exe`. In the
retail client, status `5` means dead, status `0xA` means ghost, and status `0x32` means riding. A
riding client sends `10005` with full-width selector `9` while its vigor is above 1. A living client
without ride status uses ordinary eight-direction movement. The client does not let a dead character
walk unless it is a ghost. The original TQ 15.10 server binaries are not a reference for retail 5517
behavior, and they have no mounted gameplay.

## What the server checks

The world already ties each character to its authenticated account, loads the character's saved life
state at admission, runs each map partition exclusively, frames 5517 movement packets, paces
movement by the server clock, and delivers packets to the client and its peers through a
size-limited FIFO. The server owns the primary dead and ghost status. It does not yet track whether
a character is riding, and nothing writes vigor (packet `1033` mode 2). So it cannot accept
selector-9 movement just because the client sent that packet format. `WorldRuntime` rejects a
selector-9 request at its entry point, which is fenced by session generation, before any movement
command is queued. The 24-direction tables stay in `MapInstance.TryWalk`, which computes the map
displacement, and unit tests cover them. The runtime is the only movement entry point a client can
reach. Any future 24-direction authorization must be added there and must use state the server owns,
never flags supplied by the client.

`MapInstance.TryWalk` accepts a living character, or a corpse whose ghost flag the server has set.
It refuses any other dead character. Both status bits (dead is index 5, ghost is index 10) live in a
128-bit primary status mask, the same width the client uses, on the snapshot the partition owns. A
dead character without the ghost flag cannot move. A dead character with the ghost flag can only use
selector 0, ordinary walking. Ground jumping still requires the character to be alive. The HP value
at login only decides whether the character starts alive or dead. Movement packets never set status.

The client sends request 145 after a 3-second delay. The server accepts it once a corpse-delay check
against its monotonic clock passes, and a repeated request has no further effect. The ghost status
is committed in the same exclusive partition turn. The local client receives the initial dead mask
after the login reset. The transition then sends 10017 attribute 25 (the full 128-bit mask),
followed by attribute 11 (lookface family 98/99), to the local client and current observers. For
observers who arrive later, the 10014 spawn includes the status mask and the corpse lying pose.
Future death, combat, and status systems must go through this same owner and serialization path.
There is no way for a client to write status.

A refused command uses the existing `10010/108` correction. The client moves to its predicted tile
immediately, so silently dropping a walk is not enough. The existing per-session correction budget,
replay guard, and visibility FIFO all apply. This adds no queues, timers, database fields, packet
variants, or cross-thread mutation.

## Tests

- Map tests reproduce all 24 displacement vectors in isolation. The runtime that clients reach
  rejects selector 9 and leaves the tile unchanged.
- An allowed ordinary ground walk still commits, is echoed to the player, and updates who can see
  whom, in partition order.
- A corpse cannot move. A dead character with the ghost flag can walk with the ordinary selector.
  Neither can jump or use mounted movement, which is not authorized.
- The encrypted world-session test checks that a selector-9 walk, which is not authorized, gets
  correction 108 and is never echoed or sent to other clients.
- The stale-generation checks and the existing ground movement and visibility tests still pass.

## World-session handling and corpse pose

The 5517 client sends 1056 mode 2 when the dead status is added, including when status is loaded
after login. The session accepts only the 12-byte trade-quit form of 1056 and checks its entity ID.
There is no server trade state yet, so accepting this notification changes nothing (no inventory,
currency, partner, or account changes) and the server sends no reply. Repeats are harmless. Other
1056 modes and malformed layouts are not supported and close the session. Bytes +10 and +11 are not
interpreted, and the server does not require them to be zero.

A corpse cannot change its facing or standing pose. The check and the relay to observers run in the
same serialized map-partition turn as the ghost transition, so a corpse pose cannot be cached and
shown to observers who arrive later. Ghosts and living characters keep the existing avatar actions.

## Not yet implemented

- Server-side changes to primary status beyond dead and ghost, including blessing, freeze, stun,
  fly, and secondary status details (2075).
- Death from combat, self and spell revival, freeze, stun, flustered auto-wander, transformation,
  flying, status-dependent pacing, and event-driven updates to living occupancy.
- Server-side mount checks (equipment and skill), ride flag `0x32`, current and max vigor, vigor
  cost and recovery rules, the 1033 mode 2 update, accepting selector 9, and charge handling.
- End-to-end tests with two clients for 5517 status effects and for correction and animation
  behavior. Server implementations for later client versions (Long, Canyon, Fox) can inform server
  mechanics, but they are not a source for the retail 5517 packet layout.

What is implemented is the part of the corpse-to-ghost transition that a client can reach through
movement. Treating a character who logs in with zero HP as dead is a compatibility choice of this
server. How the retail 5517 server handled reconnects is not known. The wire fields and the ghost
walking check are confirmed from the client. The 3-second delay enforced by the server and the
lifecycle that starts from saved zero HP are this server's own defensive choices, not recovered
retail behavior.
