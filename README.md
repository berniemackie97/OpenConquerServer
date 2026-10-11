# OpenConquer Server

[![CI](https://github.com/berniemackie97/OpenConquerServer/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/berniemackie97/OpenConquerServer/actions/workflows/ci.yml)

OpenConquer Server is an open-source **Conquer Online 5517** server emulator rebuilt in C# on **.NET
10**.

The project targets accurate 5517 client compatibility while using maintainable, production-grade
server architecture.

> **Status:** Early development. Transport, the runnable AccountServer host, the standard 5517
> AccountServer login transaction, account authentication, abuse protection, password persistence
> and migration, account security mutations, durable GameServer login tickets, ticket
> revocation/redemption, bounded expired-ticket maintenance, GameServer authenticated connection
> handoff, the verified existing-character bootstrap request sequence through `0x84`, and
> Game schema v9 syndicate persistence and read hydration are implemented.
> The initial canonical map-content baseline includes 282 world-map definitions and
> 185 distinct base-terrain artifacts, with immutable loading and integrity validation.
> Seven historical map identities remain unresolved. Canonical static collision is released,
> and the standalone authoritative world-partition core now supports bounded map-local command
> execution, capped character residency, generation-fenced ownership, and a revocable session
> admission/cleanup boundary. The readiness-gated GameServer Generic Host now verifies both
> database schemas, loads canonical content, and supervises authoritative world execution.
> Independent retail conformance and full native-parity verification remain ongoing.
> Account registration, GameServer network admission, character creation, gameplay packet
> routing, movement, and combat remain unimplemented.

## Architecture

OpenConquer is a modular monolith with two executable host boundaries.

```mermaid
flowchart TD
    AccountServer["OpenConquer.AccountServer"]
    GameServer["OpenConquer.GameServer"]

    Application["OpenConquer.Application"]
    Domain["OpenConquer.Domain"]
    Infrastructure["OpenConquer.Infrastructure"]

    Protocol["OpenConquer.Protocol"]
    Transport["OpenConquer.Transport"]
    GameDataTool["OpenConquer.GameData.Tool (offline)"]

    AccountServer --> Application
    AccountServer --> Infrastructure
    AccountServer --> Protocol
    AccountServer --> Transport

    GameServer --> Application
    GameServer --> Infrastructure
    GameServer --> Protocol
    GameServer --> Transport

    Infrastructure --> Application
    Infrastructure --> Domain
    GameDataTool --> Application
    GameDataTool --> Domain
    GameDataTool --> Infrastructure
    Application --> Domain
```

### Projects

| Project                        | Responsibility                                                                                                                                                       |
| ------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **OpenConquer.Domain**         | Account, character, item, social, skill, and syndicate rules, state, value objects, and invariants.                                                                  |
| **OpenConquer.Application**    | Authentication, account security, GameServer login-ticket orchestration, character-login resolution, and gameplay persistence/hydration contracts.                  |
| **OpenConquer.Infrastructure** | Accounts/Game MySQL persistence, password/security infrastructure, abuse protection, database readiness, and canonical static-content loading.                      |
| **OpenConquer.Protocol**       | 5517 framing, serialization, text encoding, login cryptography, credentials, and verified AccountServer/GameServer packet contracts.                                |
| **OpenConquer.Transport**      | TCP connections, bounded admission, I/O pumps, buffering, backpressure, and connection lifetime.                                                                    |
| **OpenConquer.AccountServer**  | Runnable 5517 account-login host with readiness-gated startup, bounded login processing, authentication handoff, supervision, observability, and ticket maintenance. |
| **OpenConquer.GameServer**     | Readiness-gated world host, authenticated bootstrap, authoritative execution, and owned world-session admission; network hosting and gameplay remain future. |
| **OpenConquer.GameData.Tool**  | Offline source-format decoding, import, verification, and generation of canonical OpenConquer release data.                                                             |

## Current AccountServer Login Flow

```text
validated host configuration
    ↓
database/schema readiness
    ↓
TCP listener
    ↓
per-source connection admission
    ↓
bounded global admission
    ↓
fixed login workers
    ↓
LoginConnectionSession
    ↓
1059 login seed
    ↓
1060 standard credential request
    ↓
AccountAuthenticator
    ↓
durable GameLoginTicket grant
    ↓
1055 authentication response
    ↓
1100 MAC report
    ↓
1052 res.dat version report
```

A successful `1055` is not sent until the GameServer login ticket has been durably granted.

The AccountServer supports the standard 5517 `1060` credential path. Protected/mobile login variants
are recognized as unsupported and fail closed.

The login listener is not exposed until account database readiness succeeds. Pre-authentication
connection admission and authentication work are bounded independently. Fatal background-service
failures trigger graceful host shutdown and result in a nonzero process exit code.

## Documentation

### Architecture

- [Architecture overview](docs/architecture/README.md)
- [Networking architecture](docs/architecture/networking.md)
- [Authentication and password migration](docs/architecture/authentication.md)
- [World execution](docs/architecture/world-execution.md)
- [Server baseline audit](docs/audits/server-baseline.md)
- [AccountServer operations](docs/operations/account-server.md)
- [GameServer operations](docs/operations/game-server.md)

### Protocol

- [Conquer Online 5517 protocol reference](docs/protocol/README.md)

## Repository Layout

```text
src/
├── OpenConquer.AccountServer/
├── OpenConquer.Application/
├── OpenConquer.Domain/
├── OpenConquer.GameServer/
├── OpenConquer.Infrastructure/
├── OpenConquer.Protocol/
└── OpenConquer.Transport/

tests/
├── OpenConquer.AccountServer.Tests/
├── OpenConquer.Application.Tests/
├── OpenConquer.Domain.Tests/
├── OpenConquer.GameData.Tool.Tests/
├── OpenConquer.GameServer.Tests/
├── OpenConquer.Infrastructure.Tests/
├── OpenConquer.Protocol.Tests/
└── OpenConquer.Transport.Tests/

tools/
└── OpenConquer.GameData.Tool/

content/
├── items/
└── maps/

docs/
├── architecture/
├── audits/
└── protocol/
```

Database schema changes are versioned through EF Core migrations in `OpenConquer.Infrastructure`.

Integration tests provision temporary MySQL 8.4 databases with Testcontainers and apply the
repository migrations before exercising persistence code.

## Requirements

- .NET SDK 10.0.400
- Docker for MySQL integration tests

## Build

```bash
dotnet restore OpenConquer.Server.slnx
dotnet build OpenConquer.Server.slnx -c Release --no-restore
```

## Tests

Run the complete suite:

```bash
dotnet test OpenConquer.Server.slnx -c Release --no-build
```

Run the protocol suite:

```bash
dotnet test tests/OpenConquer.Protocol.Tests/OpenConquer.Protocol.Tests.csproj -c Release --no-build
```

Tests use **xUnit v3** on **Microsoft Testing Platform**.

Coverage is available through `coverlet.MTP`:

```bash
dotnet test tests/OpenConquer.Protocol.Tests/OpenConquer.Protocol.Tests.csproj -c Release --no-build --coverlet
```

## Formatting

```bash
dotnet format OpenConquer.Server.slnx --verify-no-changes --no-restore
```

## Continuous Integration

GitHub Actions runs on pull requests targeting `main` and pushes to `main`.

CI verifies:

- locked dependency restore;
- formatting;
- Release build;
- account and game EF Core model/migration consistency;
- complete test suite;
- pull-request dependency vulnerability review.

Dependabot monitors NuGet packages, the pinned .NET SDK, and GitHub Actions dependencies.

## Disclaimer

OpenConquer Server is an independent open-source project and is not affiliated with or endorsed by
the original game publisher or developers.

Conquer Online and related names and assets belong to their respective owners.
