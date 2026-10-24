# Durable character location

## Purpose and scope

The server already has a fixed catalog of historical reborn destinations, and dead/ghost state owned
by the map partition. Neither one moves a character to another map in a way that is safely saved.
The current `characters` schema already stores `map_id`, `position_x`, and `position_y`, so saving a
location needed no migration of its own.

This document describes one database write: a compare-and-swap (CAS) of a character's saved
location. The write alone does not move a character in the live world, update `WorldSession.Map` or
`Ownership`, publish visibility, send packet 86, or enable action 94. A successful commit by itself
never authorizes a map transfer or a revival. The 5517 client defines the packets, and nothing here
adds new protocol behavior.

## The location CAS

`CharacterLocationChange` carries the authenticated account ID, a valid player entity ID, the
previously saved location, and a requested destination that differs from it. It is not an
authorization token. `ICharacterLocationWriteStore.TryCommitAsync` runs one parameterized UPDATE
that must match `account_id`, `character_id`, the old map, X and Y, and the expected
`location_revision`. In one atomic step it changes the three location columns and increments the
revision. The expected revision is required.

- Exactly one row affected: `Committed`.
- Zero rows: `Conflict`. This covers a stale location, a wrong account, and a missing character.
  Never tell a player which one it was.
- Any database exception after the command is sent: the outcome is uncertain. Do not retry blindly.
  Stop the transfer and reconcile with a fresh read from the database, and close the session if
  reconciliation cannot be shown to be safe.
- No-op updates and invalid expected revisions are rejected when the change is constructed. The
  store sets affected-row counting itself rather than relying on connection-string defaults.
- The caller's cancellation is observed only while connecting and setting up, before the command is
  sent. Once it is sent, canceling the caller does not mean the UPDATE was undone. The command has a
  15-second database timeout.
- The store never retries, never holds a transaction open across sockets, and never infers other
  cross-map state changes. The UPDATE is atomic on a single MySQL row.

