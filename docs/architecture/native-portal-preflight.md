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

1. Looks up the source map by server-owned `ownership.MapId`.
2. Matches the candidate tile against the map's canonical native DMap exits.
   No matching marker, or more than one marker for that tile, fails closed.
3. Resolves the **server-derived** `(mapId, passwayIndex)` through the immutable,
   evidence-qualified `MapTransitionCatalog`. Unresolved or unrepresentable
   target coordinates fail closed; no inferred default is permitted.
4. Uses #153 to reserve destination capacity and capture a FIFO source barrier
   snapshot. Pending older movement completes before the snapshot.
5. Confirms the authoritative source snapshot is alive and within one tile on
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

## Verification

The focused tests cover native marker matching, unresolved routes, off-radius
requests, duplicate-marker ambiguity, stale ownership, dead-source eligibility,
destination reservation accounting, and movement queued ahead of the barrier.
There is no schema migration, runtime configuration switch, or new protocol
layout. The source resolver has no production network caller in this slice.
