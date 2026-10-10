# Canonical Base Terrain

OpenConquer stores immutable base terrain in `.ocbt` files keyed by `MapDataId`.
These are independent project-owned artifacts, not copies of retail DMap files.

## Binary format version 1

All integer fields are little-endian.

| Offset | Field | Type |
|---|---|---|
| 0 | Magic `OCBT` | four bytes |
| 4 | Version, currently 1 | unsigned 16-bit |
| 6 | Reserved, zero | unsigned 16-bit |
| 8 | MapDataId | unsigned 32-bit |
| 12 | Width | unsigned 32-bit |
| 16 | Height | unsigned 32-bit |
| 20 | Exit count | unsigned 32-bit |
| 24 | Row-major base cells | six bytes each |
| After cells | Exit markers | twelve bytes each |
| End | SHA-256 of all preceding bytes | 32 bytes |

Each cell contains `surfaceId` (u16), `passabilityFlag` (u16), and `elevation`
(s16). Each exit contains `x` (s32), `y` (s32), and `passwayIndex` (u32).

Files are named `<MapDataId>.ocbt`.

SHA-256 detects accidental corruption. It does not authenticate an untrusted
publisher or substitute for reviewed release-content integrity checks.

## Custom OpenConquer content

A terrain source JSON document can be authored directly. It contains:

- `formatVersion`: 1;
- `mapDataId`: a nonzero unsigned 32-bit identity;
- `width` and `height`: positive dimensions;
- `cells`: exactly width × height row-major entries;
- `exits`: zero or more optional exit markers.

Each cell contains `surfaceId`, `passabilityFlag`, and signed `elevation`.
Each exit contains `x`, `y`, and `passwayIndex`.

Generate a canonical artifact without any native client:

    dotnet run \
      --project tools/OpenConquer.GameData.Tool/OpenConquer.GameData.Tool.csproj \
      -c Release \
      -- \
      generate-base-terrain \
      <authored-terrain>.json \
      content/maps/terrains/<MapDataId>.ocbt

The output filename must match the document's MapDataId for runtime loading.
Generation replaces the named destination artifact; review changes before commit.

Large terrain editors may generate the documented canonical binary representation
directly instead of maintaining millions of cells in JSON.

## Optional native 5517 importing

The native compatibility importer reads `ini/GameMap.dat` from a client asset
root and imports the referenced `.7z` or raw `.DMap` base grids for map data
IDs required by a supplied canonical map-definition catalog.

    dotnet run \
      --project tools/OpenConquer.GameData.Tool/OpenConquer.GameData.Tool.csproj \
      -c Release \
      -- \
      generate-map-terrains \
      <verified-client-root> \
      content/maps/map-definitions.json \
      <new-output-directory>

The destination directory must not already exist. The tool stages all outputs
and publishes the directory only if every required base terrain succeeds.

The importer uses explicit file limits and validates the native index, 7-Zip
container identity, grid dimensions, base-cell data, per-row native checksums,
and exit markers.

The importer does not parse or validate scenery, positioned overlays, or later
DMap sections. A successful import therefore does not prove that effective
movement collision has been reconstructed.

FoxConquer's extracted `RequiredFiles/Maps/*.DMap` representation is not a
verified retail 5517 DMap container fixture and must not serve as a retail
conformance baseline.

The 185 included canonical terrain artifacts cover all 282 resolved world-map
definitions. Repository content tests verify required-file coverage and load
every artifact through the production integrity-validating reader.

Independent retail conformance verification and reconstruction of scenery
collision, positioned overlays, and later DMap sections remain outstanding.

## Runtime

`FileMapBaseTerrainCatalogRepository` loads the required canonical files using
the already validated `MapDefinitionCatalog`. Required MapDataIds are distinct,
so maps sharing terrain do not duplicate their immutable cell grids.

A missing file, invalid identity, malformed artifact, unsupported version,
checksum mismatch, resource-limit violation, or incomplete required content
fails the catalog load. Runtime never opens native `.dat`, `.7z`, or `.DMap`
files.

The runnable GameServer host and startup readiness integration remain future
work.

## Gameplay boundary

The base-grid `PassabilityFlag` is retained as raw content information.

It is not a complete, authoritative `IsWalkable` rule.

Scenery, terrain objects, positioned overlays, movement restrictions, and map
rules must be composed and verified before world partitions use terrain for
authoritative collision or pathing.

Custom OpenConquer features need valid canonical content and supported runtime
rules, not evidence that they existed in the original 5517 client.
