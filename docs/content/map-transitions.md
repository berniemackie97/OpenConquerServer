# Native-exit-indexed map-transition candidates (v1)

`content/maps/map-transitions.json` is a versioned, hash-pinned **candidate** catalog for the native
5517 DMap exit indices, not a claim that all destinations were verified against a 5517 retail
GameServer. It is intentionally separate from movement processing and does **not** enable map changes.

## Evidence and release

The native 5517 client (`Conquer.exe`, `CHero_TickPortalDetectionAndMapChangeRequest`, `0x65D9B2`)
requests action subtype `85` with the candidate exit tile, **not** a passway index. The server must
resolve that tile against its own canonical DMap exits and only then look up a destination. See
`20_notes/Server/map-transitions.md`, `20_notes/Server/map-catalog-5517.md` and
`20_notes/Server/historical-reconciliation-5095.md` for original evidence and limitations.

Input: `Archive.zip`, SHA-256
`21f1f6ac7b3fbb57423ceccefeb58cbb55f3fdf34a8f7930832e37b2483f65a1`, member
`docs/evidence/map-catalog-5517/passways-5517.json`, SHA-256
`d728c4ec78fb75266b90ebce99f847aebd7ccdeb283d20ad9066f64747ff4ab3`.

Output: `content/maps/map-transitions.json`, SHA-256
`4b5258a9d70fd04e87e76fc9be00c4bb8464638f1f2f6d449e225d8cd9d62ea0`.

The reviewed evidence contains **373** distinct native `(mapId, passwayIndex)` identities:

| Result | Entries | Treatment |
| --- | ---: | --- |
| Resolved candidate, statically walkable destination | 322 | Available through catalog lookup, subject to future authoritative runtime rules |
| No historical source row | 41 | Unresolved; never invent an arrival |
| Shared terrain with unresolved world-specific passway | 7 | Unresolved; never inherit another world's destination |
| All historical candidates reach a blocked arrival | 3 | Unresolved; never route to that tile |

The 322 candidates use source tags `L` (258), `Q` (62), and `T7` (2). These tags name
historical cross-version server datasets, **not** native-5517 proofs. Source preferences and
world renumbering are inherited from the reviewed investigation, including the known differences
between the ZF08 snapshot and other sources. Unknown identities are represented explicitly.

## Validation contract

`FileMapTransitionCatalogRepository.LoadAsync(WorldMapContentCatalog, CancellationToken)` reads a
bounded, versioned JSON document. `MapTransitionCatalog` validates that **every** loaded native
DMap `(mapId, passwayIndex)` pair appears exactly once; extra, duplicate, missing, unknown-status,
and malformed entries fail loading. A resolved row additionally requires a known target world,
recognized historical source, and an in-bounds, statically passable **composed collision** arrival.
Unresolved rows may not contain an accidental fallback destination. Once loaded, lookups are
immutable and allocation-free, keyed by the server-derived map ID and passway index.

The release is exercised against the complete canonical map, terrain, and collision content by
`MapTransitionContentTests`. Since slice #155 the same bounded file is also a **mandatory
GameServer world-startup input**: it is loaded after canonical maps, validated against that exact
immutable map-content snapshot, and published with the running world. A missing, malformed,
unsupported, incomplete or mismatched catalog prevents world readiness and ingress.
The file is copied into the GameServer build and publish output; its path and limits are
configured under `GameServer:Content`.

The native action-85 resolver (#154) remains an internal reversible preflight only. The live
map-transition coordinator must still handle durable state, ownership handoff, session/visibility,
client packet order and failure reconciliation before any native portal request can move a player.
Never pass a client-supplied index directly to `TryResolve`.

This catalog is a curated projection, not a second runtime authority for terrain or collision.
It is project-owned and must be reviewed and repinned when original evidence changes. It adds no
MySQL tables and imports no historical player, account, or credential data.

## Reproducible rebuild

Extract `docs/evidence/map-catalog-5517/passways-5517.json` from the pinned `Archive.zip` into
an evidence workspace and run:

```bash
python3 tools/map-transitions/rebuild.py /path/to/passways-5517.json /tmp/map-transitions.json
cmp /tmp/map-transitions.json content/maps/map-transitions.json
```

The script refuses changed inputs, changed output bytes, or an unexpected existing destination.
A deliberate future data revision updates the source manifest, generated content, release hash,
related tests, and this document together. The canonical file must never be silently regenerated
by the live GameServer.
