# Base terrain format (`.ocbt`)

A base terrain file stores the ground grid for one terrain. Files are named `<MapDataId>.ocbt` and
live in `content/maps/terrains/`.

## Layout (version 1)

All integers are little-endian.

| Offset          | Field                                 | Type        |
| --------------- | ------------------------------------- | ----------- |
| 0               | Magic `OCBT`                          | 4 bytes     |
| 4               | Version (`1`)                         | `UInt16`    |
| 6               | Reserved (`0`)                        | `UInt16`    |
| 8               | MapDataId                             | `UInt32`    |
| 12              | Width                                 | `UInt32`    |
| 16              | Height                                | `UInt32`    |
| 20              | Exit count                            | `UInt32`    |
| 24              | Cells, row-major, 6 bytes each        |             |
| after cells     | Exits, 12 bytes each                  |             |
| end             | SHA-256 of all preceding bytes        | 32 bytes    |

A cell is `surfaceId` (`UInt16`), `passabilityFlag` (`UInt16`), and `elevation` (`Int16`). An exit is
`x` (`Int32`), `y` (`Int32`), and `passwayIndex` (`UInt32`); exits may lie outside the grid.

The checksum detects corruption. It does not prove who produced the file.

## Loading

`FileMapBaseTerrainCatalogRepository` loads the file for every `mapDataId` in the map catalog. It
checks the file name, magic, version, identity, dimensions, length, and checksum against the
configured limits, and fails the whole load on any error.

`passabilityFlag` is raw terrain data. Collision files refine it, and the world runtime will apply
movement rules on top of both.

## Authoring

A terrain can be written by hand as JSON and converted with `generate-base-terrain`:

```json
{
  "formatVersion": 1,
  "mapDataId": 9001,
  "width": 2,
  "height": 1,
  "cells": [
    { "surfaceId": 1, "passabilityFlag": 0, "elevation": 0 },
    { "surfaceId": 1, "passabilityFlag": 1, "elevation": 4 }
  ],
  "exits": [{ "x": 0, "y": 0, "passwayIndex": 0 }]
}
```

`cells` must contain exactly `width × height` entries. The output file name must match `mapDataId`.
Large terrain editors can write the binary format directly.

## Importing from client maps

`generate-map-terrains` reads the client's `GameMap.dat` index, opens each referenced map file (raw
or 7-Zip compressed), and imports the base grid and exit markers. It validates the index, archive
contents, dimensions, cells, per-row checksums, and exits. Scenery and later map sections are not
read; static collision comes from them separately (see [collision format](collision-format.md)).
