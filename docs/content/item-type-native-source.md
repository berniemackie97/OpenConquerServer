# 5517 item definitions: native-field evidence export

The production `content/items/item-types.json` format version 1 is an intentional, ten-field
projection of the original 5517 client `itemtype.dat`. The decoder already reads 59 decoded text
fields per native record; the production projection does **not** currently retain 49 fields.
The field loss is real: the 2026-10-12 historical reconciliation found 43 of those 49 positions
populated in the 5517 source. This export closes the offline evidence-retention gap without
pretending that unverified field semantics are server gameplay rules.

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
The exporter checks duplicate/zero IDs, inconsistent encoded IDs, source bytes, and field counts;
it writes through a temporary file then atomically replaces the destination. Source hashes and
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

## Production boundary and next slice

This command is an **offline import/evidence operation**. It does not modify `item-types.json`,
its pinned hash, GameServer startup, domain item semantics, MySQL, or gameplay behavior. Neither
`item-type-source.json` nor original client `itemtype.dat` is shipped to GameServer by this slice.

Before promoting production item catalog format version 2, audit this export against the original
source hash and all 14,981 IDs, approve typed fields with their verified widths/ranges, establish
how unknown positions remain lossless without unnecessary runtime allocations, then update the
writer, reader, domain contracts, released JSON, tests, docs, and content hash **in one release**.
Never manufacture absent native values or use 5095 values as 5517 defaults.
