# Historical source reconciliation (5095 sources and the 5517 server)

This document sets the rules for comparing three separately supplied 5095-era archives with the 5517
server: `ZFSERVER.zip`, `zfServerRE.zip`, and the derived
`OpenConquer-5095-Server-Evidence-Analysis.zip`. The archives are research input only. They are not
the runtime database or an approved data release, and they do not show how the 5517 retail server
behaves. Historical accounts, credentials, characters, and logs never go into an import pipeline for
this server.

## Current sources of truth

| Area | Current implementation | Historical sources and how to reconcile them |
| --- | --- | --- |
| World map identities | `content/maps/map-definitions.json` has 282 records of `mapId`, `mapDataId`, and 64-bit flags | 5095 `GameMap.dat` lists 169 IDs, `cq_map` has 240 rows, and FoxConquer 5517's `GameMap.dat` has 262 mappings. These sources cannot be swapped for one another. Resolve each map identity, terrain binding, and flags value on its own. |
| Base terrain | 185 `content/maps/terrains/<MapDataId>.ocbt` files, imported from the 5517 client with per-row checksum checks | 155 archived 5095 `map/map/*.DMap` grids pass the row-level checks. Use them to compare versions. Never overwrite checked 5517 terrain with 5095 bytes. |
| Composed static collision | 185 `.ocmc` files, each tied by SHA-256 to its matching `.ocbt` file | The archived `.Part` scenery files contain readable collision cells. They help explain how scenery attaches to a map. Do not copy them into the 5517 collision catalog unless the source matches and the composed result has been checked. |
| Item type definitions | `content/items/item-types.json`, 14,981 IDs, hash-pinned from the 5517 client `itemtype.dat` | Historical `cq_itemtype` is an older source of definitions. A type with the same number may have different stats, level, encoding, or compatibility fields. Compare fields and provenance. Do not add a second, implicit runtime source. |
| Owned items | `openconquer_game.items` rows, owned by `characters` and validated against `ItemTypeCatalog` | Legacy item instances and item definitions are different kinds of data. The database schema stores owned item instances. It has no copy of the 5517 item-type table. |
| Shops | No shop catalog or shop persistence yet | 5095 `Shop.dat` lists 34 shops/1,370 slots, while `cq_goods` has 1,587 rows for 33 shop IDs. Before importing any offerings, trace each disagreement to a specific binary version and call site. |
| Character starting attributes | Six selected families, with immutable starting allotments set in configuration | Legacy `cq_point_allot` has five families across 120 levels. Its Ninja level-one row differs, and Monk is missing. The 5517 data for this version takes precedence. |

See [Content](README.md), [Base terrain](base-terrain-format.md),
[Collision](collision-format.md), and [Persistence](../architecture/persistence.md).

## Map provenance and collision rules

A `mapId` is a server world location. A `mapDataId` identifies the client terrain, and several maps
can use the same terrain. Never infer these IDs from legacy file names, and never assume they are
equal. `MapDefinition` keeps the full 64-bit flag value. The import manifest lists seven maps that
were omitted because their terrain could not be resolved, and 48 entries with zero flags because no
source value was found. In those entries, zero means the source is unknown. It does not mean the
retail flags are zero. Do not fill these fields with historical values.

`OpenConquer.GameData.Tool` validates `GameMap.dat`, the source DMap grids, row checksums, scenery
object references, attachment budgets, and the generated `.ocbt`/`.ocmc` formats. At runtime, the
map-content loader checks every required file, checksum, map identity, terrain fingerprint, and
collision composition. These checks cover file integrity and where the data came from. They do not
mean the map is fully simulated.

The 5095 DMap audit covers 155 files and 58,258,484 terrain cells. The only raw passability values
it found were 0 and 1. Earlier 5517 spot checks, including the released collision tests, treat an
effective value of `0` as statically passable. The composed collision provides a conservative static
passability check: a cell with `PassabilityFlag == 0` passes, and any nonzero or out-of-bounds value
fails. The check says nothing about movement permission, occupancy by other entities, portals, map
flags, elevation changes, or whether retail intended a given spawn point.

