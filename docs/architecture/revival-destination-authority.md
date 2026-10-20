# Revival destinations

## Scope

In the 5517 client, action 94 sends a mode (0 normal, 1 here) but no destination. The 15.10 TQ server's `BeKill` records `current-map.reborn_mapid` and the target map's `portal0_x/y`, and its normal Reborn sends the player there. This target selection comes from a historical TQ server and has not been confirmed as retail 5517 behavior. The catalog always uses the target map's portal, never the source map's.

`RevivalDestinationCatalog` is a standalone, immutable, read-only catalog of revival destinations, and each way a lookup can fail has a named state. It does not enable action 94, change character HP, move world residency, change saved data or add gameplay commands. Revival still needs to be connected to atomic transfer between partitions, persistence, server-owned status lifetimes and client synchronization. The catalog is ready to build on, but revival gameplay is not implemented.

## Historical data and reproducibility

`content/maps/revival-destinations.json` is built only from the `cq_map` rows of `LongV6270-main/LongV6270-main/sql/game_cq.sql` inside the supplied `LongV6270-main.zip`. SHA-256 of that unmodified SQL file:

`867b412f67e370ae2729683f229be78723514a9127b3177baa3067cb72a2f9ba`

Columns read from `cq_map`: `id` (0), `portal0_x/y` (11, 12), `reborn_map` (13). The values are kept as they appear in the source. A portal is never moved onto the source map, coordinates are not rounded, nothing is inferred from map names, and link-point rules from newer versions are not substituted.

The checked-in 5517 world release currently contains 282 world-map definitions. The wider research covers 289, including 7 world identities that are unavailable on purpose. 207 of those 282 maps have a row in the supplied later-version SQL. The other 75 maps have no row in this import, which only takes sourced data. The research notes resolve some of them from other lineages, but they are not imported here without a checked source for each map. 207 is therefore not the total number of historical revival mappings, and it is a different figure from the research's 227/289 resolved count.

Each row keeps `provenance: LongV6270`, and the release does not present any row as proof of the retail 5517 server's data. Parsing checks `formatVersion: 1`, the numeric fields and provenance, and rejects duplicates, unknown fields, wrong types and content over the size limits.

## Runtime resolution

`RevivalDestinationCatalog` precomputes one immutable result for each loaded world-map identity from the source data and the composed collision terrain. Lookups are O(1) and do no file I/O or pathfinding. The source map's `rebornMapId` selects an existing target map, and only the target record's `portal0X/Y` is used. The composed collision must mark the tile passable, and the coordinate must fit both the uint16 wire range of the client packet and the map bounds. A zero target portal counts as missing; it is not taken as a spawn at `(0,0)`.

Each unresolved case has its own state: missing historical row, unset reborn map, target map unavailable, missing target source data, missing target portal, or invalid or blocked target tile. There are no implied success values or fallback map IDs. The resolver never sends a packet or changes world state.

Test cases: Twin City 1002 resolves to 1002 (430,380). Tiger Cave 1013 resolves to Phoenix Castle 1011 (193,266). 2060 has a zero target portal. Market 1036's point is blocked in the chosen composed terrain. Frozen Grotto 1926 has no Long source record in this release and stays unresolved instead of assuming Desert City. A later review of other sources may fill in the 75 missing mappings, with provenance and terrain checks.

## Remaining work

1. Server-controlled world transfer between partitions: moving ownership with a generation check, capacity reservations, departure and arrival visibility in order, rollback on failure, and cancellation rules.
2. Saved map and position changes, including consistency across crashes and reconnects and what to do when a durable write has an uncertain result.
3. Maximum life, mana and stamina, and blessing, shackle and status lifetimes owned by the partition, with server timers and defined persistence rules.
4. Handling of action `10010/94` requests with mode 0/1 and a 20-second ghost wait. Spell resurrection is a separate trusted combat operation.
5. In-order `10017` status, look, life, mana and energy updates, and the `10010/86` and map-entry sequence, confirmed against the 5517 client. The packet layouts must be established before these writes are implemented.
6. Restricted maps and cross-server authorization, and 10-second combat protection tied into the server's combat hooks. Neither may be described as working before it is enforced.
7. Runtime tests with two clients, using a live 5517 client, for Revive Here at the ghost's current tile, normal reborn to the target town, peer visibility, and reconnecting after revival.

The client research is in `20_notes/Server/death-ghost-revive-5517.md`, revision 2. Its source categories are C = verified 5517 client, S = TQ 15.10, X = community, and U = unavailable retail 5517 server internals. User recollection supplies the target-era behavior: Revive Here at the ghost's current position, full HP, mana and stamina restoration, and the 20-second wait for both self-revival buttons. None of that is implemented yet.