Coordinates alone cannot detect a move from A to B and back to A (the ABA problem). The revision
check described under [Location revision](#location-revision) closes that gap for this writer, so a
stale request cannot replay old coordinates. It does not grant world ownership or resolve uncertain
writes. The transfer coordinator still needs a protocol that allows one writer at a time and fences
out older generations.

## Location observation

The same opt-in location-writer credential can also read a character's saved location through
`ICharacterLocationObservationStore.ObserveAsync(accountId, characterId)`. It runs one parameterized
single-row read of `map_id`, `position_x`, `position_y` and `location_revision`, filtered by both
`account_id` and `character_id`. It returns null when the character does not exist or belongs to
another account. Invalid player or account IDs are rejected. The read honors the caller's
cancellation. Failures are passed to the caller and are never reported as a missing character. The
writer and the observer share one registered service over the GameServer's Game connection. The
observation added no SQL migrations or network changes.

**An observation cannot settle an uncertain write.** A matching destination could predate the CAS,
come from a different operation, or be read while an UPDATE sent earlier is still in flight. A
location that matches the expected one does not prove that the uncertain update was rolled back
either. Coordinates alone are still not enough. The revision check prevents ABA for this CAS, but a
future coordinator must still allow only one writer, keep a late commit from landing after an
observation, and treat anything that does not match as unresolved. After an uncertain write, keep
the affected session fenced off or disconnected. Never retry blindly, release the owner into another
map, or send a 5517 transition acknowledgement based on an observation alone.

## Portal preflight against the saved location

The internal `WorldPortalPersistedLocationPreflight` combines a portal reservation with one
observation of the character's saved location. The reservation is tied to the session generation and
can be undone. Before doing any work, the preflight checks that the caller's trusted, authenticated
profile matches the world ownership token. It reads the saved location only after the source
partition's FIFO barrier has stopped further movement. If the saved source map does not match, the
row is missing, the read is canceled or fails, or the reservation is revoked, the destination
capacity is given back. Database errors are passed to the caller. The result keeps the saved
location it observed separate from the live snapshot of the source. The preflight is not connected
to live client traffic yet. It never calls `TryCommitAsync` or changes a character's map.

The uncertainty is still unresolved. No observed set of coordinates rules out ABA or proves that a
write already sent cannot complete later. A preflight result does not authorize sending the CAS,
switching world ownership, or sending a 5517 packet. That needs a real transfer coordinator with a
one-writer protocol tied to the session generation, and a durable, monotonic record of each outcome.

## Location revision

Game schema v11 adds a nonnegative signed `BIGINT location_revision` to each character, starting at
zero for existing rows. Every successful CAS by the location writer matches the account
ID, character ID, old coordinates, and expected revision, then changes the three coordinates and
adds one to the revision in the same atomic step. A stale request cannot replay after a move from A
to B and back to A, because its old revision no longer matches. The application rejects
`long.MaxValue` as an expected revision with `ArgumentOutOfRangeException`, so the revision cannot
overflow. The GameServer identity needs column-scoped `UPDATE` on `location_revision` in addition to
the coordinates.

The observation returns a `PersistedCharacterLocation` with the coordinates, the revision, and the
ID of the last successful operation. Internal portal preparation keeps this immutable result but
does not treat it as permission to commit. An observation still cannot settle a commit that is in
flight or prove ownership. The migration cannot run while servers are live: stop all writers, back
up, update the schema, update the GameServer grants, then deploy the v11 GameServer.

Revisions prevent ABA only for writers that use this CAS. Administrative or direct SQL writes must
follow a defined policy for advancing the revision. A write whose outcome is uncertain may still
finish after a later observation. A revision on its own is not a durable commit receipt, does not
make retries safe, and is not a cross-map transaction. Do not retry unknown outcomes or enable map
handoff until the coordinator handles these cases.

## Operation marker

Game schema v12 adds a nullable `last_location_operation_id BINARY(16)` to each character. The CAS
requires a nonempty operation UUID generated by the server, written in the same atomic step as the
new coordinates and the next revision. It is stored in the 16-byte big-endian UUID form, matching
`MySqlGuidFormat.Binary16`. The restricted writer converts the bytes itself, whatever GUID settings
its caller's connection uses. The observation returns the UUID with the coordinates and revision. If
the UUID, `expected_revision + 1`, and the destination all match, the operation committed. A
different marker, no marker, a failed read, or an unchanged revision must never be taken to mean
that the uncertain operation will not commit later. Blind retries, reusing an operation ID, and
overlapping writers from different generations are not allowed.

The marker is not an operation log, a distributed transaction, or a durable single-writer lease. It
records only the last successful CAS, and later writes replace it. A future coordinator must block
new writes for a character while any outcome is uncertain. It must also handle process crashes and
races between commands already sent before it restores ownership, resumes gameplay, or acknowledges
a transfer to the 5517 client. The writer still does not run during world transfer, and map
transfers for clients remain disabled.

## Writer token

Game v13 adds a nullable `location_writer_token BINARY(16)`. Before its first CAS, the restricted
location writer replaces the token with one InnoDB row UPDATE. Competing CAS and token updates
serialize on the same row, so an older command that reaches the row after the token changed no
longer matches it. After replacing the token, the writer reads the coordinates, revision, and
operation marker, and takes write permission only if the row still holds its own token. Every later
CAS requires that token, and every check that a commit happened also checks it. Matching coordinates
alone are not enough.

Another writer can replace the token at any time, which revokes the earlier permission. The token
fences database writes for one character. It is not a realm-wide distributed lock, a writer
credential, a reconnect check, or a transaction that spans MySQL and in-memory world state. When
live sessions start using it, session admission must replace the token, or otherwise prove it holds
it, before accepting a character profile, and must read the saved location again. The internal
coordinator that runs without a session replaces its token only after the runtime is sealed, and
stops the world if the saved source location changed between the preflight and the token change.
This alone is not enough to accept action 85 from clients or to claim world ownership that survives
a crash.

## Database credential

`AddGameCharacterLocationWrite()` uses the GameServer's Game connection. Its identity may update only
the six location and fence columns of `characters`; every other character column stays read-only, and
it has no `DELETE`. The CAS WHERE clause reads the identity and location columns, which the
identity's table-level `SELECT` on `characters` covers. The grants are in
[Database identities](../operations/database-identities.md#gameserver-runtime-per-realm).

## Tests

- A committed change updates only the location of the expected account's character.
- A stale expected location or another account cannot overwrite a committed position.
- Two writers competing from the same old map, X and Y get exactly one `Committed` and one
  `Conflict`.
- The GameServer runtime identity can run the CAS and cannot update any other character column.
- An identity without the write grants fails the startup probe and cannot run the update.
- Cancellation before the command is sent never updates a character.
- The full EF model check passes with the Game schema migrations through v13.

## Before cross-map transfer can work

1. A transfer coordinator must hold the session lease, make sure no active commands can commit
   during the switch, reserve capacity at the destination, and prove the destination is valid and
   available.
2. It must define a safe order for the saved location write and the in-memory handoff. Review
   ownership by both the old and new partitions, the failure paths, and restart and reconnect
   behavior. A simple remove-then-admit sequence is not safe.
3. Once transfers are enabled, world residency, `WorldSession` and visibility must stop assuming
   that `Map` and `Ownership` never change for the life of a session. Keep generation fencing across
   moves.
4. A canceled, congested, or dead network peer must not leave reserved capacity stuck or a character
   visible twice. The caller must reconcile uncertain database writes.
5. Packet `10010/86`, the map-enter sequence, and loading an existing character on reconnect must
   match the 5517 client, with integration tests that use two clients.
6. HP, mana and stamina restoration, Heaven Blessing, Soul Shackle, and action `10010/94` belong to
   revival, which has its own lifecycle and is separate work. This store does not provide them.

The Game schema v12 and v13 migrations add only the last-operation marker and the writer token.
They add no SQL tables, client packet formats, or live gameplay paths.
