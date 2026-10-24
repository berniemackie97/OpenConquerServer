# Map-transition candidates by DMap exit (v1)

`content/maps/map-transitions.json` is a versioned, hash-pinned catalog of candidate destinations
for the 5517 DMap exit indices. Not every destination has been checked against a 5517 retail
GameServer. The catalog is separate from movement processing; the connected portal coordinator
uses only explicitly release-approved entries after server-owned exit verification,
source reservation and durable generation fencing. Resolved historical candidates are not
release approvals.

## Sources and release

The 5517 client (`Conquer.exe`, `CHero_TickPortalDetectionAndMapChangeRequest`, `0x65D9B2`) sends
action subtype `85` with the tile of the exit it detected, not a passway index. The server must
match that tile against its own DMap exits before it looks up a destination. See
`20_notes/Server/map-transitions.md`, `20_notes/Server/map-catalog-5517.md` and
`20_notes/Server/historical-reconciliation-5095.md` for the original analysis and its limits.

Input: `Archive.zip`, SHA-256
`21f1f6ac7b3fbb57423ceccefeb58cbb55f3fdf34a8f7930832e37b2483f65a1`, member
`docs/evidence/map-catalog-5517/passways-5517.json`, SHA-256
`d728c4ec78fb75266b90ebce99f847aebd7ccdeb283d20ad9066f64747ff4ab3`.

Output: `content/maps/map-transitions.json`, SHA-256
`4b5258a9d70fd04e87e76fc9be00c4bb8464638f1f2f6d449e225d8cd9d62ea0`.

The reviewed source data has 373 distinct `(mapId, passwayIndex)` pairs:

| Result | Entries | Treatment |
| --- | ---: | --- |
| Resolved historical candidates, statically walkable | 322 | Retained for review, **not playable by default** |
| No historical source row | 41 | Unresolved. No arrival point is invented. |
| Shared terrain whose passway for this world is unresolved | 7 | Unresolved. Never takes another world's destination. |
| Every historical candidate lands on a blocked tile | 3 | Unresolved. Never routes to that tile. |

None of these historical candidates is independently confirmed as a retail 5517 server
route. The shipped catalog has **zero release-approved routes**. Only explicitly promoted
`release-approved` entries are returned to runtime portal preflight. Promotion is a
version-controlled content-release decision requiring route-specific evidence and reviewed
source hashes; it must not be inferred from walkability or historical source agreements.
A release reviewer must also check the destination tile against exit triggers: native 5517
immediately sends another action 85 when arriving within a tile of an exit. The historical
candidate census has zero such arrival-to-exit overlaps, but future additions need review.

The 322 candidates use source tags `L` (258), `Q` (62), and `T7` (2). The tags name historical
server datasets from other versions. They do not prove 5517 behavior. Source preferences and world
renumbering come from the reviewed investigation, including the known differences between the ZF08
snapshot and the other sources. Unresolved pairs are listed with an unresolved status instead of
being left out.

## Validation

`FileMapTransitionCatalogRepository.LoadAsync(WorldMapContentCatalog, CancellationToken)` reads a
size-limited, versioned JSON document. `MapTransitionCatalog` checks that every loaded DMap
`(mapId, passwayIndex)` pair appears once. Extra, duplicate, missing, unknown-status, and malformed
entries fail the load. A resolved row also needs a known target world, a recognized historical
source, and an arrival tile that is in bounds and statically passable in the composed collision.
Unresolved rows must not carry a fallback destination. After loading, the catalog is immutable and
lookups do not allocate. Lookups are keyed by the map ID and passway index that the server derives
itself. `TryResolve` retains candidate evidence for tooling and `TryResolveReleased`
returns only explicitly approved gameplay destinations.

`MapTransitionContentTests` checks the release file against the full map, terrain, and collision
content. The GameServer also requires the file at world startup. It is loaded after the maps,
validated against that same immutable map-content snapshot, and published with the running world. If
the catalog is missing, malformed, unsupported, incomplete, or does not match the maps, the world
never becomes ready and the server accepts no connections. The file is copied into the GameServer
build and publish output. Its path and limits are configured under `GameServer:Content`.

The action-85 resolver is still a reversible preflight. `WorldConnectedPortalTransferCoordinator`
combines it with a durable location CAS, exact receipt check, connected residency handoff, session
generation rebind, visibility transfer and the verified native 86/1110 arrival sequence. A rejected
portal closes its waiting connection rather than sending an unverified map response. Never pass a
client-supplied passway index to `TryResolve`. Existing historical source classifications remain
version-qualified and do not assert retail destination rules.

The catalog is a curated subset of the source data. Terrain and collision still come only from their
own files. The project owns the catalog, and it must be reviewed and re-pinned whenever the original
sources change. It adds no MySQL tables and imports no historical player, account, or credential
data.

## Reproducible rebuild

Extract `docs/evidence/map-catalog-5517/passways-5517.json` from the pinned `Archive.zip` into a
working directory and run:

```bash
python3 tools/map-transitions/rebuild.py /path/to/passways-5517.json /tmp/map-transitions.json
cmp /tmp/map-transitions.json content/maps/map-transitions.json
```

The script refuses changed inputs, changed output bytes, and an existing output file with different
contents. A future data revision must update the source manifest, generated content, release hash,
related tests, and this document together. The live GameServer never regenerates the file.
