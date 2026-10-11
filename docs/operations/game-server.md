# GameServer Operations

## Current Scope

The GameServer is a runnable .NET Generic Host responsible for database readiness, canonical
world-content loading, authoritative world initialization, supervision, and shutdown.

The host does not yet open a TCP listener, accept game connections, execute the existing-character
bootstrap pipeline, or process gameplay packets.

Successful process startup means the required databases and canonical world content have passed
readiness checks and the world scheduler has started.

**World readiness does not mean the server is accepting game connections.**

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

Canonical content is maintained independently of the executable's build output. Building the
GameServer does not automatically deploy the complete content tree.

For local development, configure absolute paths to the repository's content:

```bash
export GameServer__Content__DefinitionsPath="/absolute/path/to/content/maps/map-definitions.json"
export GameServer__Content__TerrainDirectory="/absolute/path/to/content/maps/terrains"
export GameServer__Content__CollisionDirectory="/absolute/path/to/content/maps/collision"
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
8. Supervise world execution for unexpected termination.

A failure in an earlier stage prevents execution of later stages.

Database migrations are not applied automatically at startup.

No game-network listener is created.

The configured Generic Host startup timeout is two minutes.

## World Ownership

`GameServerWorldState` exposes the running world only after successful publication.

Its publication contains:

- the immutable `WorldMapContentCatalog`;
- the corresponding authoritative `WorldRuntime`.

A caller cannot acquire an unpublished or stopped world through this boundary.

World-session admission, connection ownership, and revocation are already implemented as standalone
components. They are not yet connected to this host through a network listener or
connection-processing runtime.

## Shutdown

Shutdown first unpublishes the world, preventing new consumers from acquiring it through
`GameServerWorldState`.

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

From the repository root, after configuring the required databases, verification keys, and canonical
content paths:

```bash
export DOTNET_ENVIRONMENT=Development

dotnet run \
  --project src/OpenConquer.GameServer/OpenConquer.GameServer.csproj \
  --configuration Release
```

A successfully started GameServer remains running with authoritative world execution initialized.

It does not accept client connections in this slice.

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

## Not Yet Implemented

- GameServer TCP listener and connection admission.
- Bounded connection-worker processing.
- Hosted authentication and existing-character bootstrap orchestration.
- Hosted world-session processing and ongoing gameplay packet routing.
- Character creation.
- Authoritative movement, combat, visibility, and replication.

Future network integration must preserve readiness gating, canonical map identity, bounded
admission, exclusive world ownership, and supervised cleanup.
