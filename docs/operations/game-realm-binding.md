# Game database realm binding (schema v10)

Each realm is deployed with its own Game database and GameServer process. The GameServer's
`GameServer:RealmId` setting (a nonzero uint) names its realm, and the Game database stores one
matching realm ID in its `realm_binding` table. The GameServer compares the two after the schema
readiness check, before it loads world content, accepts player connections, or publishes world
state. A missing or mismatched ID stops startup. There is no default, no implied realm 1, and no
fallback to another database.

## Migration and provisioning

1. Back up the Game database and confirm that you can restore it. Stop its GameServer.
2. Apply `20261013030000_AddGameRealmBinding` as an administrator, using
   `dotnet tool run dotnet-ef -- database update --configuration Release
   --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj
   --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj
   --context GameDbContext --connection "$GAME_DB_ADMIN_CONNECTION"`.
3. Check that `schema_compatibility` shows `game / 10 / 20261013030000_AddGameRealmBinding`.
4. Look up the `realm_id` the operator assigned to this realm in the `realms` registry in the
   Accounts database. Do not derive it from a client label or the Game database name, and do not
   seed it automatically.
5. In the Game database, insert one row with that ID:

   ```sql
   INSERT INTO `realm_binding` (`binding_key`, `realm_id`)
   VALUES (1, <ACTUAL_REALM_ID>);
   ```

   Replace the placeholder with the nonzero realm ID before you run it. The database enforces
   `binding_key = 1` and `realm_id > 0` and rejects a second row. Never update an existing binding
   to reuse a populated database for another world.
6. Set `GameServer:RealmId` to the same ID in your deployment configuration, or in the GameServer's
   .NET user secrets for local development:

   ```bash
   dotnet user-secrets set 'GameServer:RealmId' '<ACTUAL_REALM_ID>' \
     --project src/OpenConquer.GameServer
   ```

7. Grant the GameServer runtime database account only `SELECT` on `realm_binding`. Do not grant
   `INSERT`, `UPDATE`, or `DELETE` to the runtime or creation accounts.
8. Check the binding and the permissions, then start the GameServer.

## Availability and failure handling

The migration creates an empty binding table, and the GameServer will not start until the operator
has added the row. MySQL commits DDL outside EF's transaction. If CREATE TABLE succeeds but the
migration history is not recorded, stop the deployment and inspect the table definition and schema
markers. Do not rerun the migration without checking first, edit the EF migration history, or delete
the table to force the migration through. If a table it does not recognize already exists, the
migration fails. The migration does not modify characters, items, or syndicates, and it refuses a
destructive downgrade.

The binding is one half of realm isolation. The other half is the realm-bound ticket (Accounts v4,
see [realm-bound tickets](realm-bound-tickets.md)), which a GameServer redeems only for its own
configured realm. Adding a realm end to end is covered in [Realm operations](realms.md).
