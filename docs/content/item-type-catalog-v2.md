# Item type catalog v2

The GameServer reads item types from one file, `content/items/item-types.json`. The 5517 retail
`itemtype.dat` is only an offline import source, and the game process never reads it.
`export-item-type-source` keeps the complete decoded 59-field retail record outside the runtime.

## Compatibility with version 1

Format v2 adds the source file's SHA-256 at the root and six retail fields to each item type record.
Fields from v1 keep their values and types. The new fields do not change any gameplay rule.

| Runtime name | Source field | C# type | Status / restriction |
| --- | ---: | --- | --- |
| `requiredProfessionCode` | 2 | `int` | Matches the 5095 schema and the 5517 data. Not enforced as a requirement yet. |
| `monopolyFlags` | 10 | `uint` | Historical monopoly/trade column. No meaning for the flags has been approved. |
| `silverPrice` | 12 | `uint` | Historical silver price. Shop precedence and discounts are not approved. |
| `attackRange` | 32 | `int` | Field position matches across versions. Whether the 5517 server enforces it is unconfirmed. |
| `attackSpeed` | 33 | `int` | Field position matches across versions. Whether the 5517 server enforces it is unconfirmed. |
| `conquerPointsPrice` | 37 | `uint` | Historical emoney price. Shop precedence is unconfirmed. |

The generated runtime definition has 16 typed properties. The remaining fields stay in the offline
export as indexed text. The GameServer does not carry them as 43 extra managed strings per item,
which avoids a second copy of the source data and the extra runtime allocations.

The existing runtime `speedPercentOffset` property is unchanged. Field 18 is `dodge` in the
historical schema and is confirmed as mount speed in 5517. Neither meaning is extended to boots or
combat until 5517 sources settle the question.

## Release generation

Generate the release from the audited retail `itemtype.dat` with this SHA-256:

`90e9c983b0f78337f20720a3cf61d8f1392f23979565da14eb01dc514e3a71e7`

`generate-item-types` writes format v2 with all six new fields and the source fingerprint, ordered
by numeric ID. The release step compares the output against the 59-field export and against the v1
production catalog. It rejects changed existing values, missing IDs, source bytes that fail the hash
check, output over 16 MiB, and inconsistent or out-of-range fields. It generates the file twice and
fails if the two runs differ. Only after these checks pass does it replace the release JSON and
update the pinned SHA-256 test.

In a v2 build, `FileItemTypeCatalogRepository` rejects v1 files, so the content and the loader must
ship together. Do not run the new loader against a v1 file, and do not publish partly generated
content.

The v2 format needs no database migration, client protocol change, world-state change, new runtime
service, or change to character, login, or realm routing.

Shopping, trading, equipment permission checks, and combat-speed calculations are not implemented.
Each of these needs its own reviewed sources, server-side checks, and regression tests.
