# Accounts v4: realm-bound login tickets

This migration is a breaking security change. It cannot be applied as a live or rolling deployment. It adds a required `game_login_tickets.realm_id` column and invalidates the outstanding short-lived tickets from Accounts v3. No existing ticket is given a guessed realm. Old AccountServer and GameServer binaries cannot run against schema v4.

## Before you start

1. Deploy only a build that passed the full CI run: locked restore, formatting, Release compilation, both EF model checks, the GameServer and AccountServer unit tests, and the MySQL Testcontainers integration and migration tests.
2. Stop the AccountServer and every GameServer that uses the shared Accounts database. Make sure it is safe to invalidate every outstanding login ticket (each is valid for five minutes). Do not run the migration while logins are in progress.
3. Back up the Accounts database and confirm that you can restore it. MySQL commits each DDL statement on its own, so `database update` is not fully transactional.
4. Check that `openconquer_accounts.schema_compatibility` is `accounts / 3 / 20261013010000_AddRealmRegistry`, that `realms` contains only the registrations you provisioned, and that the local Game database's `realm_binding` is `(1,1)`.
5. On the local development workstation, the values are `realm_id=1`, protocol server name `LocalRealm`, GameServer `127.0.0.1:5816` and AccountServer `127.0.0.1:9958`. They are already provisioned, so do not insert them again. The client catalog IDs `local` and `local-realm` are unrelated to the permanent numeric realm ID.
6. Keep the existing user secrets for connection strings and verification keys. Confirm that `GameServer:RealmId=1` is set in Development. The GameServer needs `SELECT, DELETE` on `game_login_tickets`, and `SELECT` on the schema compatibility table and the Game database realm binding. The AccountServer needs `SELECT, INSERT, DELETE` on tickets and `SELECT` on the realm registry. The normal runtime users do not need DDL permissions.

## Apply the migration with EF Core

Use the administrative connection string for the Accounts database, not the Game database. From the repository root, with both servers stopped, run:

```bash
dotnet tool restore

dotnet tool run dotnet-ef -- database update \
  --configuration Release \
  --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
  --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
  --context AccountDbContext \
  --connection "$ACCOUNTS_DB_ADMIN_CONNECTION"
```

**Do not run this command with the connection variable unset.** MySQL then fails with `Access denied for user ''@'localhost'`. Load the administrative credential from its approved local secret store. Never paste it into chat or commit it. `dotnet user-secrets list --project src/OpenConquer.AccountServer` shows the application's secret keys, but the normal AccountServer runtime identity may not have DDL privileges, so keep the admin credential separate. The `--connection` value is passed as a process argument, so run this only on a trusted development machine.

## Verification

```sql
SELECT component_name, schema_version, migration_id FROM openconquer_accounts.schema_compatibility;
SELECT MigrationId FROM openconquer_accounts.__EFMigrationsHistory ORDER BY MigrationId;
SHOW CREATE TABLE openconquer_accounts.game_login_tickets;
SELECT COUNT(*) FROM openconquer_accounts.game_login_tickets;
SELECT realm_id, protocol_server_name, INET6_NTOA(public_game_address), public_game_port, availability
FROM openconquer_accounts.realms;
SELECT binding_key, realm_id FROM openconquer_game.realm_binding;
```

Expected results:

- Accounts version 4, with migration `20261013050000_BindGameLoginTicketsToRealm`.
- The ticket table has `realm_id INT UNSIGNED NOT NULL` and the check constraint `CK_game_login_tickets_realm_id`.
- No old tickets remain immediately after the migration.
- `LocalRealm` is still `(1, 'LocalRealm', '127.0.0.1', 5816, 1)` and the Game binding is still `(1,1)`.
- The Game schema stays at v10.

If the migration fails or its DDL commits only in part, stop. Inspect the table structure, the EF history and the schema compatibility row before any retry. Never make up a realm ID or advance the migration history by hand. Do not restart either server while the schema version does not match.

## Security

The realm comes from the validated server name in packet 1060. The client never supplies a numeric realm ID. A ticket is bound to its realm in the same Accounts transaction that issues it. On redemption, the GameServer checks the ticket against its configured realm in the same transaction that locks and deletes the ticket row. Tickets in the pre-v4 shape are not accepted. A successful 1055 redirect comes from the selected realm's registration, and a refused selection gets the 5517 availability codes; see [Realm operations](realms.md).
