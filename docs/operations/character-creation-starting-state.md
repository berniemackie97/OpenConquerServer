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

The 5095 MyISAM `cq_point_allot` contains an older family-5 level-one row
`(2,7,1,0)` and no family-6 row. Neither supersedes the verified later-version
policy. See [Historical source reconciliation](../content/historical-source-reconciliation.md).

Deployments must choose a starting map and coordinates from their canonical
**5517** map content. This package intentionally provides **no default spawn point**.

## Validation boundaries

Configuration binding rejects unknown properties and invalid numeric conversions.
Construction rejects missing, duplicate, unsupported, or budget-invalid allotments
and a zero starting map identity.

After loading canonical definitions, terrain, and composed collision, world startup
requires the configured map to exist, the coordinates to be in bounds, and the
**effective static collision** cell to have `PassabilityFlag == 0`.

The predicate uses the final `.ocmc` scenery override where present, not merely the
original `.ocbt` base-grid flag. An unknown nonzero flag is rejected, not treated as
walkable. Failures occur before `WorldRuntime.Start`, world publication, and opening
network listeners. When creation is unconfigured, ordinary existing-character login
starts without an additional spawn requirement.

This is a **conservative static safety gate**, not comprehensive walkability or
permission to enable creation. It does not establish portal rules, dynamic occupancy,
NPC placement, per-map restrictions, or retail spawn intent. Those remain explicit
runtime and evidence obligations.

The previous test/example coordinate `mapId 1010, (61,101)` is blocked in sampled
5095/5517 references and is not an approved spawn. The released 5517 content test
pins this regression against the canonical composed collision. Do not promote a
nearby raw-passable coordinate without the same independent approval.

## Deployment security

The feature is an opt-in readiness prerequisite, not a character-creation
feature flag. It registers no write credentials, changes no database schema,
and makes no login path capable of creating a character. The existing
authenticated creation route still ends without performing creation.

Supply this configuration only when authoritative data and an approved spawn
are ready. Follow the normal secret-management process for future separate
creation credentials; do not reuse the read-only GameServer database identity
for writes.
