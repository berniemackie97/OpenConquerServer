# Canonical Static Collision

OpenConquer stores the final effective static terrain collision in immutable,
versioned `.ocmc` artifacts keyed by `MapDataId`.

Collision content is independent of retail map containers, scenery files, and
terrain-object lists. Those source formats are supported only by the offline
game-data tool.

The canonical base terrain remains in a separate `.ocbt` artifact. A collision
artifact contains only cells whose final effective values differ from that base.

## Binary format version 1

All integers are little-endian.

| Offset | Field | Type |
|---|---|---|
| 0 | Magic `OCMC` | four bytes |
| 4 | Version, currently 1 | unsigned 16-bit |
| 6 | Reserved, zero | unsigned 16-bit |
| 8 | MapDataId | unsigned 32-bit |
| 12 | Width | unsigned 32-bit |
| 16 | Height | unsigned 32-bit |
| 20 | Override count | unsigned 32-bit |
| 24 | Canonical base-terrain fingerprint | SHA-256, 32 bytes |
| 56 | Effective-cell overrides | ten bytes each |
| End | Collision-content SHA-256 | 32 bytes |

Each override contains:

- `cellIndex`: unsigned 32-bit, row-major, strictly increasing;
- `surfaceId`: unsigned 16-bit;
- `passabilityFlag`: unsigned 16-bit;
- `elevation`: signed 16-bit final effective elevation.

The file length is exactly `88 + (overrideCount * 10)` bytes.

Override indices must be unique and within the base grid. Redundant overrides
are invalid. The base terrain supplies every cell not explicitly overridden.

A zero-override artifact is valid and exactly 88 bytes long. It is required
even when no static scenery changes the base terrain. Missing artifacts are
not equivalent to valid empty collision.

Files are named `<MapDataId>.ocmc`.

## Base-terrain binding

The 32-byte base-terrain fingerprint equals the SHA-256 checksum stored in
the corresponding canonical `.ocbt` version-one artifact.

It is calculated over the complete canonical `.ocbt` payload preceding
that artifact's final 32-byte integrity checksum.

This binds collision overrides to base terrain identity, dimensions, cells,
elevations, and exit markers. Matching identity and dimensions alone is
insufficient to establish compatibility.

The collision artifact's own trailing SHA-256 covers its complete header
and override records.

These checks detect corruption and incompatible content. They do not provide
publisher authentication.

## Offline generation

Build the GameData.Tool project, then run:

    dotnet run \
      --project tools/OpenConquer.GameData.Tool \
      -c Release \
      --no-build \
      -- \
      generate-map-collisions \
      <verified-client-root> \
      content/maps/map-definitions.json \
      content/maps/terrains \
      content/maps/collision

The destination directory must not exist.

The command loads and validates all required canonical base terrains,
independently verifies the matching source base grids, composes static
terrain-object attachments in source order, and writes final effective
collision overrides.

Every staged artifact is read back through the production collision binary
reader. The complete directory is published only after all terrains succeed.

The command does not overwrite an existing release directory.

Custom OpenConquer content can produce the same documented canonical format
without retail source files or historical reconstruction tooling.

## Runtime boundary

The production reader belongs to OpenConquer.Infrastructure and consumes
canonical collision artifacts plus their matching immutable base terrain.

The GameServer must never load retail `.dat`, `.DMap`, `.7z`, or terrain-object
source files to obtain static collision.

The immutable runtime collision catalog validates complete artifact
coverage, base-terrain compatibility, and aggregate resource limits
before publishing a snapshot.

The standalone GameServer world-partition core retains the immutable effective collision snapshot
and checks initial character coordinates against its dimensions. It does not derive passability or
movement authorization. GameServer startup readiness, dynamic occupancy, and gameplay movement
restrictions remain separate boundaries.

Static terrain passability is not a complete gameplay movement policy.

## Verification

The release tests require one valid collision artifact per required MapDataId
and reject missing or additional collision files.

They validate every artifact through the production reader.

Independent archived passway coordinates provide selected effective-passability
conformance checks. Those samples do not establish complete native tile-level
parity or independent verification of every surface and elevation.

Changes to canonical base terrain require regeneration or revalidation of
its corresponding collision artifact.

Future OpenConquer content is not required to reproduce historical retail
asset identities or source bytes.
