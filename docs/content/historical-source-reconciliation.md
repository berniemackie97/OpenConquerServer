# Historical source reconciliation (5095 evidence versus 5517 authority)

This is the source and migration contract for evaluating the separately supplied 5095-era
`ZFSERVER.zip`, `zfServerRE.zip`, and derived
`OpenConquer-5095-Server-Evidence-Analysis.zip` against the maintained 5517 server.
The historical archives are **research input**, not the runtime database, an approved data release,
or proof of 5517 retail server behavior. No historical accounts, credentials, characters, or logs
belong in an import pipeline for the modern server.

## Current authoritative boundaries

| Area | Current implementation | Historical evidence and reconciliation rule |
| --- | --- | --- |
| World map identities | `content/maps/map-definitions.json` contains 282 `mapId`, `mapDataId`, 64-bit flags records | 5095 `GameMap.dat` lists 169 IDs, `cq_map` has 240 rows, and FoxConquer 5517's `GameMap.dat` has 262 mappings. These sources do not form one interchangeable catalog. Resolve each map identity, terrain binding, and flags separately. |
| Base terrain | 185 canonical `content/maps/terrains/<MapDataId>.ocbt` objects, imported from the 5517 client with per-row checksum checks | 155 archived 5095 `map/map/*.DMap` grids validate at the row level. Use them for cross-version evidence; never overwrite verified 5517 terrain with 5095 bytes. |
| Composed static collision | 185 `.ocmc` artifacts, SHA-256 bound to their matching `.ocbt` file | The archived `.Part` scenery files contain readable collision cells. They inform attachment semantics; they must not be pasted into the 5517 collision catalog without matching source and composition verification. |
| Item type definitions | `content/items/item-types.json`, 14,981 IDs, hash-pinned from the 5517 client `itemtype.dat` | Historical `cq_itemtype` is an older definition source. A same-numbered type need not have the same stats, level, encoding, or compatibility fields. Diff fields and provenance; do not add a second implicit runtime source. |
| Owned items | Modern `openconquer_game.items` rows, owned by `characters`; validated against `ItemTypeCatalog` | Legacy item instances and item definitions are different data categories. The existing database schema stores durable owned instances, not a duplicate 5517 item-type table. |
| Shops | No active canonical shop catalog or shop persistence contract yet | 5095 `Shop.dat` lists 34 shops/1,370 slots, while `cq_goods` has 1,587 rows for 33 shop IDs. Their disagreements require exact binary-version and call-site attribution before importing offerings. |
| Character starting attributes | Six selected families, configured immutable starting-state allotments | Legacy `cq_point_allot` has five families across 120 levels; its Ninja level-one row differs, and Monk is absent. Version-specific 5517 data remains authoritative. |

See [Content](README.md), [Base terrain](base-terrain-format.md),
[Collision](collision-format.md), and [Persistence](../architecture/persistence.md).

## Map provenance and collision rules

A `mapId` is a server world location; `mapDataId` identifies the client terrain;
multiple maps may reference the same terrain. These IDs must **never** be inferred from
legacy filenames or assumed equal. `MapDefinition` retains the full 64-bit flag value.
The existing import manifest identifies seven omitted maps with unresolved terrain and
48 zero-flag entries without source flag evidence: zero in that case means *unknown
source*, not proven retail flags. Do not silently fill these fields with historical values.

`OpenConquer.GameData.Tool` already validates `GameMap.dat`, source DMap grids,
row checksums, scenery object references, attachment budgets, and the generated
`.ocbt`/`.ocmc` formats. At runtime, the map-content loader verifies every required
artifact, checksum, map identity, terrain fingerprint, and collision composition.
These are integrity and provenance boundaries, not claims that the map is fully simulated.

The 5095 DMap audit covers 155 files and 58,258,484 terrain cells. Its observed
raw passability values were only 0 and 1. Earlier 5517 spot checks, including the
existing released collision tests, interpret effective `0` as statically passable.
This slice adds a *conservative static* predicate over the **composed** collision:
`PassabilityFlag == 0` is accepted; any nonzero or out-of-bounds value is rejected.
It does not decide movement permission, dynamic entity occupancy, portals, map flags,
elevation transitions, or whether a particular spawn was intended by retail.

