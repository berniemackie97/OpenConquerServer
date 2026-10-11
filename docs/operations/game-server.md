# GameServer Operations

## Current Scope

The GameServer Generic Host owns startup readiness, authoritative world initialization, runtime
supervision, and shutdown.

The current host does not open a TCP listener, accept clients, execute the existing-character
bootstrap pipeline, or process gameplay packets. Those integrations remain separate work.

Starting the process successfully means both databases and the complete canonical world content have
passed readiness verification and the world scheduler is running.

It does not mean the server is accepting game connections.

## Configuration

The GameServer reads `appsettings.json`, environment-specific application settings, environment
variables, command-line configuration, and development user secrets through the standard .NET
Generic Host configuration pipeline.

Configuration is validated before the host is built.

Required deployment configuration:

| Setting                                               | Purpose                                                                       |
| ----------------------------------------------------- | ----------------------------------------------------------------------------- |
| `ConnectionStrings:Accounts`                          | Accounts database used for GameServer ticket redemption and schema readiness. |
| `ConnectionStrings:Game`                              | Game database used for character persistence and schema readiness.            |
| `GameServer:GameLoginTickets:ActiveVerificationKeyId` | Active ticket-verification key identity.                                      |
| `GameServer:GameLoginTickets:VerificationKeys`        | Verification keys shared with AccountServer.                                  |
| `GameServer:Content:DefinitionsPath`                  | Canonical world-map definition catalog.                                       |
| `GameServer:Content:TerrainDirectory`                 | Canonical `.ocbt` base-terrain artifacts.                                     |
| `GameServer:Content:CollisionDirectory`               | Canonical `.ocmc` static-collision artifacts.                                 |
| `GameServer:World`                                    | World worker, queue, execution-turn, and character-capacity limits.           |

GameServer and AccountServer must use compatible ticket-verification keys, including the same active
key identity. Rotation must preserve any verification keys needed for outstanding tickets.

Do not commit database passwords or verification keys to the repository.

For local development:

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
