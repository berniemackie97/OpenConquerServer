# World transfer preflight

## Scope

`WorldRuntime` can pause commands for a character on its source map and reserve a population slot
on the destination map, and both steps can be undone. This does not transfer the character. It adds
no client input, packet handling, database write, 5517 map transition, revival or visibility
handoff. `TryReserveTransferAsync` is an internal API for trusted world code. It accepts only a
token whose runtime ID, character ID, map ID and ownership generation all match, and no client
frame can invoke it. It is one prerequisite for the future cross-map coordinator, not a complete
transfer.

## Sequence and concurrency

1. Take `_gate`, the lock also used by admission, removal and state command enqueue.
2. Reject a stale or departing source owner, a duplicate preflight, a target on the same map, an
   unavailable target partition, a blocked or out-of-bounds target tile, or a full destination
   partition. A trusted caller supplies the target position. Passing preflight does not give
   permission to pick arbitrary gameplay destinations.
3. Reserve one destination population slot before releasing `_gate`. The global resident count
   does not change, because the same resident will move rather than be duplicated.
4. While still holding `_gate`, install a lease scoped to the owner and enqueue one read-only FIFO
   barrier on the source partition. The barrier runs after older ordinary commands already
   accepted on the source. While the lease exists, later `TryWalkCharacterAsync`,
   `TryJumpCharacterAsync`, `TryEnterGhostAsync`, and `TryPublishAvatarActionAsync` calls are
   denied. `ReadCharacterAsync` still works. No blocking work runs inside a partition worker or
   under the global ownership lock.
5. Wait for the barrier up to the configured preflight timeout (10 seconds by default, at most 30
   seconds). On timeout, throw `TimeoutException`, refund the destination capacity immediately and
   accept source commands again. A timeout does not mean a stuck partition worker has recovered. If
   the barrier runs later, it only reads.
6. Return the barrier's source snapshot only after the barrier has run. If in the meantime the
   character was removed, the partition stopped, the caller canceled or the lease became invalid,
   cancel and refund the reservation.
7. `Dispose()` is safe to call more than once. It releases the target slot and reopens source
   commands only if the original ownership generation still exists. Removing the session also
   refunds the destination slot, and disposing an orphaned lease cannot refund capacity held by a
   replacement character. Shutdown invalidates all outstanding leases.

The reserved removal lane is not blocked by the lease, because session revocation and server
teardown must always be able to release a character. A removal may overtake the ordinary barrier.
In that case the preflight returns unavailable and cannot commit.

## Properties and limits

- All reservations count against the existing per-map population limit. No global slot is added,
  and one source owner can hold at most one destination reservation.
- Normal admission counts reserved destination slots as occupied. There is no overbooking and no
  separate unbounded waiting queue.
- Nothing changes on the target map, so there is no visibility session there and the character
  cannot appear on two maps.
- The lease is temporary and tied to the ownership generation. `IsActive` turns false on removal or
  shutdown, but an active lease is not permission for a commit that cannot be undone. The future
  coordinator must check again while it performs the atomic ownership cutover.
- `SourceSnapshot` is the latest map-partition snapshot after the source barrier. It does not show
  the last saved location. The CAS in [Durable character location](durable-character-location.md)
  expects the saved location, and normal movement is not saved to the database yet.
- A barrier rejection, cancellation or shutdown releases the capacity reservation. An outstanding
  lease holds its destination slot until it is disposed or its owner is removed. Callers must
  dispose it in `finally`.
- Chat, whispers, raw packet handling and the socket keep working during preflight. Before
  transfers are enabled, the coordinator must decide which game commands pause during a transfer
  and settle the 5517 packet order.
- No lock is held across an `await`, and the partition command only reads. Map workers do no
  database or socket I/O.

## Offline partition-residency handoff

