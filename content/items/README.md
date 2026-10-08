# Item Type Catalog

`item-types.json` is the canonical runtime item-type catalog for the clean Conquer Online 5517 data set.

The GameServer consumes this generated catalog through `ItemTypeCatalog`. It does not parse or depend
on native client `itemtype.dat` files at runtime.

## Canonical 5517 Baseline

- definitions: `14,981`
- catalog format: `1`
- source `itemtype.dat` SHA-256:
  `90e9c983b0f78337f20720a3cf61d8f1392f23979565da14eb01dc514e3a71e7`
- generated `item-types.json` SHA-256:
  `e18b1822dd9270c4c5fd36c7c81c2c413c9cd9e3cf73da9a50f18856bb484ab7`

The source hash identifies the verified clean native 5517 `ini/itemtype.dat` used to generate this
baseline. The native source file is not part of the server repository.

## Regeneration

Run the repository game-data tool against the verified clean 5517 source:

    dotnet run \
      --project tools/OpenConquer.GameData.Tool/OpenConquer.GameData.Tool.csproj \
      -c Release \
      -- \
      generate-item-types \
      <clean-5517-client>/ini/itemtype.dat \
      content/items/item-types.json

The generated catalog is deterministic for the same source payload and generator implementation.

Catalog changes must be intentional and reviewed. The existing canonical catalog integrity
test pins the original 5517 baseline's definition count and SHA-256 to detect unintended
changes to that historical artifact.

## OpenConquer-owned content

`item-types.json` is a first-class canonical content format. Future custom items,
balance changes, and item definitions can be authored directly without retail
`itemtype.dat`, native evidence references, or the native import tool.

The legacy importer remains available for reconstructing the initial baseline.
Do not run it over independently edited content without reviewing the replacement.
Changes to a pinned catalog require corresponding intentional updates to its
content-integrity expectations, or a separately versioned release artifact.
Integrity hashes identify approved release content; they do not permanently
require retail item definitions or prevent custom OpenConquer items.
