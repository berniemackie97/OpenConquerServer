# Canonical World-Map Definitions

OpenConquer's world-map definitions are server-owned content.

`MapId` identifies a server world map. `MapDataId` identifies the client terrain
resource used by that map. `Flags` preserves the complete unsigned 64-bit map
property value. Multiple world maps may share one map-data identity.

Native `ini/GameMap.dat` provides client terrain references. It does not
establish the complete server-owned map-definition catalog.

## Canonical content format

`map-definitions.json` is OpenConquer's authoritative, versioned map-definition
catalog. Developers can author and maintain it directly. Neither the file format
nor the runtime loader requires native Conquer Online source data.

Format version 1 contains a `maps` array. Each definition contains exactly:

- `mapId`: nonzero unsigned 32-bit server world-map ID;
- `mapDataId`: nonzero unsigned 32-bit terrain-resource identity;
- `flags`: unsigned 64-bit map-property value.

Multiple server maps may share one `mapDataId`. A zero flags value is valid when
zero is the intended value, but it must not be used to disguise unknown flags
during initial retail-baseline reconstruction.

The runtime loader rejects unsupported versions, unexpected or duplicate
properties, missing fields, invalid identities, duplicate world-map IDs,
invalid numeric values, empty catalogs, and resource-limit violations.
It publishes an immutable `MapDefinitionCatalog`.

## Optional import and normalization

The offline generator accepts the same required fields as the canonical format.
Imported records may additionally supply `mapDataEvidence` and `flagsEvidence`
as optional nonempty strings of at most 512 characters.

Provided provenance is validated and discarded during canonical generation.
It is useful for reviewing the original 5517 baseline, but never required for
custom OpenConquer content. A reference is not independent proof of correctness.

The generator sorts definitions by `mapId` and writes the canonical format
deterministically. Running the generator is optional; authored canonical content
can be loaded directly.

Historical source records and canonical runtime records are not two mandatory
representations that developers must maintain in parallel.

## Optional generation

To normalize imported or authored records into deterministic canonical output:

    dotnet run \
      --project tools/OpenConquer.GameData.Tool/OpenConquer.GameData.Tool.csproj \
      -c Release \
      -- \
      generate-map-definitions \
      <source>/maps.json \
      content/maps/map-definitions.json

The source and destination must be different files. Generation replaces the
destination with the normalized catalog, so review the resulting diff before
committing. Do not overwrite independently authored changes unintentionally.

The tool reports SHA-256 hashes for the source and generated content.
Different record orderings and optional provenance metadata do not change
the canonical output when the map definitions themselves are identical.

## Native 5517 compatibility baseline

The initial reconstruction baseline is included in this repository:

- 282 resolved world-map definitions;
- 185 distinct canonical base-terrain artifacts;
- seven explicitly unavailable historical map identities;
- 48 documented zero-flag defaults where source flags were unavailable;
- preservation of the full 64-bit source flags, including values containing
  bits outside the client compatibility projection.

`catalog-manifest.json` records the reconstruction decisions, availability,
and source checksum. The original research evidence is maintained separately
and is not required to compile, run, or extend OpenConquer.

The offline `import-map-catalog` command reproduces the initial catalog from
that evidence. It is a historical migration adapter, not a required step
for future map creation or updates.

This is a working compatibility baseline, not proof of complete retail parity.
Unresolved identities, provisional defaults, scenery collision, and remaining
native behavior require further verification.

Future OpenConquer maps and revisions are independent project-owned content.
They require normal schema, gameplay, asset, and compatibility validation;
they do not require retail 5517 provenance or matching native definitions.
Release-content integrity hashes identify intentional artifacts and are not
requirements to reproduce the original retail bytes.

## Base terrain

Base terrain is stored as versioned `.ocbt` content indexed by `MapDataId`.
It is independent of retail DMap and 7-Zip containers. The immutable runtime
catalog loads only required terrain identities and validates resource limits,
format version, file length, and SHA-256 integrity.

Maps sharing the same `MapDataId` share one immutable base-terrain grid.
Native importing is optional. Custom terrain may be generated directly
from authored JSON without a retail client or historical provenance.

See [Base Terrain](base-terrain.md) for the exact format and generation commands.

## Remaining map-system boundaries

The following are not completed by this catalog slice:

- resolution of the remaining historical map-definition uncertainties;
- independent retail conversion and behavioral conformance verification;
- scenery and positioned-overlay collision composition;
- complete world-map-to-terrain readiness integration;
- GameServer startup publication and readiness gating;
- authoritative map instances and world-session integration.

The GameServer must not assume a map exists or substitute MapId for MapDataId
when canonical content is absent.
