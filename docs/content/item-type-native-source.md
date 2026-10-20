# 5517 item definitions: full source-field export

The original 5517 client `itemtype.dat` holds 59 positional fields per item once decoded. The
version 2 production file, `content/items/item-types.json`, keeps 16 of them as typed fields: the
ten fields shipped earlier plus six retail requirement, economy, and attack values. The 59-field
export is the complete record of the decoded fields, separate from the smaller runtime file. The
original encoded bytes are pinned by SHA-256. No exported value becomes a gameplay rule on its own.

## Generating the export

Run from the repository root with the original, audited 5517 `itemtype.dat` (not an older 5095
copy), and keep the files outside the runtime content directory:

```bash
dotnet run --project tools/OpenConquer.GameData.Tool -c Release -- \
  export-item-type-source /absolute/path/to/5517/itemtype.dat /absolute/path/to/item-type-source.json
```

Expected original source SHA-256:

`90e9c983b0f78337f20720a3cf61d8f1392f23979565da14eb01dc514e3a71e7`

**Do not use or approve an exported file whose `sourceSha256` differs from this value.** The
exporter reads any file in this format and does not check that its input is the 5517 file.

The export (format version 1) contains `formatVersion`, `sourceFormat`, `sourceSha256`,
`fieldCount`, and an `itemTypes` array. Each record has its numeric `itemTypeId` and 59 decoded,
unmodified text fields in source order. Entries are ordered by numeric ID. The writer rejects
duplicate or zero IDs in the effective record set, inconsistent encoded IDs, empty source bytes, and
wrong field counts. When the source repeats a line for the same ID, the decoder keeps the last one.
The writer stages its output and replaces the file atomically. The tool reports the source and
output hashes. Two exports from the same source produce identical bytes.

The export is a snapshot of the decoded fields of each record. It is not a byte-for-byte copy of the
encrypted source, whose original bytes stay in the pinned source file identified by SHA-256. Because
the decoder keeps the last record for a repeated item-type ID, the export holds one effective record
per ID. It does not keep duplicate source lines or the original line endings.

## Interpreting fields

| Source index | What is known | Implementation note |
| ---: | --- | --- |
| 0 | Item type identifier | Numeric record key |
| 1 | Item name as stored in the client | Keep the original `~` conventions |
| 2 | Required profession | Matches the 5095 schema. Confirm 5517 gameplay use before enforcing. |
| 4 | Required level | Confirmed 5517 use |
| 10 | Monopoly / trade restriction | Confirm the meaning of each flag combination before enforcing |
| 12 | Silver price | Matches the historical schema. Confirm 5517 pricing behavior. |
| 18 | Historical `dodge`. Confirmed as mount speed in 5517 | Do not rename it to a generic dodge field or use it for boots until the behavior is confirmed |
| 19 / 20 | Life / mana bonuses | Confirmed in 5517 equipment calculations |
| 21 / 22 | Initial / maximum durability | Already in the v1 runtime file |
| 32 / 33 | Attack range / speed | Field position matches across versions. Confirm 5517 runtime behavior. |
| 37 | Conquer Points shop price | Matches the historical schema. Confirm 5517 shop precedence. |
| 39 / 47 | Static lifetime / stack capacity | Already in the v1 runtime file |
| 53 / 54 | Type / item descriptions | Decoded client text with no assumed gameplay effect |

All other positions stay as indexed source values until their meaning and signedness are known. None
of them may become a gameplay rule until then. The full field mapping and confidence ratings are in
`20_notes/Server/historical-reconciliation-5095.md` and the accompanying `field-mapping-matrix.csv`
research dataset.

## What ships

The export is an offline import and reference file. The GameServer ships with neither the 59-field
JSON nor the original encoded client file. The release generator copies selected, checked values
into `item-types.json` version 2 and leaves the other positional fields in the separately
hash-pinned export. The v2 file on disk has 16 fields per item plus a source fingerprint, and the
GameServer parses it into an immutable catalog at startup.

The v2 conversion requires the expected 5517 source hash, 14,981 item IDs, a field-by-field match
against the export, unchanged v1 values, output that is identical on every run, and a pinned release
hash. See [Item catalog v2](item-type-catalog-v2.md). Field 18 keeps the existing
`speedPercentOffset` API for compatibility. Do not mix up its historical `dodge` meaning with its
5517 mount-speed use.

Any game mechanic that uses professions, trading, prices, or attack timing needs its own sources and
server-side validation before it is turned on. Never substitute 5095 values for 5517 ones.
