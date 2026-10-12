# Item type catalog v2 — source-verified static metadata

The GameServer has **one** authoritative runtime item catalog:
`content/items/item-types.json`. The original 5517 retail `itemtype.dat` is an
**offline import source** and is never read by the game process. The complete decoded
59-field native record is preserved outside runtime by `export-item-type-source`.

## Compatibility contract

Format v2 adds the source-file SHA-256 at the root and six native fields to each
existing item type record. Existing v1 fields retain their exact value and type.
No gameplay policy changes are implied by the additional metadata.

| Runtime name | Native field | C# type | Status / restriction |
| --- | ---: | --- | --- |
| `requiredProfessionCode` | 2 | `int` | Corroborated across 5095 schema and 5517 data; not yet an enforced requirement |
| `monopolyFlags` | 10 | `uint` | Historical monopoly/trade column; no flag interpretation approved |
| `silverPrice` | 12 | `uint` | Historical silver price; shop precedence and discounts not approved |
| `attackRange` | 32 | `int` | Cross-version field alignment; native 5517 server enforcement unconfirmed |
| `attackSpeed` | 33 | `int` | Cross-version field alignment; native 5517 server enforcement unconfirmed |
| `conquerPointsPrice` | 37 | `uint` | Historical emoney price; shop precedence unconfirmed |

The generated runtime definition has 16 typed properties. Additional fields retain their
original **indexed text** in the offline evidence export, not as 43 additional managed strings
per item in GameServer. This avoids duplicating source authority and unnecessary runtime allocations.

The pre-existing runtime `speedPercentOffset` property remains unchanged in this release.
Field 18 corresponds to historical `dodge` and proven 5517 mount-speed usage. Neither
meaning is broadened to boots or combat until native evidence resolves it.

## Release generation

The release must be generated from the **exact** audited retail `itemtype.dat`:

`90e9c983b0f78337f20720a3cf61d8f1392f23979565da14eb01dc514e3a71e7`

`generate-item-types` emits format v2 with all six additional native values,
source fingerprint and deterministic numeric ordering. The release step independently
compares the output to the 59-field native evidence export and to the v1 production catalog.
It rejects changed existing values, missing IDs, unverified source bytes, output over 16 MiB,
and inconsistent or out-of-range fields. It regenerates twice to detect nondeterminism.
Only after validation does it replace the release JSON and update the pinned SHA-256 test.

`FileItemTypeCatalogRepository` intentionally rejects v1 when running a v2 build.
The content and loader must be shipped together. Do not backport the new runtime onto a
v1 artifact or publish partially generated content.

No database migration, client protocol change, world-state modification, additional runtime
service, or character/login/realm routing change is involved.

This release does **not** implement shopping, trades, equipment permission enforcement,
or combat-speed calculations. Each mechanic requires separately reviewed source evidence,
server-side invariants, and regression tests.
