# AccountServer operations

The AccountServer and GameServer are separate processes with separate configuration. Both use
the Accounts database and must share the same ticket verification keys.

## Configuration

Settings come from the standard .NET host providers, each overriding the one before:
`appsettings.json`, `appsettings.{Environment}.json`, user secrets (Development only), environment
variables, and the command line. Settings files are resolved from the executable's directory. The
committed settings files contain no credentials or keys.

| Setting                                                        | Purpose                                                  |
| -------------------------------------------------------------- | -------------------------------------------------------- |
| `ConnectionStrings:Accounts`                                   | Accounts database (required)                             |
| `AccountServer:Network:BindAddress`, `LoginPort`               | Login listener                                           |
| `AccountServer:Network:ListenBacklog`                          | Listener backlog (default 512)                           |
| `AccountServer:Admission:Capacity`                             | Queued connections (default 1,024)                       |
| `AccountServer:Admission:MaximumConcurrentConnectionsPerSource`| Per-source connections (default 6)                       |
| `AccountServer:Workers:Count`, `ConnectionTimeout`             | Login workers (default 8) and whole-login timeout (15 s) |
| `AccountServer:Handshake:PhaseTimeout`                         | Timeout per expected client packet (5 s)                 |
| `AccountServer:AuthenticationProtection:*`                     | Rate limits; defaults in [Authentication](../architecture/authentication.md#rate-limiting) |
| `AccountServer:GameLoginTickets:ActiveVerificationKeyId`       | Key used for new tickets (required)                      |
| `AccountServer:GameLoginTickets:VerificationKeys`              | `Id` and `EncodedKey` pairs (required)                   |
| `AccountServer:GameLoginTickets:Cleanup:Interval`, `MaximumBatchesPerRun` | Expired ticket cleanup (1 minute, 10 batches) |
| `AccountServer:Registration:Enabled`                           | In-client account registration (default `false`)         |
| `AccountServer:Registration:*`                                 | Registration limits; defaults in [Account registration](../architecture/account-registration.md#abuse-limits) |
| `ConnectionStrings:AccountRegistration`                        | Registration identity (required when registration is enabled) |

The AccountServer has no GameServer setting. Each login is redirected to the endpoint registered for
the realm the client selected (see [Realm registry](realm-registry.md)). Configuration that still
sets the retired `AccountServer:Network:GameServerAddress` or `GameServerPort` is rejected at
startup. In environment variables, replace `:` with `__`, for example
`AccountServer__Network__LoginPort`.

### Verification keys

Each key needs a unique nonzero ID and 32 random bytes encoded as canonical Base64. The active ID
must be one of the configured keys. Keep a retired key configured until every ticket it signed has
expired, and configure the same keys on the GameServer. Never reuse keys from test fixtures.

## Local development

`DOTNET_ENVIRONMENT=Development` enables `appsettings.Development.json` (listener on
`127.0.0.1:9958`) and user secrets. The development realm and its GameServer endpoint are provisioned
in the database, as described in [LocalRealm provisioning](local-realm-provisioning.md). From the
repository root:

```bash
P=src/OpenConquer.AccountServer/OpenConquer.AccountServer.csproj
dotnet user-secrets set "ConnectionStrings:Accounts" "$ACCOUNT_CONNECTION_STRING" --project $P
dotnet user-secrets set "AccountServer:GameLoginTickets:ActiveVerificationKeyId" "1" --project $P
dotnet user-secrets set "AccountServer:GameLoginTickets:VerificationKeys:0:Id" "1" --project $P
dotnet user-secrets set "AccountServer:GameLoginTickets:VerificationKeys:0:EncodedKey" "$(openssl rand -base64 32)" --project $P
```

Generate a key once per database, not on every start. For an existing database, use its deployed key
IDs and keys. User secrets are for development only. Production configuration should come from the
deployment's secret store.

## Database

The Accounts database must already exist on MySQL 8.4 with character set `utf8mb4` and collation
`utf8mb4_0900_as_cs`, migrated to schema version 6 (`20261013090000_AddAccountSessions`). The server
never migrates on startup. See [Database migrations](database-migrations.md). The runtime database
account needs only the permissions for normal account operations, the readiness check, `SELECT` on
`realms`, `realm_status` and `account_sessions`, and `INSERT, UPDATE` on `account_session_kicks`.

### Registration identity

Registration writes through its own MySQL identity, on the same server and database as
`ConnectionStrings:Accounts` but not `root` and not the AccountServer identity. It needs only:

```sql
GRANT SELECT, INSERT ON `openconquer_accounts`.`accounts` TO 'openconquer_account_registration'@'%';
GRANT INSERT ON `openconquer_accounts`.`account_password_credentials` TO 'openconquer_account_registration'@'%';
GRANT INSERT ON `openconquer_accounts`.`account_audit_events` TO 'openconquer_account_registration'@'%';
```

`SELECT` on `accounts` serves the `INSERT … SELECT` statements and the read-back of a commit whose
acknowledgment was lost. To enable registration in development:

```bash
P=src/OpenConquer.AccountServer/OpenConquer.AccountServer.csproj
dotnet user-secrets set "ConnectionStrings:AccountRegistration" "$REGISTRATION_CONNECTION_STRING" --project $P
dotnet user-secrets set "AccountServer:Registration:Enabled" "true" --project $P
```

## Running

```bash
dotnet build OpenConquer.Server.slnx -c Release
DOTNET_ENVIRONMENT=Development dotnet run --project src/OpenConquer.AccountServer -c Release --no-build
```

`dotnet run` uses the project's launch profile, which sets `DOTNET_ENVIRONMENT=Development`. In
Rider, run the shared configurations in `.run/`: `AccountServer (Development)`, `GameServer
(Development)`, or `Servers (Development)` for both. A Rider ".NET Project" configuration ignores
launch profiles, so the server starts in Production without user secrets and fails configuration.

Startup order: load and validate configuration and keys, compose services, verify the database,
start ticket cleanup, then open the listener. Any failure stops startup before the listener opens.
Startup and shutdown are each limited to 30 seconds. A fatal background failure shuts the host down
with a nonzero exit code.

## Troubleshooting

| Error                                      | Check                                                                 |
| ------------------------------------------ | --------------------------------------------------------------------- |
| `ConnectionStrings:Accounts` missing       | The environment name and the secret provider in use                  |
| Configuration invalid                      | The setting named in the inner exception; do not substitute defaults |
| Verification key configuration invalid     | Unique nonzero IDs, 32-byte canonical Base64, active ID present       |
| Database readiness failed                  | Server reachable, correct database, character set and collation, migration history, `schema_compatibility` row, permissions |

Never bypass the readiness check or edit `schema_compatibility` by hand to get the server to start.
