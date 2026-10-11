# OpenConquer Server

[![CI](https://github.com/berniemackie97/OpenConquerServer/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/berniemackie97/OpenConquerServer/actions/workflows/ci.yml)

OpenConquer is a game server for the Conquer Online client, written in C# on .NET 10. It targets
client version 5517 and is built as a modular monolith with two executable hosts: an AccountServer
that authenticates players and a GameServer that runs the world.

The project is in early development. Players can log in, receive a GameServer ticket, and load an
existing character into the world. Character creation and gameplay are not implemented yet. See
[Implementation status](docs/status.md) for details.

## Projects

| Project                      | Responsibility                                                                     |
| ---------------------------- | ---------------------------------------------------------------------------------- |
| `OpenConquer.Domain`         | Game rules, value types, and invariants.                                           |
| `OpenConquer.Application`    | Use cases and the persistence and content contracts they depend on.                |
| `OpenConquer.Infrastructure` | MySQL persistence, security primitives, rate limiting, and content loading.        |
| `OpenConquer.Protocol`       | Wire formats: framing, serialization, text encoding, cryptography, and packets.    |
| `OpenConquer.Transport`      | TCP connections, buffering, admission, and backpressure.                           |
| `OpenConquer.AccountServer`  | Account login host.                                                                |
| `OpenConquer.GameServer`     | Game host: connection handling, character login, and world execution.              |
| `OpenConquer.GameData.Tool`  | Offline tool that imports client data files and generates the server's content.    |

Dependencies point inward: hosts compose Application, Infrastructure, Protocol, and Transport;
Infrastructure implements Application contracts; Application depends only on Domain. See the
[architecture overview](docs/architecture/README.md).

## Getting started

Requirements:

- .NET SDK 10.0.400 (pinned in `global.json`)
- MySQL 8.4 for running the servers
- Docker for the database integration tests

```bash
dotnet restore OpenConquer.Server.slnx --locked-mode
dotnet build OpenConquer.Server.slnx -c Release --no-restore
dotnet test OpenConquer.Server.slnx -c Release --no-build
dotnet format OpenConquer.Server.slnx --verify-no-changes --no-restore
```

Tests use xUnit v3 on Microsoft Testing Platform. Database tests start temporary MySQL containers
with Testcontainers and apply the repository migrations. Add `--coverlet` to a test command for
coverage.

To run the servers, follow the [AccountServer](docs/operations/account-server.md) and
[GameServer](docs/operations/game-server.md) operations guides.

## Repository layout

```text
src/        Production projects
tests/      One test project per production project, mirroring its folder structure
tools/      Offline content tooling
content/    Content shipped with the GameServer (item types, maps)
docs/       Architecture, protocol, content, and operations documentation
```

## Continuous integration

GitHub Actions runs on every pull request to `main` and every push to `main`. It restores with
locked dependencies, checks formatting, builds in Release with warnings as errors, verifies that
both EF Core models match their migrations, runs the full test suite, and reviews dependency
changes. Dependabot tracks NuGet packages, the .NET SDK, and GitHub Actions.

## Disclaimer

OpenConquer is an independent project and is not affiliated with or endorsed by the publisher or
developers of Conquer Online. Conquer Online and related names and assets belong to their
respective owners.
