# Realm registry migration (Accounts schema v3)

The SQL-only EF migration `20261013010000_AddRealmRegistry` adds the `realms` table, which the
operator controls. It does not change existing account or login-ticket rows. Stop the AccountServer
and GameServer before upgrading the schema, and restart them only after the readiness checks pass.

The full migration runbook is in `database-migrations.md`. Back up the Accounts database before
applying this release. The migration cannot be reversed with `database update`, because dropping a
populated realm directory would destroy the routing identities that the operator assigned. If you
need to roll back, restore a tested backup, or write and review a rollback migration that keeps the
data.

Apply the migration with administrative credentials, using the local EF tool:

```bash
dotnet tool run dotnet-ef -- database update \
  --configuration Release \
  --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
  --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
  --context AccountDbContext \
  --connection "$ACCOUNTS_DB_ADMIN_CONNECTION"
```

Check that `schema_compatibility` reports `accounts / 3 / 20261013010000_AddRealmRegistry` and
inspect `SHOW CREATE TABLE realms`. Then provision a known realm with a `realm_id` that you assign:

```sql
INSERT INTO `realms`
    (`realm_id`, `protocol_server_name`, `public_game_address`, `public_game_port`, `availability`)
VALUES (1, 'LocalRealm', INET6_ATON('127.0.0.1'), 5816, 1);
```

The `LocalRealm` values above are the identity assigned to the OpenConquerClient development
realm. They are real values, not an example to adapt. For a one-time insert with safety checks, use
[LocalRealm provisioning](local-realm-provisioning.md) instead of running the INSERT directly. The
public GameServer address and port must match the real deployment. `protocol_server_name` is a
permanent 5517 identity, not a display name. Changing it breaks the paths that clients saved under
the old name. Adding a second row does not, on its own, bring up a second GameServer.

Grant the AccountServer runtime SQL identity `SELECT` on `realms`, and no `INSERT`, `UPDATE` or
`DELETE`. Make changes with a separate administrator identity, and do not log connection strings
or credentials.

MySQL does not run DDL inside a transaction. If a deployment was interrupted and `realms` exists
while the migration history still reports v2, stop and compare the table's full schema with the
migration before you go on. Do not edit `__EFMigrationsHistory` or the compatibility row by hand,
and do not quietly replace the existing table. If a conflicting `realms` table exists, the
migration fails instead of overwriting it.
