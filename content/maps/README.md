# Canonical World-Map Definitions

OpenConquer's world-map definitions are server-owned content.

`MapId` identifies a server world map. `MapDataId` identifies the client terrain
resource used by that map. `Flags` preserves the complete unsigned 64-bit map
property value. Multiple world maps may share one map-data identity.

Native `ini/GameMap.dat` provides client terrain references. It does not
establish the complete server-owned map-definition catalog.

## Source and runtime formats

The offline source is `map-definitions.source.json`, with format version 1.

Every source record contains:

- `mapId`: nonzero unsigned 32-bit world-map ID;
- `mapDataId`: nonzero unsigned 32-bit client terrain ID;
- `flags`: unsigned 64-bit map-property value;
- `mapDataEvidence`: nonempty reference supporting the map-data assignment;
- `flagsEvidence`: nonempty reference supporting the exact flags value.

Evidence references are required for review and traceability. Their presence
does not independently prove a value is correct. A zero flags value must be
supported just like any other value; it must not substitute for missing evidence.

Source validation rejects unsupported versions, unexpected or repeated JSON
properties, missing evidence, invalid identities, duplicate world-map IDs,
invalid numeric widths, and oversized source documents.

The generated runtime file is `map-definitions.json`, also format version 1.
It contains only `mapId`, `mapDataId`, and `flags`, ordered by MapId.

The runtime loader validates the file and publishes one immutable
`MapDefinitionCatalog`. It does not access or parse the source evidence,
retail client DAT files, or terrain containers.

## Generation

After the source records and their evidence have been reviewed:

    dotnet run \
      --project tools/OpenConquer.GameData.Tool/OpenConquer.GameData.Tool.csproj \
      -c Release \
      -- \
      generate-map-definitions \
      <audited-source>/map-definitions.source.json \
      content/maps/map-definitions.json

The tool reports SHA-256 hashes for both source and generated content.

The canonical output is deterministic for an identical set of source records,
regardless of source record ordering.

## Production baseline status

A complete verified 5517 world-map definition baseline has not yet been
established in this repository.

Do not create a production `map-definitions.json` from guessed assignments,
default flags, unrelated 6270-era database rows, or unreviewed emulator
exports merely to satisfy the catalog loader.

The historical OpenConquerPublic map corrections are useful evidence, but
they also document unresolved identities and previously incorrect mappings.
The original map and flag assignments still require independent review.

Before a production baseline is accepted, its complete record count, source
hash, output hash, and verified map-specific evidence must be documented
and pinned by an integrity test, following the existing item-type catalog
precedent.

## Remaining map-system boundaries

The following are not completed by this catalog slice:

- a verified full 5517 map-definition dataset;
- offline client map-index and terrain conversion;
- canonical terrain/collision artifacts;
- cross-validation of world-map definitions against terrain;
- GameServer startup publication and readiness gating;
- authoritative map instances and world-session integration.

The GameServer must not assume a map exists or substitute MapId for MapDataId
when canonical content is absent.
