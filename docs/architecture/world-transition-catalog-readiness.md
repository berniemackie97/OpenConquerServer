# Transition catalog world readiness — slice #155

## Boundary

The validated historical/native-exit candidate catalog is now a mandatory world-startup dependency,
not an optional offline-only resource. This slice does not activate native action 85 at gameplay ingress
and does not transfer characters.

## Startup and lifetime

1. Verify Account and Game database versions, realm binding, and canonical map data.
2. Validate configured character-creation spawn (when enabled).
3. Read the bounded map-transition JSON with `FileMapTransitionCatalogRepository`, reject links,
   missing/malformed entries, unknown statuses, duplicate keys and invalid target tiles.
4. Require that the catalog was built from the **same `WorldMapContentCatalog` object** that will
   construct `WorldRuntime`. Never accept a second equivalent load by map ID alone.
5. Start the runtime and publish maps, catalog, and runtime together. The catalog is exposed to
   network-scoped services only after world readiness.
6. On world stop, unpublish them together. An unready or stopped world cannot hand out the catalog.

The packaged `content/maps/map-transitions.json` is mandatory for `dotnet build`/publish and at
runtime. `GameServer:Content:TransitionsPath`, `MaximumTransitionFileBytes`, and
`MaximumTransitions` are required bounded settings. No hot reload is allowed; updates require
new pinned content, a restart and the normal release review.

## Guarantees

- No partially published world while transition content is loading or invalid.
- One immutable content snapshot controls terrain passways, collision and candidate routes.
- Fail-closed mismatched catalog snapshots even for tests/custom repository implementations.
- No database writes, new credentials or EF migrations.
- The historical candidate destinations do not constitute proof of 5517 retail server-side
  gameplay permissions. `WorldPortalPreflight` is not a live authorization/commit coordinator.

## Tests and operational gates

Tests verify settings validation, readiness publication, same-snapshot identity, failure before
publication and unpublication on stop. The existing 373-row canonical release test verifies file
provenance and destination collision. Full CI must pass before merge. Production readiness also
requires the canonical map-transition artifact in the deployed content tree; a missing file
prevents world startup instead of silently disabling or inventing portals.

## Remaining before transfers

The next operational cutover must coordinate persisted-location CAS with unknown outcomes,
source/destination generation-fenced partition ownership, disconnect/shutdown behavior, session
map identity and reconciliation, visibility departure/arrival, and verified native 5517 action-86
map-enter packet ordering. No socket or database I/O may run on a partition worker.
