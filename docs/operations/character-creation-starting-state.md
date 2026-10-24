# Character creation: starting state

You can optionally configure the starting state for new characters, and the GameServer validates it
at startup. This does not enable character-creation packet handling or database writes.

## Configuration

The `GameServer:CharacterCreation` section is left out of `appsettings.json` on purpose. Supply it
yourself through the usual configuration providers.

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

Choose a starting map and coordinates from your 5517 map content. No default spawn point is
provided.

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

The coordinate previously used in tests and examples, `mapId 1010, (61,101)`, is blocked in sampled
5095/5517 references and is not an approved spawn. A released 5517 content test checks that it stays
blocked in the composed collision. Do not switch to a nearby coordinate that is passable in the raw
map without the same separate approval.

## Deployment security

This configuration is an opt-in prerequisite. It does not turn on character creation. It registers
the creation store and service over `ConnectionStrings:Game`, changes no database schema, and gives
no login path the ability to create a character. The authenticated creation route still ends
without creating anything.

Supply this configuration only when the source data and an approved spawn point are ready. The
GameServer identity's `INSERT` on `characters` is listed in
[Database identities](database-identities.md#gameserver-runtime-per-realm).
