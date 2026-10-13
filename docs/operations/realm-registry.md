# Realm registry migration — Accounts schema v3

The operator-controlled `realms` table is introduced by the SQL-only EF migration
`20261013010000_AddRealmRegistry`. It does not alter existing accounts or
login-ticket rows. AccountServer and GameServer must be stopped before schema
upgrades and restarted only after readiness checks succeed.

The full migration runbook remains in `database-migrations.md`. Back up the Accounts
database before applying this release; the migration is deliberately not reversible
with `database update` because dropping a populated realm directory would destroy
operator-owned routing identity. Restore a verified backup or write and review an
explicit data-preserving rollback migration if rollback is required.

Apply with administrative credentials, using the local EF tool:

```bash
dotnet tool run dotnet-ef -- database update \
  --configuration Release \
  --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
  --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
  --context AccountDbContext \
  --connection "$ACCOUNTS_DB_ADMIN_CONNECTION"
```

Verify `schema_compatibility` reports `accounts / 3 /
20261013010000_AddRealmRegistry`, inspect `SHOW CREATE TABLE realms`, then
provision a known realm with an operator-controlled `realm_id`:

```sql
INSERT INTO `realms`
    (`realm_id`, `protocol_server_name`, `public_game_address`, `public_game_port`, `availability`)
VALUES (1, 'Local', INET6_ATON('127.0.0.1'), 5816, 1);
```

The example is for local development only. The public GameServer address and port
must match the real deployment. `protocol_server_name` is an immutable native
identity, not a display name; changing it disrupts saved client paths. A second
operator-provisioned row does not enable a second GameServer on its own.

Grant the AccountServer runtime SQL identity `SELECT` on `realms` and no
`INSERT`, `UPDATE` or `DELETE` privileges. Use an independent administrator
identity for changes, and avoid logging connection strings or credentials.

MySQL commits DDL nontransactionally. If `realms` exists but migration history
still reports v2 after an interrupted deployment, **stop** and compare its
complete schema against the migration before proceeding. Do not edit
`__EFMigrationsHistory` or the compatibility row manually or silently replace
the existing table. A conflicting `realms` table fails migration rather than
being overwritten.
