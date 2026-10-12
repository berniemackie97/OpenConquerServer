# Content

The GameServer loads game data from versioned files in `content/`. It never reads the client's own
data files; those are only imported offline by `OpenConquer.GameData.Tool`. Content files are
project-owned: they can be edited or extended directly, and the tool is just one way to produce
them.

```text
content/
├── items/item-types.json          Item type catalog
└── maps/
    ├── map-definitions.json       World maps
    ├── catalog-manifest.json      Record of how the initial map catalog was imported
    ├── terrains/<MapDataId>.ocbt  Base terrain, one file per terrain
    └── collision/<MapDataId>.ocmc Static collision, one file per terrain
```

The GameServer project copies `item-types.json`, `map-definitions.json`, and the `.ocbt` and `.ocmc`
files into its build and publish output. The manifest is not shipped.

## Item types

`item-types.json` (format version 2) holds one entry per item type with its ID, name, required
level, speed offset, life and mana bonuses, initial and maximum durability, lifetime in minutes,
stack capacity, profession-code requirement, native monopoly flags, silver and CP prices, and
attack range/speed. The additional values are data only; gameplay rules must be separately
evidence-verified. The GameServer loads it into an immutable `ItemTypeCatalog` and uses it to validate
persisted items at login: unknown types, incompatible lifetimes, and stacks over capacity are
rejected.

The current catalog has 14,981 types and was generated from client data:

| File               | SHA-256                                                            |
| ------------------ | ------------------------------------------------------------------ |
| `itemtype.dat`     | `90e9c983b0f78337f20720a3cf61d8f1392f23979565da14eb01dc514e3a71e7` |
| `item-types.json`  | `5862d91e68b408942be81512877a7bdb818d3f5f7106a593668222458d89246c` |

A content test pins the count and hash so changes are deliberate. Update the test in the same
change when editing the catalog.

## Maps

A world map has three identities that are never assumed to be equal:

- `mapId`, the server's world map;
- `mapDataId`, the terrain it uses (several maps can share one terrain);
- `flags`, a 64-bit property field.

`map-definitions.json` (format version 1) has a `maps` array whose entries contain exactly those
three fields. The loader rejects unknown versions, missing, extra, or duplicate fields, zero IDs,
duplicate map IDs, empty catalogs, and files over the configured size or count limits.

Each `mapDataId` needs one [base terrain](base-terrain-format.md) file and one
[collision](collision-format.md) file. Maps that share a terrain share the same loaded objects.

`WorldMapContentLoader` loads definitions, then terrains, then collision, and produces a
`WorldMapContentCatalog` indexed by map ID. Both content directories must contain exactly the
required files; a missing, extra, or invalid file fails startup. The GameServer starts the world
only after this succeeds.

The current release has 282 maps using 185 terrains. Seven map IDs from the original data have no
terrain that could be identified and are omitted, and 48 maps use zero flags because no source
value existed. `catalog-manifest.json` records these decisions per map, along with the checksum of
the source data.

Base terrain passability and static collision are terrain data, not movement rules. Walkability,
occupancy, and movement validation belong to the world runtime and are not implemented yet.

## Content tool

Run commands with:

```bash
dotnet run --project tools/OpenConquer.GameData.Tool -c Release -- <command> <arguments>
```

| Command                        | Arguments                                                       | Output                                     |
| ------------------------------ | --------------------------------------------------------------- | ------------------------------------------ |
| `generate-item-types`          | `<itemtype.dat> <item-types.json>`                              | Item catalog from client data              |
| `generate-map-definitions`     | `<source.json> <map-definitions.json>`                          | Normalized map catalog                     |
| `import-map-catalog`           | `<source.json> <new-directory>`                                 | Map catalog and manifest from import data  |
| `generate-base-terrain`        | `<terrain.json> <MapDataId.ocbt>`                               | One terrain from hand-authored JSON        |
| `generate-map-terrains`        | `<client-root> <map-definitions.json> <new-directory>`          | All required terrains from client maps     |
| `verify-map-collision-sources` | `<client-root> <map-definitions.json> <terrain-directory>`      | Report only; writes nothing                |
| `generate-map-collisions`      | `<client-root> <map-definitions.json> <terrain-directory> <new-directory>` | All required collision files    |

General behavior:

- Output is deterministic: the same input always produces the same bytes, and the tool prints
  SHA-256 hashes of its input and output.
- Commands that write a directory require that it does not exist yet. They stage every file and
  publish the directory only if all succeed. `generate-map-collisions` also reads each file back
  through the runtime loader before publishing.
- Commands that write a single file replace it; review the diff before committing.
- Client files are read with size limits, archive and path validation, and case-insensitive
  lookup. Symbolic links are refused.

`generate-map-definitions` also accepts optional `mapDataEvidence` and `flagsEvidence` strings (up to
512 characters) on each entry so reviewers can note where a value came from. They are validated
and then dropped from the output.

To add a new map, add its entry to `map-definitions.json` and supply its terrain and collision
files, either generated from client data or authored directly in the documented formats. No
client data is required.