Map `1010` is BirthVillage in `cq_map`, and the 5095 `newbie.DMap` is 132x132. The proposed example
coordinate `(61,101)` is blocked in that raw map and in sampled 5517 references. The shipped 5517
composed collision is what counts, and tests fail if its effective passability ever disagrees with
these sources. No default spawn point is installed.

Some historical map IDs point to different assets in different versions (for example 1039, 1068, and
1766), and the older archive is missing nine Kunlun files that it references. Set aside any mapping
that fails. Do not assign it a guessed terrain.

## Modern database mapping rules

Treat the historical MyISAM `.frm/.MYD/.MYI` files as offline source snapshots. Do not attach them
to the MySQL 8.4 runtime storage, convert their DDL into production migrations automatically, or
reuse legacy auto-increment IDs as domain identities. Future imports should run in typed stages that
keep provenance and spell out each mapping:

1. **Read and fingerprint:** Record the archive hash, the executable or module hash where relevant,
   the source table or file, the schema interpretation, the source record ID, and the original
   bytes. Reject duplicate source IDs and malformed fields instead of trimming them.
2. **Normalize to domain types:** Define `MapId`, `MapDataId`, the item-type ID, NPC and monster
   identities, and foreign-key relationships. Keep unknown fields in staging data, not in ad hoc
   live columns. Validate encodings and numeric ranges before converting.
3. **Cross-check 5517:** Where they apply, the 5517 client and the versioned content in this
   repository take precedence. Any mechanic found only in 5095 stays labeled provisional. Each
   version difference needs a recorded decision and a test. Do not let the last import win.
4. **Model durable configuration separately:** Future normalized tables for shops, NPCs, quest and
   action definitions, monster definitions and spawns, and portal links need defined unique keys,
   relationships, transaction scopes, and read models. Add an EF migration only after that design is
   settled.
5. **Publish atomically:** Stage validated records in a separate import area, report unresolved
   references, compare counts and digests (which must be reproducible), and then promote an
   approved, immutable revision. Startup must never read partly imported game content. Do not run
   historical executables to build a catalog.

The Game database holds instances (`items`, `characters`, skills, social relationships, syndicates)
and uses separate permissions for reads and for character creation. The JSON item-type catalog holds
immutable definitions. `cq_goods` holds historical shop offerings, which are not character
inventories. `cq_itemtype` holds historical item definitions, which do not correspond to the `items`
rows. A future design may move a definition catalog into the Game database. If it does, the database
must replace the current runtime source instead of duplicating it, and releases must still be
versioned and validated with reproducible output.

The 5095 `cq_action` export has 68,017 physical records, `cq_task` has 8,336, `cq_generator` 1,846,
`cq_portal` 274, and `cq_passway` 286. These counts do not show which features are active in 5517.
The action graph has 93 nonzero links to action IDs that do not exist. Before any action runs, an
NPC or quest import must validate branch closure (every branch target exists), operation signatures,
graph execution limits, and data dependencies. Raw action parameters are never treated as executable
code or trusted SQL.

## Targeted binary research before larger imports

Answer specific unknowns by analyzing the binaries whose module hashes are listed in the analysis
archive. The `MsgServer.exe` used for reverse engineering matches the archived backup executable,
not the active binary, and `NpcServer.exe` also differs. Do not attribute code from one build to
another without confirming the hash or version.

Open questions, in priority order:

1. How 5095 code combines base terrain, scenery, and portal passability.
2. Which shop source (`Shop.dat` or `cq_goods`) wins at runtime.
3. How `cq_action` type codes and `cq_task` branches execute, including absent links.
4. How spawn-generator records map to persistent NPC and monster definitions.

Each finding should include the module hash, the function address or call chain, the affected source
records, version limits, and a minimal regression fixture that gives the same result on every run.
Analysis of the 5095 login protocol is out of scope.

This research does not enable character creation, change the live item-type JSON, import game-data
rows, or migrate the Game database.