The runtime can use a completed reversible reservation to move the partition snapshot of a
character that has no active world session. Destination admission keeps the alive and ghost state,
status and death timestamp, and the generation rollover revokes the old map identity. The existing
reservation consumes destination capacity exactly once, and a successful cutover refunds the
source slot. The destination is staged before the source is removed, and no new ownership token is
published until the source command completes. A failure before the departure is submitted removes
the staged destination state before the source block is lifted. Once the source departure has
been submitted, any failure stops world execution. The error does not show whether the character
is still on the source map, so the runtime must not reopen source commands or publish an owner it
has not confirmed.

This API is internal and is not a live portal transfer. It rejects tracked world sessions, because
`WorldSession.Map`, `WorldSession.Ownership`, visibility state and network bootstrap are all tied
to a map that never changes during the session's lifetime. The operation does not write to MySQL,
consume an observed commit marker or send client packets, and the handoff is not crash-safe. A
real portal coordinator needs durable fencing and recovery and the 5517 packet sequence before it
can use this relocation for connected characters.

## Unbound portal transaction

`WorldUnboundPortalTransferCoordinator` combines the authenticated portal preflight and the
saved-location read with an exclusive runtime commit seal for one ownership generation. Once the
seal is in place, new sessions, direct removals and disposing the reservation cannot reopen
changes on the source. The coordinator first atomically rotates the writer generation on the
character row and checks that the source, read again after this fence, has not changed. Any earlier
CAS either completes before the rotation or fails its generation check afterwards. The coordinator
then submits one CAS that names the expected revision, with a new operation ID and the acquired
generation. It reads the saved state for that account and character again and requires the
operation, revision and destination to match exactly. It then consumes the reserved destination
through the offline partition handoff described above. On success, the result is an owner with a
new generation and a matching saved destination. No socket write or client packet is produced.

After sealing there are only two outcomes: a confirmed relocation, or a permanent shutdown of the
world runtime. Write conflicts, database exceptions, read timeouts, a missing or negative operation
check, caller cancellation after sealing and a failed world cutover all stop the runtime. A
different marker or an unchanged revision cannot prove that an earlier SQL command will not still
complete. An adapter call with a time limit may outlive its caller after a timeout. Shutting down
the runtime stops further in-process admission, but it cannot stop a late database write or
provide crash-safe fencing across processes.

This workflow is internal and accepts only characters without a world session. It is not connected
to live gameplay input. It has no durable cross-process lease, recovery journal, world-session
rebinding, visibility cutover or confirmed 5517 map-transition acknowledgement. Before real
transfers are enabled, a restarted realm must be protected from a CAS sent before a crash that
completes late. Characters with an active session cannot be moved this way.

## Remaining work: map transfer

Working map transfers need a cutover state machine that obtains a preflight lease and also
handles:

- the durable-location CAS, and reading back to reconcile when an outcome is unknown;
- map and ownership generation handoff;
- insertion into the destination partition, with rollback on failure;
- departure and arrival visibility without the character appearing twice;
- the assumption in `WorldSession` that the map never changes, and resetting movement
  reconciliation;
- the 5517 action-86 and enter-map sequence, confirmed against the client.

The safe order between the MySQL CAS and state ownership across partitions must be proven under
crashes and cancellation. **There is no distributed transaction across MySQL, map workers and a
TCP socket.**

Only after those steps can revival through action 94 use the reviewed historical reborn
destinations (see [Revival destinations](revival-destination-authority.md)). Revival also needs
server-owned resource and status lifetimes and a confirmed 5517 response order. None of this works
yet.

## Testing

- The lease captures the source-map snapshot and blocks position and avatar commands on the
  source, without changing coordinates or database contents.
- A target reservation consumes per-map capacity but not global resident capacity.
- Duplicate, stale, same-map, missing-map and invalid-tile reservations are rejected.
- Disposal is idempotent and lets the source accept commands again.
- Removal invalidates the lease and refunds exactly one destination slot, without refunding
  capacity reserved by an unrelated new resident.
- Cancellation before the call and runtime shutdown cannot leave an active lease.
- A stalled barrier times out and releases both the capacity and the block on source changes.
- Barrier-ordering and global capacity tests use controlled partition blocking and a third map.

Live transfer testing with two clients belongs to the future map-transfer work.
