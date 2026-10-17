# Revival destination authority — slice #151

## Scope

Native 5517 **client** action 94 supplies a mode (0 normal, 1 here) but no destination. The 15.10 TQ server's `BeKill` records `current-map.reborn_mapid` and the **target** map's `portal0_x/y`, and its normal Reborn flies there. TQ's target selection is historical server evidence, not a verified retail 5517 implementation. The target map's portal, never the source map's portal, is used in this slice.

This PR publishes an **independent, immutable, query-only destination catalog** with explicit failure states. It does **not** enable action 94, change character HP, transfer world residency, alter persistence or create any gameplay commands. A follow-up PR must integrate it with atomic inter-partition transfer, persistence, authoritative status lifecycle, and client synchronization. This is a release-ready foundation, **not** a completed revival gameplay feature.

## Historical data and reproducibility

`content/maps/revival-destinations.json` is derived only from the supplied `LongV6270-main.zip` → `LongV6270-main/LongV6270-main/sql/game_cq.sql`, `cq_map` rows. SHA-256 of that unmodified SQL file:

`867b412f67e370ae2729683f229be78723514a9127b3177baa3067cb72a2f9ba`

Recorded columns of `cq_map`: `id` (0), `portal0_x/y` (11, 12), `reborn_map` (13). Preserve these exact numbers; never rebase a portal on the source map, round a coordinate, infer from map names, or substitute link-point rules from a newer version.

The checked-in 5517 world release currently contains 282 world-map definitions; the broader research covers 289 including 7 deliberately unavailable world identities. The intersection of those 282 with the supplied later-source SQL contains **207** rows. The other 75 maps have no row in this evidence-only import. Some are resolved by other lineages in research notes but **are not imported here** without the vetted per-map source. Do not interpret 207 as the total number of historical revival mappings, or confuse this import with the research's 227/289 resolved count.

Each row retains `provenance: LongV6270`. The release does not claim 5517 retail server proof. `formatVersion: 1`, numeric fields, and provenance are strictly validated; duplicates, unknown fields, wrong types and unbounded content fail parsing.

## Runtime resolution contract

`RevivalDestinationCatalog` compiles a bounded immutable result for every **loaded** world-map identity, using the source evidence and composed collision terrain. The resolver is O(1) and performs no file I/O or pathfinding. The source's `rebornMapId` selects an existing target map; only the **target record's** `portal0X/Y` is considered. The composed collision must mark the tile passable and the coordinate must fit both the native uint16 wire range and map bounds. A zero target portal is missing, not a presumed `(0,0)` spawn.

Every unresolved state is explicit: missing historical row, unset reborn map, target map unavailable, missing target evidence, missing target portal, or invalid/blocked target tile. There are no implicit success values or fallback map IDs. The destination resolver never sends a packet or changes world state.

Verification cases: Twin City 1002 → 1002 (430,380); Tiger Cave 1013 → Phoenix Castle 1011 (193,266); 2060 has a zero target portal; Market 1036's point is blocked under the chosen composed terrain; Frozen Grotto 1926 lacks a Long source record in this release and remains unresolved rather than assuming Desert City. A future source reconciliation may fill in the 75 missing mappings with provenance and terrain checks.

## Next integration gates

1. Authoritative world **cross-partition transfer** with generation-validated ownership relocation, capacity reservations, ordered visibility departure/arrival, rollback on failure, and cancellation rules.
2. Persisted map and position changes (including crash/reconnect consistency and what to do if a durable write has an uncertain result).
3. Partition-owned maximum life/mana/stamina and blessing/shackle/status lifetimes with explicit server timers and persistence semantics.
4. Native action `10010/94` request handling with 0/1 mode and 20-second ghost-wait policy; spell resurrection is a separate trusted combat operation.
5. Sequential `10017` status/look/life/mana/energy synchronization and `10010/86` plus map-entry sequencing confirmed against the 5517 client. The native packet layouts must be established before implementing writes.
6. Restricted maps and cross-server authorization, and 10-second combat protection integrated with authoritative combat hooks; neither may be represented as operational before it is enforced.
7. Two-client runtime tests with a live 5517 client for revive here at the ghost's **current tile**, normal reborn to the **target town**, peer visibility, and reconnect after revival.

The available native client research is `20_notes/Server/death-ghost-revive-5517.md`, revision 2; source categories are C = verified 5517 client, S = TQ 15.10, X = community, U = unavailable retail 5517 server internals. User recollection supplies target-era behavior of ghost-current-position Revive Here, full HP/mana/stamina restoration, and the 20-second wait for both self-revival buttons. None of that is implemented by this PR.
