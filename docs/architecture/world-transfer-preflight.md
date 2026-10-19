# World transfer preflight — slice #153

## Scope and authority

This slice adds **reversible source-quiescence and destination population reservation** to
`WorldRuntime`. It does not transfer a character. There is no new client input, native
packet handling, database write, 5517 map transition, revival, or visibility handoff.
`TryReserveTransferAsync` is an **internal** trusted-world API and accepts only an exact
runtime ID / character ID / map ID / ownership-generation token. It cannot be invoked by
a client frame. This is a deliberately limited operational prerequisite to the eventual
cross-map coordinator; do not mistake it for a completed transfer.

## Sequence and concurrency contract

1. Acquire `_gate` shared with admission, removal, and state command enqueue.
2. Reject a stale/removing source owner, a duplicate preflight, a same-map target,
   an unavailable target partition, a blocked/out-of-bounds target tile, or a full
   destination partition. The target position is supplied by a **trusted caller**;
   preflight does not confer permission to select arbitrary gameplay destinations.
3. Reserve one destination population slot **before** releasing `_gate`. Global
   resident count is unchanged because the same resident will move, not duplicate.
4. Install the owner-scoped lease and enqueue one read-only source-partition FIFO
   barrier while still holding `_gate`. The barrier runs after older accepted ordinary
   source commands. Subsequent `TryWalkCharacterAsync`, `TryJumpCharacterAsync`,
   `TryEnterGhostAsync`, and `TryPublishAvatarActionAsync` are denied while the lease
   is present; `ReadCharacterAsync` remains available. No blocking work executes
   inside a partition worker or the global ownership lock.
5. Await the barrier for the configured bounded preflight timeout (10 seconds by
   default, configurable to at most 30 seconds). If it times out, throw
   `TimeoutException` and immediately refund destination capacity and reopen
   source command admission. Timeout does not claim a stuck partition worker
   has resumed; the barrier is read-only if it eventually executes.
6. Return the barrier's authoritative source snapshot only after execution completes.
   If the character was removed, the partition stopped, the caller canceled, or the
   lease lost authority during that interval, cancel/refund the reservation.
7. `Dispose()` idempotently releases the target slot and reopens source mutators
   **only if** the original ownership generation still exists. Session removal
   refunds the destination slot too; disposing an orphan lease cannot refund a
   replacement character's capacity. Shutdown invalidates all outstanding leases.

The special reserved-removal lane is intentionally not fenced: session revocation and
server teardown must always be able to release a character. Removal may overtake the
ordinary barrier; in that case preflight resolves unavailable and cannot commit.

## Invariants and deliberate boundaries

- All reservations are bounded by the existing per-map population limit. No global
  slot is added; one source owner may hold at most one destination reservation.
- Normal admission counts reserved destinations as occupied. There is no optimistic
  overbooking or separate unbounded waiting queue.
- No state change occurs on the target map, so no target visibility session is
  introduced and the character cannot appear in two maps.
- The generation-fenced lease is temporary; `IsActive` reflects removal/shutdown
  but is **not** an irreversible commit capability. The eventual coordinator must
  recheck while performing its atomic ownership cutover.
- `SourceSnapshot` is the latest map-partition snapshot after the source barrier,
  not proof of the last persisted location. #152's CAS expects the *persisted*
  location; normal movement is not yet durably saved.
- A barrier rejection, cancellation, or shutdown releases the capacity reservation.
  An outstanding lease holds its destination slot until explicitly disposed or the
  originating owner is removed. Future callers **must** dispose in `finally`.
- Existing chat, whisper, raw packet handling, and the socket remain live in this
  preflight-only slice. The later coordinator must define the quiescence boundary
  for all game commands and exact native packet order before enabling transfers.
- No locks are held across `await`; the partition command performs a read only.
  No DB or socket I/O is introduced into map workers.

## Offline partition-residency handoff (#161)

