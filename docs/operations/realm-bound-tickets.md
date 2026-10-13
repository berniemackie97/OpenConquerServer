# Accounts v4: realm-bound login tickets

This migration is an **incompatible security cutover**, not a live/rolling deployment. It adds a required `game_login_tickets.realm_id` and invalidates outstanding short-lived tickets from Accounts v3; no ticket is ever assigned an inferred realm identity. Old AccountServer and GameServer binaries cannot operate against schema v4.

## Operator preflight

1. Merge only after CI's locked restore, formatting, Release compilation, both EF model checks, GameServer/AccountServer unit tests, MySQL Testcontainers integration and migration tests succeed.
2. Stop the AccountServer and **every** GameServer using the shared Accounts database. Ensure all outstanding five-minute logins can safely be invalidated. Do not run this with active login traffic.
3. Back up Accounts and confirm a restore path; MySQL DDL commits independently, so `database update` is not fully transactional.
4. Verify that `openconquer_accounts.schema_compatibility` is `accounts / 3 / 20261013010000_AddRealmRegistry`, that `realms` has exactly the intended operator-provisioned registrations, and that this local Game database's `realm_binding` is `(1,1)`.
5. For this workstation only: `realm_id=1`, native `LocalRealm`, GameServer `127.0.0.1:5816`, AccountServer `127.0.0.1:9958`. These are already operator-provisioned; **do not insert them again**. Client catalog IDs `local` and `local-realm` are unrelated to the stable numeric Realm ID.
6. Preserve existing connection string and verification key User Secrets. Confirm `GameServer:RealmId=1` in Development. GameServer must have `SELECT, DELETE` on `game_login_tickets` and `SELECT` on schema compatibility and the Game database realm binding; AccountServer needs `SELECT, INSERT, DELETE` on tickets and `SELECT` on realm registry. The normal runtime users do not need schema DDL permissions.

## Apply the existing migration with EF Core

Use the existing administrative **Accounts** connection string (not the Game database connection). From the repository root, with both servers stopped:

```bash
dotnet tool restore

dotnet tool run dotnet-ef -- database update \
  --configuration Release \
  --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
  --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
  --context AccountDbContext \
  --connection "$ACCOUNTS_DB_ADMIN_CONNECTION"
```

**Do not run this command with an unset connection variable**—that causes `Access denied for user ''@'localhost'`. Read your existing administrative credential from its approved local secret store; never paste it into chat or commit it. `dotnet user-secrets list --project src/OpenConquer.AccountServer` can inspect the application secret keys, but the normal AccountServer runtime identity may not have DDL privileges. Keep the admin credential separate. The `--connection` value is passed as a process argument; use this only on your trusted development machine.

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

Expected: Accounts version **4**, migration `20261013050000_BindGameLoginTicketsToRealm`; `realm_id INT UNSIGNED NOT NULL` and `CK_game_login_tickets_realm_id`; zero old tickets immediately after migration; `LocalRealm` remains `(1, 'LocalRealm', '127.0.0.1', 5816, 1)` and Game binding `(1,1)`. Game schema remains v10.

If migration fails or DDL partially commits, **stop**. Inspect table structure, EF history and schema compatibility before any retry. Never fabricate a realm ID or manually advance migration history. Do not restart either server on a mismatched version.

## Security limits

Realm identity comes from the validated 1060 selection, not a client-supplied numeric ID. Tickets are bound in the Accounts transaction and redemption must verify the configured GameServer realm in the same row-lock-and-delete transaction. The pre-v4 ticket shape is not accepted. Existing static 1055 redirects remain unchanged: this release does **not** enable dynamic multi-realm routing or remote clients.
