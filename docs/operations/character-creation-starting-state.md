# Character creation: starting state

You can optionally configure the starting state for new characters, and the GameServer validates it
at startup. This also explicitly enables the authenticated creation route. The
source-backed Birth Village portal0 candidate is enabled in Development only;
production still requires an explicit starting-state deployment decision and
verification of database grants.

## Configuration

The `GameServer:CharacterCreation` section is left out of production `appsettings.json`.
The checked-in `appsettings.Development.json` enables the map-1010 portal0
candidate at (60,108) with the six allotments below for local integration tests.
Production deployments must explicitly supply their validated starting state.

| Setting | Meaning |
| --- | --- |
| `GameServer:CharacterCreation:StartingMapId` | Nonzero world map ID |
| `GameServer:CharacterCreation:StartingX` | Zero-based `ushort` X coordinate |
| `GameServer:CharacterCreation:StartingY` | Zero-based `ushort` Y coordinate |
| `GameServer:CharacterCreation:Allotments` | Exactly six profession-family entries |
| `Allotments:*:Family` | One unique family from `1`, `2`, `4`, `5`, `6`, `10` |
| `Allotments:*:Strength` | Level-one STR |
| `Allotments:*:Agility` | Level-one AGI |
| `Allotments:*:Vitality` | Level-one VIT |
| `Allotments:*:Spirit` | Level-one SPI |

Each of the six family rows must add up to ten attribute points. Take the values from the corrected
auto-allot data. The ten-point check cannot tell whether a distribution that adds up to ten actually
matches that source.

Level-one starting allotments from the audited OpenConquerPublic reference:

| Family | STR | AGI | VIT | SPI |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 5 | 2 | 3 | 0 |
| 2 | 5 | 2 | 3 | 0 |
| 4 | 2 | 7 | 1 | 0 |
| 5 | 1 | 8 | 1 | 0 |
| 6 | 3 | 1 | 4 | 2 |
| 10 | 0 | 2 | 3 | 5 |

The 5095 MyISAM `cq_point_allot` table has an older family-5 level-one row `(2,7,1,0)` and no
family-6 row. Neither difference overrides the later-version values above. See
[Historical source reconciliation](../content/historical-source-reconciliation.md).

The historical TQ 15.10 creation routine initializes Birth Village (map 1010)
from its `portal0` location, and the archived 5517 map catalog records
`portal0=(60,108)`, with `portal0_walkable=true` and map data ID 1010.
The released composed-collision test checks (60,108) is passable and that
the older sample (61,101) remains blocked. These sources justify the
Development candidate; they do not demonstrate that every retail 5517
realm created every character at precisely this tile.

## Validation

Configuration binding rejects unknown properties and values that cannot be converted to the target
number type. Building the starting state then rejects missing, duplicate, or unsupported allotments,
allotments that do not add up to ten points, and a zero starting map ID.

After loading the map definitions, terrain, and composed collision, world startup requires that the
configured map exists, the coordinates are in bounds, and the effective static collision cell has
`PassabilityFlag == 0`.

The check uses the `.ocmc` scenery override where one exists, instead of only the original `.ocbt`
base-grid flag. Any nonzero flag, including an unknown one, is rejected. These checks run before
`WorldRuntime.Start`, before the world is published, and before any network listener opens. When
creation is not configured, login for existing characters starts as usual with no spawn check.

This is a conservative static safety check. Passing it does not prove the tile is walkable in every
sense, and it does not mean creation can be enabled. Portal rules, occupancy by other entities, NPC
placement, per-map restrictions, and whether retail meant players to spawn there are still left to
the runtime and to further research.

The previously suggested `mapId 1010, (61,101)` tile is blocked in sampled
5095/5517 references. The composed-collision regression test checks both
its blocked status and the portal0 candidate's passability, rather than
assuming any neighboring raw-terrain cell is a valid spawn.

## Deployment security

This configuration opts into live character creation. It registers the store and
service over `ConnectionStrings:Game`, changes no database schema, and permits only
an authenticated character-less GameServer connection to create a character once.
A successful creation requires a subsequent same-session game-login proof before
existing-character world admission. Unconfigured realms remain unchanged.

In local Development, use the source-backed portal0 candidate to exercise
the authenticated creation path. For production, approve the deployment's
spawn and confirm the GameServer identity has `INSERT` on `characters` as listed in
[Database identities](database-identities.md#gameserver-runtime-per-realm).
The live client role UI still requires the exact English 5517
`ini/selectCharacter.ini` asset and a functioning world renderer before
end-to-end login can be certified.
