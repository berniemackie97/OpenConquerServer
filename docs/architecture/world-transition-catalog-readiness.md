# Transition catalog readiness

## Scope

The world cannot start without the validated transition catalog, which lists candidate destinations
for map exits taken from historical data and the 5517 client's exit markers. It is no longer an
optional, offline-only resource. Gameplay input still does not handle 5517 action 85, and no
characters are transferred.

## Startup and lifetime

1. Verify the Account and Game database versions, the realm binding and the map data.
2. Validate the configured character-creation spawn (when enabled).
3. Read the map-transition JSON, within its size limits, with `FileMapTransitionCatalogRepository`.
   Reject links, missing or malformed entries, unknown statuses, duplicate keys and invalid target
   tiles.
4. Require that the catalog was built from the same `WorldMapContentCatalog` instance that will
   construct `WorldRuntime`. A second, equivalent load that only matches by map ID is not accepted.
5. Start the runtime and publish the maps, catalog and runtime together. Network-scoped services
   can see the catalog only once the world is ready.
6. When the world stops, unpublish them together. A world that is not ready or has stopped cannot
   hand out the catalog.

The packaged `content/maps/map-transitions.json` is required by `dotnet build`/publish and at
runtime. `GameServer:Content:TransitionsPath`, `MaximumTransitionFileBytes`, and
`MaximumTransitions` are required settings, and the last two set size limits. There is no hot
reload. Changing the content needs new pinned content, a restart and the normal release review.

## Properties

- The world is never partly published while transition content is loading or invalid.
- One immutable content snapshot controls terrain passways, collision and candidate routes.
- A catalog built from a different snapshot is rejected, including in tests and custom repository
  implementations.
- There are no database writes, new credentials or EF migrations.
- The historical candidate destinations do not prove what the 5517 retail server allowed in
  gameplay. `WorldPortalPreflight` does not authorize or commit live transfers.

## Testing and deployment

Tests cover settings validation, readiness publication, the same-snapshot check, failure before
publication and unpublishing on stop. The existing release test over the standard 373-row file
checks its provenance and destination collision. A production deployment also needs the
map-transition file in the deployed content tree. If the file is missing, the world does not start;
portals are never silently disabled or made up.

## Remaining before transfers

The map-transfer cutover still has to coordinate the saved-location CAS when its outcome is
unknown, source and destination partition ownership under the ownership generation, behavior on
disconnect and shutdown, the session's map identity and reconciliation, departure and arrival
visibility, and the 5517 action-86 map-enter packet order, confirmed against the client. No socket
or database I/O may run on a partition worker.
