# Character creation starting-state readiness

The GameServer supports **optional configuration and startup validation** of the starting state.
It does **not** enable character-creation network processing or database writes.

## Configuration

The `GameServer:CharacterCreation` section is deliberately absent from `appsettings.json`.
Operators must supply it explicitly through the existing configuration providers.

| Setting | Meaning |
| --- | --- |
| `GameServer:CharacterCreation:StartingMapId` | Nonzero canonical world-map identity |
| `GameServer:CharacterCreation:StartingX` | Zero-based `ushort` X coordinate |
| `GameServer:CharacterCreation:StartingY` | Zero-based `ushort` Y coordinate |
| `GameServer:CharacterCreation:Allotments` | Exactly six profession-family entries |
| `Allotments:*:Family` | One unique family from `1`, `2`, `4`, `5`, `6`, `10` |
| `Allotments:*:Strength` | Level-one STR |
| `Allotments:*:Agility` | Level-one AGI |
| `Allotments:*:Vitality` | Level-one VIT |
| `Allotments:*:Spirit` | Level-one SPI |

The six family rows must each sum to ten attribute points. The configured values must
be sourced from the established corrected auto-allot content: the ten-point check
alone does not prove that an arbitrary distribution matches that source.

Level-one starting allotments from the audited OpenConquerPublic reference:

| Family | STR | AGI | VIT | SPI |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 5 | 2 | 3 | 0 |
| 2 | 5 | 2 | 3 | 0 |
| 4 | 2 | 7 | 1 | 0 |
| 5 | 1 | 8 | 1 | 0 |
| 6 | 3 | 1 | 4 | 2 |
| 10 | 0 | 2 | 3 | 5 |

Deployments must choose a starting map and coordinates from their canonical
map content. This package intentionally provides **no default spawn point**.

## Validation boundaries

Configuration binding rejects unknown properties and invalid numeric conversions.
Construction rejects missing, duplicate, unsupported, or budget-invalid allotments
and a zero starting map identity.

After loading canonical definitions, terrain, and composed collision, world
startup requires the configured map identity to exist and both coordinates
to fall within the collision grid dimensions.

Validation runs before `WorldRuntime.Start`, before world publication, and before
network listeners start. Invalid configured content fails startup closed.

Map bounds are not equivalent to walkability. The raw `PassabilityFlag` is not
yet assigned a proven complete movement policy; this gate intentionally makes
no assertion that a particular bounded coordinate is walkable.

## Deployment security

The feature is an opt-in readiness prerequisite, not a character-creation
feature flag. It registers no write credentials, changes no database schema,
and makes no login path capable of creating a character. The existing
authenticated creation route still ends without performing creation.

Supply this configuration only when authoritative data and an approved spawn
are ready. Follow the normal secret-management process for future separate
creation credentials; do not reuse the read-only GameServer database identity
for writes.
