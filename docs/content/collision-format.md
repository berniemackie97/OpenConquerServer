# Collision format (`.ocmc`)

A collision file stores the final static collision for one terrain as a list of cells that differ
from its [base terrain](base-terrain-format.md). Files are named `<MapDataId>.ocmc` and live in
`content/maps/collision/`. Every terrain needs a collision file, even when nothing differs. A
missing file is an error and is never read as an empty override list.

## Layout (version 1)

All integers are little-endian.

| Offset | Field                                      | Type      |
| -----: | ------------------------------------------ | --------- |
|      0 | Magic `OCMC`                               | 4 bytes   |
|      4 | Version (`1`)                              | `UInt16`  |
|      6 | Reserved (`0`)                             | `UInt16`  |
|      8 | MapDataId                                  | `UInt32`  |
|     12 | Width                                      | `UInt32`  |
|     16 | Height                                     | `UInt32`  |
|     20 | Override count                             | `UInt32`  |
|     24 | Base terrain fingerprint                   | 32 bytes  |
|     56 | Overrides, 10 bytes each                   |           |
|    end | SHA-256 of all preceding bytes             | 32 bytes  |

An override is `cellIndex` (`UInt32`, row-major), `surfaceId` (`UInt16`), `passabilityFlag`
(`UInt16`), and `elevation` (`Int16`). Indices must be strictly increasing and inside the grid, and
each override must change the cell. The file length is `88 + 10 × overrideCount` bytes.

The fingerprint is the checksum stored at the end of the matching `.ocbt` file. It ties the
overrides to that version of the terrain, so a collision file cannot be applied to a terrain whose
cells or exits have changed. Regenerate collision whenever a base terrain changes.

## Loading

`FileMapTerrainCollisionCatalogRepository` loads one file per required terrain, checks the
fingerprint against the loaded base terrain, validates the layout, ordering, and checksum, and
applies limits across the whole catalog, such as the total number of overrides. Any problem fails
the load.

## Generating from client maps

`generate-map-collisions` loads the base terrains, re-reads the client's base grids to confirm they
still match, applies the scenery objects that alter collision in the order the client applies
them, and writes the resulting overrides. Each file is read back through the runtime loader before
the output directory is published.

`verify-map-collision-sources` performs the same checks and composition without writing anything
and prints statistics per terrain.

Content tests require one valid collision file per terrain and check a set of portal destination
coordinates whose walkability is known from other sources. These are spot checks. They do not show
that every cell matches the client.
