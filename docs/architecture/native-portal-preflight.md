# Native portal candidate authority — slice #154

## Scope

This slice implements a **trusted world-side preflight resolver** for the candidate
exit tile carried in retail 5517 MsgAction subtype 85. It consumes the reversible
reservation API from #153. It does not register a network handler, change world
residency, commit database location, mutate session map identity, publish visibility,
acknowledge a map transition, or initiate revival. Consequently **portals are not
operational yet**. The implementation is not a substitute for the required cutover.

## Source authority

The native `Conquer.exe` 5517 function
`CHero_TickPortalDetectionAndMapChangeRequest` at `0x65D9B2` samples nearby DMap
exit marker tiles and sends subtype 85 with the candidate marker X/Y packed in the
parameter pair. The client does not send a passway index. This is recorded in
`20_notes/Server/map-transitions.md` (native analysis of `0x65D9B2`, `0x637700`
and `0x63777D`). Packet hero coordinates and facing do not grant location authority.

`WorldPortalPreflight.TryReserveAsync` is internal and must only be called for the
exact authenticated world ownership token. It:

1. Requires the transition catalog to have been validated from the **exact same**
   `WorldMapContentCatalog` object used to construct the runtime. Equal map IDs,
   passway indices or separately loaded equivalent files are insufficient: a
   mismatched catalog can otherwise authorize a stale route. This reference
   identity check is fail-closed and does not compare mutable representations.
2. Looks up the source map by server-owned `ownership.MapId`.
3. Matches the candidate tile against the map's canonical native DMap exits.
   No matching marker, or more than one marker for that tile, fails closed.
4. Resolves the **server-derived** `(mapId, passwayIndex)` through the immutable,
   evidence-qualified `MapTransitionCatalog`. Unresolved or unrepresentable
   target coordinates fail closed; no inferred default is permitted.
5. Uses #153 to reserve destination capacity and capture a FIFO source barrier
   snapshot. Pending older movement completes before the snapshot.
6. Confirms the authoritative source snapshot is alive and within one tile on
   each axis of the candidate marker. A stale or moved-away source refunds the
   reservation. The returned lease is generation-fenced, temporary, and the
   caller must dispose it on every exit path.

The catalog's historical destination evidence is **cross-version**, not complete
5517 server-side proof. Runtime eligibility also depends on gameplay states
not yet modeled. Only proven restrictions are enforced here; this cannot be
called a fully authorized gameplay transition until the cutover state machine
owns all relevant authoritative conditions.

## Why this boundary is separate

The existing `WorldSession` exposes a lifetime-stable `Map` and `Ownership` and
`WorldVisibilityService` keys membership by that map. #152's CAS matches the
*last persisted location*, not the latest moving-world snapshot. Committing an
in-memory map change before those boundaries are redesigned would create
cross-system contradictions. This slice stops before any irreversible mutation.

The next cutover must solve durable CAS and uncertain-outcome reconciliation,
inter-partition ownership handoff, session mutation and revocation, position
history reset, departure/arrival visibility, and verified native 86/map-entry
ordering. No distributed transaction spans MySQL, world partitions and TCP.

## Persisted-location preparation (#157)

`WorldPortalPersistedLocationPreflight.TryReserveAsync` composes this reversible portal
reservation with #156's identity-scoped `ICharacterLocationObservationStore`. It accepts the
**trusted authenticated login profile** and the exact world-ownership token, rejects character
or login-map mismatches *before* taking a reservation or querying persistence, and then
resolves and barriers the canonical native marker as above. Only after source mutations
are fenced does it read the persisted `(map_id, x, y)` for the profile's account and
character. Missing data, changed persisted map identity, a revoked generation, or a
canceled observation refuse preparation and refund destination capacity. Read errors
propagate after the refund; even an observation provider ignoring cancellation is bounded
by a 10-second wait and cannot retain the capacity reservation indefinitely.

The result pairs an immutable observed **persisted source location** with the existing
revocable reservation and its distinct **live source snapshot**. Persisted coordinates may
differ from in-memory coordinates because normal movement is not durably saved. The paired
result is `IDisposable`; callers must release it in `finally` or `using`, including on
shutdown. No service or client ingress invokes this internal preparation yet.

**Not commit authority:** an observation is not a transfer receipt, a monotonic revision,
a permanent ownership lease, or proof that an outstanding CAS cannot still commit.
No database write, residency handoff, network acknowledgement, client visibility change,
or position-save pipeline is introduced. The future coordinator must independently
serialize durable writes across generations, resolve uncertain outcomes, and atomically
manage ownership/session/visibility before enabling a native map transition.

## Verification

The focused tests additionally reject a catalog constructed from a different
world snapshot and prove that a live reservation occupies destination capacity.
They cover native marker matching, unresolved routes, off-radius
requests, duplicate-marker ambiguity, stale ownership, dead-source eligibility,
destination reservation accounting, and movement queued ahead of the barrier.
There is no schema migration, runtime configuration switch, or new protocol
layout. The source resolver has no production network caller in this slice.
