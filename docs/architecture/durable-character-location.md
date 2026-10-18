# Durable character location — slice #152

## Purpose and evidence boundary

PR #151 established an immutable historical reborn-destination catalog; #150 established
partition-owned dead/ghost state. Neither provides a safe durable map transfer. The current
`characters` schema already stores `map_id`, `position_x`, and `position_y`; no migration is
necessary to create a persistent location write boundary.

This slice implements **one database commit primitive only**. It does **not** move an active
world resident, update `WorldSession.Map` or `Ownership`, publish visibility, send native 86,
or enable action 94. Callers must never interpret database commit alone as an authorized
map transfer or resurrection. The native 5517 client is the packet authority; no new
protocol semantics are asserted here.

## Commit contract

`CharacterLocationChange` carries the authenticated account ID, valid player entity ID,
previous persisted location, and a distinct requested destination. It is deliberately
**not** an authorization token. `ICharacterLocationWriteStore.TryCommitAsync` issues a single
parameterized atomic UPDATE, matching **all** of `account_id`, `character_id`, old map,
old X, and old Y, and changing only `map_id`, `position_x`, `position_y`.

- Exactly one row affected -> `Committed`.
- Zero rows -> `Conflict`, including stale location, wrong account, or missing character;
  never reveal which to a player.
- Any database exception after dispatch -> **uncertain outcome**. Do not retry blindly;
  halt the transfer and reconcile via a fresh authoritative read, closing the session if
  reconciliation is not provably safe.
- No-op updates are rejected at the model boundary; affected-row semantics are explicitly
  configured, not left to connection-string defaults.
- Only the pre-dispatch connection and setup phase observes external cancellation.
  Once dispatched, canceling the caller does not establish that the UPDATE was undone.
  The command has a bounded database timeout.
- The store does no automatic retries, transactions spanning sockets, or inferred
  cross-map state changes. The UPDATE is itself atomic on one MySQL row.

**Important ABA limitation:** matching `(map,x,y)` is not a monotonic revision. A character
could eventually return to exactly the same triple. Until the coordinator introduces a
proper generation-bound transfer lease or monotonic persistence version, the CAS store
must be callable only from the generation-fenced, single-writer world-transfer coordinator.
This slice does NOT make arbitrary concurrent writers or replayed stale sessions safe.

## Post-#152: bounded location observation (#156)

The opt-in, dedicated location-writer credential also exposes an **identity-bound location
observation** through `ICharacterLocationObservationStore.ObserveAsync(accountId, characterId)`.
It issues one bounded, parameterized read of only `map_id`, `position_x`, and `position_y`,
restricted by both `account_id` and `character_id`, returning null for either missing or
foreign identities. Invalid player/account identities are rejected. The read honors caller
cancellation; failures propagate and are never rewritten as an absent character. The writer
and observer share one registered service/credential, and the GameServer's default read-only
identity receives no new grants. There are no SQL migrations or network changes.

**Observation is not reconciliation authority.** A matching destination could predate the
CAS, arise from a different operation, or be observed while a previously dispatched update
is still in flight. Even a matching expected location cannot prove the uncertain update was
rolled back. The ABA limitation remains. The future coordinator must hold a generation-fenced
single-writer lease, prevent late commits from outrunning its observations, and define a
monotonic revision or other provable handoff protocol before treating a read as resolution.
On an uncertain write, keep the affected session fenced/disconnected; never blindly retry,
release the owner into another map, or send a 5517 transition acknowledgement based on an
observation alone.

## World-side persisted-location preflight (#157)

The internal `WorldPortalPersistedLocationPreflight` composes a generation-bound,
reversible native portal reservation with one identity-scoped observation. The caller's
trusted authenticated profile must match the world ownership token before any work;
observations only occur after the source FIFO barrier has fenced movement. A persisted
source map mismatch, absent row, canceled read, failed read or revoked reservation refunds
the destination capacity; database errors propagate. The result keeps the observed
persisted expectation distinct from the live source snapshot. It is not yet connected
to live ingress, and it never calls `TryCommitAsync` or changes a character's map.

**Uncertainty remains unresolved:** no observed coordinate tuple eliminates ABA or proves
that an already-dispatched write cannot complete later. Preparing a result cannot authorize
CAS dispatch, world ownership cutover, or a 5517 packet until a true transfer coordinator
has a generation-bound single-writer protocol and a durable monotonic outcome boundary.

## Credential boundary

The existing `AddGamePersistence()` / ordinary runtime identity is **read-only** and must
remain unchanged. The opt-in `AddGameCharacterLocationWrite()` registration requires an
entirely separate MySQL principal provisioned by an operator. Do not reuse the character
creation principal (INSERT only), runtime reader, or administrative schema owner.

Provision the dedicated identity using the deployment's approved secret-management process.
After its account exists, the minimum table permission is column-scoped. The CAS predicate reads all five location and identity columns. MySQL 8.4
requires `SELECT` on every referenced predicate column, including the location
columns being updated, in addition to column-scoped `UPDATE`:

```sql
GRANT UPDATE (`map_id`, `position_x`, `position_y`),
      SELECT (`character_id`, `account_id`, `map_id`, `position_x`, `position_y`)
ON `<game_database>`.`characters`
TO '<location_writer>'@'<restricted_host>';
```

The exact principal/database names and host pattern are operator-specific. Do not paste
placeholders into an actual migration, elevate the default GameServer principal, or embed
production credentials in the repository. The database schema, grants, and connectivity
for this new principal require an operations review before runtime registration.

## Regression gates

- One committed transition updates only the expected account/character location.
- A stale expected location or foreign account cannot overwrite a committed position.
- Competing writers for one old triple yield exactly one `Committed` and one `Conflict`.
- The restricted location-writer principal successfully executes the CAS; it cannot update
  other character columns or read unrelated fields.
- The existing read-only runtime principal cannot issue the update.
- Pre-dispatch cancellation never updates a character.
- The registration is explicit and does not grant writes to default persistence services.
- Full EF model verification passes with **no migrations**.

## Before cross-map transfer is operational

1. A transfer coordinator must hold the authoritative session lease, ensure no active
   commands can commit during the cutover, reserve destination capacity, and prove the
   destination is valid and available.
2. It must define safe order between the durable location write and in-memory handoff.
   Both the old and new partition ownerships, failure paths, and restart/reconnect
   behavior must be reviewed; **do not** attempt a simple remove-then-admit sequence.
3. World residency and `WorldSession`/visibility must stop treating `Map`/`Ownership`
   as lifetime-immutable once transfers are enabled. Keep generation fencing across moves.
4. A canceled, congested, or dead network peer must not strand reserved population or
   leave a character visible twice. The caller must reconcile uncertain database writes.
5. Packet `10010/86`, map-enter sequencing, and existing-character hydration on reconnect
   must follow the verified 5517 client, with two-client integration tests.
6. HP/mana/stamina restoration, Heaven Blessing, Soul Shackle, and action `10010/94`
   remain a separate authoritative revival lifecycle; they are **not** implied by this store.

No new SQL tables, EF migrations, client packet formats, or gameplay paths are introduced.
