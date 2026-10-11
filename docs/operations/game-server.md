# GameServer Operations

## Current Scope

The GameServer is a runnable .NET Generic Host responsible for database readiness, canonical
world-content loading, authoritative world initialization, supervision, and shutdown.

With network ingress enabled, the host accepts bounded TCP connections, performs the verified
native 5517 secure handshake and single-use ticket redemption, resolves existing characters,
and transfers completed bootstrap connections into supervised authoritative world sessions.
Character creation and gameplay packet dispatch remain unimplemented.

Successful process startup means the required databases and canonical world content have passed
readiness checks and the world scheduler has started.

**Network ingress begins only after world and item-content readiness have completed.**

## Configuration

The GameServer uses the standard .NET Generic Host configuration pipeline, including:

- `appsettings.json`;
- environment-specific application settings;
- environment variables;
- command-line configuration;
- development user secrets.

Deployment configuration is validated before constructing the host.

| Setting                                               | Purpose                                                            |
| ----------------------------------------------------- | ------------------------------------------------------------------ |
| `ConnectionStrings:Accounts`                          | Accounts database used for ticket redemption and schema readiness. |
| `ConnectionStrings:Game`                              | Game database used for character persistence and schema readiness. |
| `GameServer:GameLoginTickets:ActiveVerificationKeyId` | Active verification-key identity.                                  |
| `GameServer:GameLoginTickets:VerificationKeys`        | Configured game-login ticket verification keys.                    |
| `GameServer:Content:DefinitionsPath`                  | Canonical map-definition catalog.                                  |
| `GameServer:Content:TerrainDirectory`                 | Canonical `.ocbt` base-terrain directory.                          |
| `GameServer:Content:CollisionDirectory`               | Canonical `.ocmc` collision directory.                             |
| `GameServer:Content:MaximumDefinitionFileBytes`       | Maximum map-definition file size.                                  |
| `GameServer:Content:MaximumDefinitions`               | Maximum number of map definitions.                                 |
| `GameServer:World:WorkerCount`                        | Fixed world scheduler worker count.                                |
| `GameServer:World:PartitionQueueCapacity`             | Maximum ordinary queued commands per partition.                    |
| `GameServer:World:MaxCommandsPerTurn`                 | Maximum commands executed per partition turn.                      |
| `GameServer:World:MaxCharacters`                      | Global authoritative character capacity.                           |
| `GameServer:World:MaxCharactersPerMap`                | Per-map authoritative character capacity.                          |
| `GameServer:Network:Enabled`                          | Enables bounded GameServer TCP ingress; false retains world-only hosting. |
| `GameServer:Network:BindAddress`                      | IPv4 listener binding. |
| `GameServer:Network:Port`                             | GameServer TCP port. |
| `GameServer:Network:ListenBacklog`                    | Kernel listener backlog limit. |
| `GameServer:Network:AdmissionCapacity`                | Bounded waiting-connection capacity. |
| `GameServer:Network:MaximumConnectionsPerSource`     | Per-source occupancy across queued, processing, and world connections. |
| `GameServer:Network:WorkerCount`                      | Fixed simultaneous login/bootstrap workers. |
| `GameServer:Network:ConnectionTimeout`                | Whole pre-world login/bootstrap timeout. |
| `GameServer:Items:FilePath`                           | Canonical item-type catalog. |
| `GameServer:Items:MaximumFileBytes`                   | Maximum item-type catalog file size. |
| `GameServer:Items:MaximumDefinitions`                 | Maximum number of item-type definitions. |

The GameServer and AccountServer must use compatible ticket verification keys. Retain verification
keys needed for outstanding tickets during rotation.

Do not commit connection strings containing credentials or ticket-verification key material.

### Local development secrets

User secrets are loaded by the Generic Host in the `Development` environment. Set
`DOTNET_ENVIRONMENT=Development` when running with user secrets.

Configure the local secrets:

```bash
dotnet user-secrets set \
  "ConnectionStrings:Accounts" \
  "<accounts-connection-string>" \
  --project src/OpenConquer.GameServer

dotnet user-secrets set \
  "ConnectionStrings:Game" \
  "<game-connection-string>" \
  --project src/OpenConquer.GameServer

dotnet user-secrets set \
  "GameServer:GameLoginTickets:ActiveVerificationKeyId" \
  "<active-key-id>" \
  --project src/OpenConquer.GameServer

dotnet user-secrets set \
  "GameServer:GameLoginTickets:VerificationKeys:0:Id" \
  "<active-key-id>" \
  --project src/OpenConquer.GameServer

dotnet user-secrets set \
  "GameServer:GameLoginTickets:VerificationKeys:0:EncodedKey" \
  "<canonical-base64-verification-key>" \
  --project src/OpenConquer.GameServer
```

The verification key must be the canonical Base64 encoding of exactly 32 bytes.

For deployment, use secure configuration providers or environment variables rather than development
user secrets.

Corresponding environment-variable names:

```text
ConnectionStrings__Accounts
ConnectionStrings__Game
GameServer__GameLoginTickets__ActiveVerificationKeyId
GameServer__GameLoginTickets__VerificationKeys__0__Id
GameServer__GameLoginTickets__VerificationKeys__0__EncodedKey
```

## Canonical Content

The GameServer uses `AppContext.BaseDirectory` as its content root.

Relative paths are resolved against this directory, not the shell's current working directory.

The default release-content layout is:

```text
content/maps/
├── map-definitions.json
├── terrains/
│   └── *.ocbt
└── collision/
    └── *.ocmc
```

