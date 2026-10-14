# GameServer operations

The GameServer verifies both databases, loads content, starts the world runtime, and then, if
network ingress is enabled, accepts client connections. It supports login of existing characters
into the world. Character creation and gameplay are not implemented yet.

## Configuration

Settings use the same providers as the [AccountServer](account-server.md#configuration) and are
validated before the host is built.

| Setting                                            | Purpose                                                       |
| -------------------------------------------------- | ------------------------------------------------------------- |
| `GameServer:RealmId`                               | Realm this server hosts; must match the Game database binding (required) |
| `ConnectionStrings:Accounts`                       | Accounts database: ticket redemption and readiness            |
| `ConnectionStrings:Game`                           | Game database: characters and readiness                       |
| `GameServer:GameLoginTickets:ActiveVerificationKeyId`, `VerificationKeys` | Same keys as the AccountServer         |
| `GameServer:Content:DefinitionsPath`               | Map definitions file                                          |
| `GameServer:Content:TerrainDirectory`              | `.ocbt` directory                                             |
| `GameServer:Content:CollisionDirectory`            | `.ocmc` directory                                             |
| `GameServer:Content:MaximumDefinitionFileBytes`, `MaximumDefinitions` | Map catalog limits                         |
| `GameServer:Items:FilePath`                        | Item type catalog                                             |
| `GameServer:Items:MaximumFileBytes`, `MaximumDefinitions` | Item catalog limits                                    |
| `GameServer:World:WorkerCount`                     | World scheduler threads (default 8)                           |
| `GameServer:World:PartitionQueueCapacity`          | Commands queued per map (default 256)                         |
| `GameServer:World:MaxCommandsPerTurn`              | Commands per map turn (default 64)                            |
| `GameServer:World:MaxCharacters`, `MaxCharactersPerMap` | Resident characters (defaults 8,192 and 1,024)          |
| `GameServer:Network:Enabled`                       | Accept client connections; `false` runs the world only        |
| `GameServer:Network:BindAddress`, `Port`           | IPv4 listener (default `0.0.0.0:5816`)                        |
| `GameServer:Network:ListenBacklog`                 | Listener backlog (default 512)                                |
| `GameServer:Network:AdmissionCapacity`             | Queued connections (default 256)                              |
| `GameServer:Network:MaximumConnectionsPerSource`   | Connections per source, including those in the world (default 8) |
| `GameServer:Network:WorkerCount`                   | Login workers (default 16)                                    |
| `GameServer:Network:ConnectionTimeout`             | Time allowed from accept to world entry (default 1 minute)    |
| `GameServer:RealmStatus:ReportInterval`            | Realm status report interval; required with network ingress (default 10 seconds) |

For local development, set the connection strings and keys with `dotnet user-secrets` against
`src/OpenConquer.GameServer`, as for the AccountServer. Use the same key IDs and keys. Each realm
runs its own GameServer; see [Realm operations](realms.md) for running more than one.

## Content

Content paths are resolved against the executable's directory, not the working directory. The
build and publish output already contains `content/items/item-types.json`,
`content/maps/map-definitions.json`, `content/maps/terrains/`, and `content/maps/collision/`. To use
content from elsewhere, override the paths, for example
`GameServer__Content__TerrainDirectory=/srv/openconquer/content/maps/terrains`. See
[Content](../content/README.md) for the formats.

## Startup

1. Validate configuration and build the service container.
2. Verify the Accounts database, then the Game database.
3. Load and validate map definitions, terrain, and collision.
4. Start the world runtime and publish it together with the content catalog.
5. If ingress is enabled: load the item catalog, open the listener, start the login workers, and
   start reporting the realm's load to the Accounts database.

Any failure stops later steps; the listener never opens on a partially ready server. Migrations are
not applied. The startup limit is two minutes.

Once a login completes, its connection becomes a world session and the worker is free for the next
connection. World sessions have their own supervised lifetime.

## Shutdown

Shutdown first deletes the realm status report, so the AccountServer refuses new logins at once. It
then cancels ingress, closes the listener, drains the admission queue and workers, and closes
world sessions. It then unpublishes the world, stops the runtime, fails pending commands, joins the
scheduler threads, and clears map state. Cleanup completes even if the host stops waiting. The
host's one-minute shutdown limit bounds how long it waits, not how long cleanup runs.

The host exits with a nonzero code if the world runtime or the network runtime stops unexpectedly,
or if a connection or world session cannot be cleaned up. Connections that close normally during
shutdown are not failures.

## Performance

The admission concurrency test reports throughput and p50/p95/p99 latency for 256 simulated
concurrent sources. Treat these numbers as diagnostics, not as targets. Production sizing still
needs measurements of startup time, memory, live connections, real TCP throughput, and end-to-end
login latency under representative load. Login metrics are listed in
[Networking](../architecture/networking.md#gameserver).

## Running

```bash
dotnet build OpenConquer.Server.slnx -c Release
DOTNET_ENVIRONMENT=Development dotnet run --project src/OpenConquer.GameServer -c Release --no-build
```

Press `Ctrl+C` for a normal shutdown.
