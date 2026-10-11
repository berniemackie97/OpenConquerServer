# AccountServer Operations

This document describes configuration, local development startup, database preparation, and
operational requirements for OpenConquer.AccountServer.

AccountServer and GameServer are independent executable applications. Their configuration, database
dependencies, and startup readiness must be managed separately.

## Configuration

AccountServer uses the standard .NET Generic Host configuration providers.

The following configuration sources are supported:

- appsettings.json
- appsettings.{Environment}.json
- Development User Secrets
- Environment variables
- Command-line configuration

Later configuration providers override earlier providers.

Application settings are loaded relative to the executable's deployment directory.

The version-controlled settings files contain no database credentials or cryptographic keys.

## Development Environment

Use DOTNET_ENVIRONMENT=Development to enable the development settings and User Secrets.

The development configuration binds the AccountServer listener to 127.0.0.1:9958 and advertises
127.0.0.1:5816 as the GameServer endpoint.

These addresses are intended for local development only.

Production network addresses must be explicitly configured for the target deployment.

## Required Secrets

AccountServer requires an Accounts database connection string:

    ConnectionStrings:Accounts

It also requires:

    AccountServer:GameLoginTickets:ActiveVerificationKeyId
    AccountServer:GameLoginTickets:VerificationKeys

Each verification-key entry requires a nonzero ID and a canonical Base64 encoding of exactly 32
cryptographically random bytes.

The active key ID must identify an entry in the configured verification-key set.

Never reuse the verification-key examples contained in unit-test fixtures.

Verification keys must be retained for as long as valid outstanding tickets may reference them.
Removing a key prematurely can invalidate those tickets.

AccountServer and GameServer must use compatible verification-key configuration when operating
against the same Accounts database.

## Local User Secrets

The AccountServer project has a dedicated User Secrets identity.

Run these commands from the repository root.

Configure the connection string using the actual local Accounts database credentials:

    dotnet user-secrets set "ConnectionStrings:Accounts" \
      "$ACCOUNT_CONNECTION_STRING" \
      --project src/OpenConquer.AccountServer/OpenConquer.AccountServer.csproj

ACCOUNT_CONNECTION_STRING must already contain the intended connection string.

For a new, isolated development database, an operator may select verification-key ID 1 and generate
a new random verification key:

    dotnet user-secrets set "AccountServer:GameLoginTickets:ActiveVerificationKeyId" \
      "1" \
      --project src/OpenConquer.AccountServer/OpenConquer.AccountServer.csproj

    dotnet user-secrets set "AccountServer:GameLoginTickets:VerificationKeys:0:Id" \
      "1" \
      --project src/OpenConquer.AccountServer/OpenConquer.AccountServer.csproj

    dotnet user-secrets set "AccountServer:GameLoginTickets:VerificationKeys:0:EncodedKey" \
      "$(openssl rand -base64 32)" \
      --project src/OpenConquer.AccountServer/OpenConquer.AccountServer.csproj

Do not generate a replacement key on every application startup.

For an existing database, preserve the deployed key IDs and key material instead of following the
new-database example.

User Secrets are a local development convenience, not a production secret store.

## Accounts Database

AccountServer uses MySQL 8.4.

The database must exist before AccountServer starts.

Its required character set is:

    utf8mb4

Its required collation is:

    utf8mb4_0900_as_cs

AccountServer does not automatically migrate the database during startup.

Schema migration is an explicit administrative operation.

The current Accounts schema contract is version 2, with migration:

    20260907004603_ProtectGameLoginTicketAuthenticationKey

Restore repository-local tools:

    dotnet tool restore

Using the operator-provided administrative connection string, apply migrations:

    dotnet tool run dotnet-ef -- database update \
      --configuration Release \
      --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --context AccountDbContext \
      --connection "$ACCOUNT_SCHEMA_MIGRATION_CONNECTION_STRING"

The migration connection string must identify the intended Accounts database.

For an existing populated database, stop active writers and take a verified backup before applying
schema changes.

Do not manually edit migration history or schema compatibility metadata.

The runtime database identity should have only the permissions needed for normal account operations
and readiness verification.

## Running AccountServer

Restore and build:

    dotnet restore OpenConquer.Server.slnx --locked-mode

    dotnet build OpenConquer.Server.slnx -c Release --no-restore

Start the development host:

    DOTNET_ENVIRONMENT=Development \
      dotnet run \
      --project src/OpenConquer.AccountServer/OpenConquer.AccountServer.csproj \
      --configuration Release \
      --no-build

The development launch profile can also be selected in a supporting IDE.

Do not assume IDE launch configurations automatically load User Secrets unless the process is
running in the Development environment.

## Startup Readiness

Startup proceeds through the following boundaries:

1. Load application configuration.
2. Validate configuration and verification-key material.
3. Compose the Generic Host and application services.
4. Verify Accounts database connectivity and schema compatibility.
5. Start game-login ticket cleanup.
6. Start the bounded account-login listener and processing runtime.

The login listener must not accept connections before required database readiness has completed
successfully.

Invalid configuration must terminate startup rather than enabling an insecure or partially
configured runtime.

Successful AccountServer startup does not imply that GameServer is operational.

## Deployment

Production deployments should supply configuration through an approved deployment configuration and
secret-management mechanism.

Environment-variable configuration uses double underscores to separate sections.

Examples of configuration keys include:

    ConnectionStrings__Accounts
    AccountServer__Network__BindAddress
    AccountServer__Network__LoginPort
    AccountServer__Network__GameServerAddress
    AccountServer__Network__GameServerPort
    AccountServer__GameLoginTickets__ActiveVerificationKeyId
    AccountServer__GameLoginTickets__VerificationKeys__0__Id
    AccountServer__GameLoginTickets__VerificationKeys__0__EncodedKey

The values must be provided by the deployment environment.

Never commit production credentials, verification keys, or environment-specific connection strings
to source control.

The runtime network binding and advertised GameServer endpoint are distinct settings.

The advertised GameServer address must be reachable by the intended clients.

Authentication-key verification material must be coordinated with GameServer before account-login
traffic is enabled.

## Failure Diagnosis

### ConnectionStrings:Accounts is missing or empty

The required Accounts connection string was not supplied.

Verify the active environment and configured secret provider.

Do not disable connection-string validation.

### AccountServer configuration is invalid

Inspect the nested validation exception and correct the named setting.

Do not replace invalid values with arbitrary defaults.

### Verification-key configuration fails

Confirm that each configured key has a unique nonzero ID and contains the canonical Base64
representation of exactly 32 bytes.

Confirm that the active ID identifies a configured key.

### Database readiness fails

Confirm:

- MySQL availability and credentials.
- The intended Accounts database.
- Database character set and collation.
- Applied EF Core migration history.
- The schema_compatibility record.
- Runtime database permissions.

Do not bypass readiness checks or manually advance schema metadata to force startup.