The GameServer project copies the runtime-required canonical content into both build and
publish outputs under `content/maps/`, preserving the directory structure.

The packaged runtime assets are `map-definitions.json`, `terrains/*.ocbt`, and
`collision/*.ocmc`. Historical reconstruction metadata, documentation, and other
non-runtime content are not included.

The canonical source files remain independently maintained in the repository.
The runtime-required `content/items/item-types.json` is also packaged for network-enabled
GameServer startup. `PreserveNewest` avoids recopying unchanged artifacts during incremental builds.

To use external canonical content instead of the packaged artifacts, override the
default paths:

```bash
export GameServer__Content__DefinitionsPath="/absolute/path/to/content/maps/map-definitions.json"
export GameServer__Content__TerrainDirectory="/absolute/path/to/content/maps/terrains"
export GameServer__Content__CollisionDirectory="/absolute/path/to/content/maps/collision"
export GameServer__Items__FilePath="/absolute/path/to/content/items/item-types.json"
```

The canonical loader validates map definitions, base-terrain identities, collision coverage,
artifact integrity, and configured resource limits.

No incomplete catalog is published.

## Startup

The startup sequence is:

1. Load and validate deployment configuration.
2. Construct the dependency-injection container.
3. Verify Accounts database schema readiness.
4. Verify Game database schema readiness.
5. Load and validate canonical map definitions, terrain, and collision content.
6. Construct and start the authoritative `WorldRuntime`.
7. Publish the validated canonical catalog and running runtime together.
8. When ingress is enabled, load and validate the canonical item-type catalog.
9. Bind the GameServer TCP listener and start fixed connection workers.
10. Supervise world execution, network ingress, and independently owned world sessions.

A failure in an earlier stage prevents execution of later stages.

Database migrations are not applied automatically at startup.

The TCP listener is created only after the preceding readiness stages succeed and ingress is enabled.

The configured Generic Host startup timeout is two minutes.

## World Ownership

`GameServerWorldState` exposes the running world only after successful publication.

Its publication contains:

- the immutable `WorldMapContentCatalog`;
- the corresponding authoritative `WorldRuntime`.

A caller cannot acquire an unpublished or stopped world through this boundary.

Existing-character bootstrap transfers ownership into the authoritative world through
the hosted connection-processing pipeline. A login worker becomes available immediately
after transferring a completed `WorldSession`.

World-session I/O and cleanup are supervised independently of login workers. Fatal cleanup
failures are recorded and application shutdown is requested before the supervised session
task completes. The worker pool does not finish draining sessions before those outcomes
have been reported.

Per-source admission reservations remain occupied until the underlying transport has
successfully completed disposal. A failed transport disposal does not release its source
reservation or permit replacement admission against that reservation. It records a fatal
process failure and requests application shutdown.

This policy also applies to disposal failures involving capacity-rejected connections.
The server does not continue operating when TCP connection closure cannot be established.

## Shutdown

Shutdown cancels GameServer ingress, closes the TCP listener once through the network
execution lifecycle, prevents additional admission, and drains login workers and supervised
sessions. It then unpublishes the world through `GameServerWorldState`.

Failures during connection or session cleanup are retained in the process failure state.
Normal shutdown does not treat successfully closed connections as fatal.

It then stops authoritative execution, rejects pending commands, joins world workers, clears
map-local state, and awaits registered admission and session lifetimes.

The underlying world cleanup does not abandon ownership merely because a caller cancels its shutdown
wait.

Runtime shutdown failures propagate through the hosted service.

The configured Generic Host shutdown timeout is one minute. This timeout cancels the host's wait; it
is not a guarantee that stalled cleanup can be forcibly terminated.

Unexpected world-runtime termination records a fatal process condition and requests application
shutdown. Normal host shutdown does not record a fatal runtime failure.

## Run

From the repository root, after configuring the required databases and verification keys:

```bash
export DOTNET_ENVIRONMENT=Development

dotnet run \
  --project src/OpenConquer.GameServer/OpenConquer.GameServer.csproj \
  --configuration Release
```

A successfully started GameServer remains running with authoritative world execution initialized.

With ingress enabled, it accepts 5517 connections and supports existing-character
bootstrap through authoritative world admission. It deliberately does not implement
gameplay dispatch or character creation.

Use `Ctrl+C` for normal shutdown.

## Verification

```bash
dotnet restore OpenConquer.Server.slnx --locked-mode

dotnet build OpenConquer.Server.slnx \
  --configuration Release \
  --no-restore

dotnet test OpenConquer.Server.slnx \
  --configuration Release \
  --no-build \
  --no-restore

dotnet format OpenConquer.Server.slnx \
  --verify-no-changes \
  --no-restore

git diff --check
```

CI additionally verifies the Account and Game EF Core migration models, dependency changes, and
security analysis.

The admission concurrency test reports completed-operation throughput and p50, p95, and
p99 latency for 256 concurrent simulated source connections. These measurements are
diagnostic, not machine-independent performance acceptance thresholds.

Production performance validation must additionally collect world startup duration,
managed memory, process working set, live-connection counts, real TCP throughput, and
end-to-end authentication/bootstrap latency under representative concurrent load.
The simulated admission measurement does not establish native-client throughput or
a production latency service-level objective.

## Not Yet Implemented

- Character creation.
- Gameplay request routing, command authorization, and replication.
- Authoritative movement, combat, visibility, and NPC simulation.
- Account registration and remaining retail compatibility verification.

Future network integration must preserve readiness gating, canonical map identity, bounded
admission, exclusive world ownership, and supervised cleanup.