Map `1010` is BirthVillage in `cq_map`; the 5095 `newbie.DMap` is 132×132.
The candidate example coordinate `(61,101)` is blocked in that raw map and in
sampled 5517 references. The shipped 5517 composition is the actual authority:
tests must fail if its effective passability unexpectedly contradicts this evidence.
No default spawn is installed by this change.

Some historical map identifiers point to different assets across versions
(e.g. 1039, 1068, 1766); the older archive also lacks nine referenced Kunlun
files. Failed mappings must be quarantined rather than assigned guessed terrain.

## Modern database mapping rules

Use the historical MyISAM `.frm/.MYD/.MYI` files as **offline source snapshots**.
Do not attach them to MySQL 8.4 runtime storage, translate DDL automatically into
production migrations, or treat legacy auto-increment IDs as the new domain identity.
Future imports should have typed stages with preserved provenance and explicit mappings:

1. **Read and fingerprint:** Pin archive hash, executable/module hash when relevant,
   source table/file, schema interpretation, source record ID, and original bytes.
   Reject duplicate source IDs and malformed fields rather than silently trimming them.
2. **Normalize to domain types:** Define `MapId`, `MapDataId`, item-type ID,
   NPC/monster identities, and foreign relationships explicitly. Preserve unknown
   fields in staging/evidence, not arbitrary live columns. Validate encodings and
   numeric ranges before conversion.
3. **Cross-check 5517:** Prefer the native 5517 client and canonical versioned
   content where applicable. Any 5095-only mechanic remains labeled provisional.
   Version differences require deliberate decisions and tests, not last-write-wins.
4. **Model durable configuration separately:** Future normalized tables for shops,
   NPCs, quest/action definitions, monster definitions and spawns, and portal links
   must have explicit unique keys, relationships, transaction boundaries and
   read models. Add an EF migration only after those contracts are established.
5. **Publish atomically:** Stage validated records in isolated import space,
   report unresolved references, compare deterministic counts and digests, and
   promote an approved immutable revision. Startup must never read partially
   imported game content. Do not run historical executables to build a catalog.

The current Game database owns **instances** (`items`, `characters`, skills,
social relationships, syndicates) and uses distinct permissions for reads and
character creation. The JSON item-type catalog owns immutable **definitions**.
`cq_goods` represents historical shop **offerings**, not character inventories;
`cq_itemtype` represents historical item **definitions**, not the modern `items`
rows. A future design may deliberately move a definition catalog into the modern
Game database, but must replace—not duplicate—the existing runtime authority and
retain deterministic versioned release validation.

The 5095 `cq_action` export has 68,017 physical records, `cq_task` 8,336,
`cq_generator` 1,846, `cq_portal` 274, and `cq_passway` 286.
These counts do not prove active 5517 features. The action graph includes 93
nonzero links to absent action IDs; NPC/quest import must validate branch closure,
operation signatures, graph execution limits, and data dependencies before running
any action. Raw action parameters are never executable code or trusted SQL.

## Targeted binary research before larger imports

Use Codex with the **exact module hashes** in the evidence package to answer
specific unknowns. The reverse-engineering `MsgServer.exe` matches the archived
backup executable, not the active binary; `NpcServer.exe` also differs. Do not
attribute one build's code to another without hash/version confirmation.

Priority questions: (1) how 5095 code combines base terrain, scenery and portal
passability; (2) which shop source (`Shop.dat` or `cq_goods`) wins at runtime;
(3) how `cq_action` type codes and `cq_task` branches execute, including absent
links; (4) how spawn-generator records map to persistent NPC/monster definitions.
Each finding should include module hash, function address/call chain, affected
source records, version limits, and a minimal deterministic regression fixture.
5095 login protocol analysis is intentionally out of scope.

Nothing in this evidence or this slice enables character creation, changes the
live item-type JSON, imports game-data rows, or migrates the Game database.
