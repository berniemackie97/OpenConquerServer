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
| `ConnectionStrings:Game`                           | Game database: characters and readiness, SELECT-only          |
| `ConnectionStrings:GameLocationWriter`             | Dedicated Game location CAS and writer-fence identity; required with network ingress |
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
runs its own GameServer. See [Realm operations](realms.md) for running more than one.

## Provisioning the location-writer identity

Provision a separate location-writer MySQL user for each realm as part of deployment, outside the
GameServer runtime and EF migrations. The database account is created using independently
authorized database administration or infrastructure automation; GameServer must never create
users, grant privileges, or have access to the provisioning credential.

This MySQL 8.4 statement set is an **operator-reviewed template**, not an application startup
script. Replace the example database, account, permitted source host, and secret for the target
realm. Use a cryptographically generated secret supplied by your deployment secret manager.
Never commit the completed statements or credentials, log the secret, or rerun `CREATE USER` for
an existing account without an independently reviewed credential-rotation procedure.

```sql
CREATE USER 'realm_location_writer'@'game-server-host'
    IDENTIFIED WITH caching_sha2_password BY '<generated-secret>';

GRANT SELECT (
    account_id, character_id, map_id, position_x, position_y,
    location_revision, last_location_operation_id, location_writer_token
) ON `realm_game_database`.`characters`
    TO 'realm_location_writer'@'game-server-host';

GRANT UPDATE (
    map_id, position_x, position_y, location_revision,
    last_location_operation_id, location_writer_token
) ON `realm_game_database`.`characters`
    TO 'realm_location_writer'@'game-server-host';

SHOW GRANTS FOR 'realm_location_writer'@'game-server-host';
```

Apply the statements only after migrating the realm's Game database to schema v13 and confirming
its permanent realm binding. Inspect `SHOW GRANTS` for both the required column privileges
and unexpected global, database-wide, or other-table permissions. Do not grant DDL, `DELETE`,
`INSERT`, or unrestricted `UPDATE` on `characters`. Restrict the MySQL user host to the
actual GameServer source and use protected database transport appropriate to the deployment.

Configure `ConnectionStrings:GameLocationWriter` with that realm's connection endpoint, database,
and credential. For local development, store the connection string in .NET User Secrets; for
deployed servers, use a trusted deployment secret provider. It must not share an identity with
`ConnectionStrings:Game` or `ConnectionStrings:GameCreation`. The network-enabled server verifies
the restricted writer can perform the required no-row `SELECT` and `UPDATE` operations before
opening its listener, but that startup probe does not establish that excessive privileges are absent.

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
5. If ingress is enabled: remove the realm's account session records and kicks left by an earlier
   run, start reading kicks from other realms, load the item catalog, open the listener, start the
   login workers, and start reporting the realm's load to the Accounts database.

Any failure stops the later steps, so the listener never opens on a partly ready server. Migrations
are not applied. The startup limit is two minutes.

Once a login completes, its connection becomes a world session and the worker is free for the next
connection. World sessions have their own supervised lifetime.

## Shutdown

Shutdown first deletes the realm status report, so the AccountServer refuses new logins at once. It
then cancels ingress, closes the listener, drains the admission queue and workers, and closes world
sessions. It then stops reading kicks and removes the realm's account session records, so its
players can log in to another realm at once. It then unpublishes the world, stops the runtime, fails
pending commands, joins the scheduler threads, and clears map state. The host's one-minute shutdown
limit only caps how long the host waits. Cleanup keeps running until it finishes, even after the
host stops waiting.

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

`dotnet run` uses the project's launch profile, which sets `DOTNET_ENVIRONMENT=Development`. In
Rider, run the shared configurations in `.run/`: `AccountServer (Development)`, `GameServer
(Development)`, or `Servers (Development)` for both. A Rider ".NET Project" configuration ignores
launch profiles, so the server starts in Production without user secrets and fails configuration.

Press `Ctrl+C` for a normal shutdown.

## Fenced existing-character admission (Game schema v13)

A network-enabled GameServer requires `ConnectionStrings:GameLocationWriter` and fails configuration
validation before opening a listener when it is missing or points at a different Game database.
The writer identity must be distinct from both read-only GameServer and character-creation identities.
Grant `SELECT` on exactly `account_id`, `character_id`, `map_id`, `position_x`, `position_y`,
`location_revision`, `last_location_operation_id`, `location_writer_token` of `characters`; grant
`UPDATE` on exactly the six mutable location/fence columns. Grant no DDL and no other table rights.
Do not use root or the ordinary read-only GameServer identity for this connection.

Once cross-realm account-session admission succeeds, login rotates the character-row generation,
rehydrates the profile using the read-only connection, and compares the profile, revision, operation
marker and token to a fresh scoped observation before responding with successful bootstrap packets.
The session carries that authority and checks again before and after authoritative world admission.
Failures dispose presence, session ownership and transport; they must not be retried following an
uncertain rotation. This generation is a write CAS fence, **not** a distributed player-session lock.
A client already admitted can still be superseded by a different process after a point-in-time
observation; that case requires the account-session and presence protocols to revoke its ownership.
Live portal commit sequencing and durable in-world movement are separate milestones.
