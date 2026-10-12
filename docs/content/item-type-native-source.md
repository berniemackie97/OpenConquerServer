# 5517 item definitions: native-field evidence export

The original 5517 client `itemtype.dat` contains 59 positional decoded fields per item. The
version-2 production `content/items/item-types.json` retains 16 selected typed fields,
including the previously shipped ten fields and six additional native requirement/economy/attack
values. The 59-field evidence export remains the **lossless decoded-field source**, independent
of the deliberately smaller runtime projection. Original encoded bytes are SHA-256 pinned.
No exported value automatically becomes an authorized gameplay rule.

## Generate the native-field evidence

Run from the repository root with the **original audited 5517** `itemtype.dat` (not an older 5095
copy), outside the runtime content directory:

```bash
dotnet run --project tools/OpenConquer.GameData.Tool -c Release -- \
  export-item-type-source /absolute/path/to/5517/itemtype.dat /absolute/path/to/item-type-source.json
```

Expected original source SHA-256:

`90e9c983b0f78337f20720a3cf61d8f1392f23979565da14eb01dc514e3a71e7`

**Do not use or approve an exported file if its `sourceSha256` differs**. The exporter is a
general native-format tool; it deliberately does not pretend every input is 5517.

The version-1 evidence output contains `formatVersion`, `sourceFormat`, `sourceSha256`,
`fieldCount`, and an `itemTypes` array. Each record has its numeric `itemTypeId` and precisely
59 **decoded, unmodified, positional text fields**. Entries are ordered by numeric ID.
The writer rejects duplicate/zero IDs in the effective record set, inconsistent encoded IDs,
empty source bytes, and incorrect field counts. The native decoder itself applies last-record-wins
semantics to duplicate physical source lines. The writer stages and atomically replaces its output. Source hashes and
output hashes are reported by the tool. Two exports from the same source produce identical bytes.

This is a *decoded field-level record snapshot*, not a byte-for-byte copy of the encrypted source.
Original encoded bytes remain in the pinned source file, identified by SHA-256. The existing
native decoder retains the **last record** on repeated item-type ID; the exported document
likewise captures the effective unique records rather than claiming to preserve duplicate
physical lines or original line endings.

## Interpreting fields

| Native index | Established evidence | Implementation note |
| ---: | --- | --- |
| 0 | Item type identifier | Authoritative numeric record key |
| 1 | Native item name | Preserve original `~` conventions here |
| 2 | Required profession | Corroborated by 5095 schema; verify 5517 gameplay usage before enforcing |
| 4 | Required level | Established native 5517 use |
| 10 | Monopoly / trade restriction | Meaning/flag combinations require verification before enforcement |
| 12 | Silver price | Historical schema alignment; verify 5517 pricing behavior |
| 18 | Historical `dodge`; native 5517 mount-speed use proven | **Do not rename to generic dodge or use for boots without verified behavior** |
| 19 / 20 | Life / mana bonuses | Verified native 5517 equipment calculations |
| 21 / 22 | Initial / maximum durability | Already in runtime v1 projection |
| 32 / 33 | Attack range / speed | Cross-version field mapping; confirm 5517 runtime behavior |
| 37 | Conquer Points shop price | Historical schema alignment; verify 5517 shop precedence |
| 39 / 47 | Static lifetime / stack capacity | Already in runtime v1 projection |
| 53 / 54 | Type / item descriptions | Decoded native text, no assumed gameplay effect |

All other positions are deliberately retained as **indexed source values** until their meaning
and signedness are established; they must not silently become authoritative gameplay policies.
The full field mapping and confidence classifications are in
`20_notes/Server/historical-reconciliation-5095.md` and the accompanying
`field-mapping-matrix.csv` research dataset.

## Production boundary

The native export is offline import/evidence. Neither the 59-field source JSON nor the original
encoded client file is shipped to GameServer. The production release generator promotes selected
verified native values to `item-types.json` version 2, retaining the remaining positional fields
in the separately hash-pinned evidence export. The on-disk v2 release contains 16 fields per item
plus a source fingerprint. The GameServer parses those into an immutable catalog at startup.

The v2 conversion is conditioned on an exact 5517 source hash, 14,981 item IDs, field-by-field
comparison against the native export, unchanged v1 values, deterministic output, and a pinned
release hash. See [Item catalog v2](item-type-catalog-v2.md). The native field 18 retains the
existing `speedPercentOffset` API for compatibility; historical `dodge` and native mount-speed
interpretations must not be conflated.

All game mechanics using professions, trading, prices, or attack timing require separate evidence
and explicit server-side validation before being activated. Never substitute 5095 values for 5517.