The runtime can now consume a completed reversible reservation for a character with **no active
world session** and relocate its partition-owned snapshot. Destination admission retains alive,
ghost, status and death timestamp; a generation rollover revokes the old map identity.
Destination capacity is consumed exactly once by the existing reservation, and successful
cutover refunds the source slot. The destination is staged before source removal; no new
ownership capability becomes public until the source command completes. A failure before departure submission removes staged destination state before releasing
the source fence. Once source departure submission is attempted, any failure terminates world
execution: an error cannot prove that the source occupant remains present, so the runtime
must not reopen source commands or publish an unverified owner.

This API is internal and is **not** a live portal transfer. It deliberately rejects tracked
world sessions because `WorldSession.Map`, `WorldSession.Ownership`, visibility state, and
network bootstrap currently bind to a lifetime-immutable map. The operation does not write
MySQL, consume an observed commit marker, send client packets, or promise crash-safe durable
handoff. A real portal coordinator must add durable fencing/recovery and the native wire
sequence before it can invoke this relocation for connected characters.

## Unbound portal transaction (#162)

`WorldUnboundPortalTransferCoordinator` links authenticated portal preflight and persisted
source observation to an exclusive, generation-scoped runtime commit seal. Once sealed,
new sessions, direct removals, and reservation disposal cannot reopen source mutations.
The coordinator first atomically rotates the character-row writer generation and verifies
the fresh post-fence source is unchanged. A prior CAS must either complete before rotation
or fail its generation predicate afterwards. It then submits one revision-qualified CAS
with a fresh operation ID and the acquired generation, rereads
identity-scoped persisted state, requires **exact** operation/revision/destination evidence,
and consumes the reserved destination through the #161 partition handoff. Its successful
result is a new generation owner with a matching saved destination. No socket or native
client packet is produced.

The sealed phase intentionally has only two outcomes: **confirmed relocation**, or
**terminal world shutdown**. Write conflicts, database exceptions, read timeouts,
negative/missing operation evidence, caller cancellation after sealing, and failed world
cutover all stop this runtime. A different marker or an unchanged revision cannot prove
that a prior SQL command cannot still complete. A bounded adapter call may outlive its
caller after timeout; shutting down the runtime prevents further in-process admission,
not late database completion or crash-safe cross-process fencing.

This workflow is internal and accepts **only unbound characters**. It is not registered
with live gameplay ingress, and it does not establish a durable cross-process lease,
recovery journal, world-session rebinding, visibility cutover, or verified native 5517
map-transition acknowledgement. A restarted realm must still be protected from a late
pre-crash CAS before real transfers may be enabled. No active-session movement is claimed.

## Next mandatory slice — operational map transfer

A subsequent PR must implement a cutover state machine that obtains a preflight lease
and also handles (a) durable-location CAS and read-after-unknown-outcome reconciliation,
(b) map and ownership generation handoff, (c) destination-partition insertion plus
failure rollback, (d) visibility departure/arrival without double appearance,
(e) `WorldSession` immutable-map assumptions and movement reconciliation reset, and
(f) the verified 5517 action-86/enter-map sequence. The safe order between MySQL CAS
and inter-partition state ownership must be proved under crashes and cancellation;
**there is no distributed transaction across MySQL, map workers, and a TCP socket**.

Only after those steps may native action 94 revival consume the vetted historical
reborn destinations (#151). Revival also needs authoritative resource/status lifecycles
and verified 5517 response order. None is asserted operational here.

## Test gates

- The lease captures the source-map snapshot and fences source positional/avatar
  commands, without modifying coordinates or database contents.
- A target reservation consumes per-map capacity but not global resident capacity.
- Duplicate, stale, same-map, missing-map, and invalid-tile reservations fail closed.
- Disposal is idempotent and restores source command acceptance.
- Removal revokes authority and refunds exactly one destination slot, without
  refunding capacity reserved by an unrelated new resident.
- Pre-cancellation and runtime shutdown cannot leave an active lease.
- A stalled barrier times out and releases capacity and source mutation fencing.
- Barrier-ordering and global capacity tests use controlled partition blocking and a third map.

The full .NET release build, formatter, EF model checks, unit/integration tests, and
CI must pass before merge. Live two-client transfer testing is intentionally a future
operational transfer gate, not claimed by this slice.
