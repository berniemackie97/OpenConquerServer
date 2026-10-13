# Game database realm binding — schema v10

Each independently deployed realm has its own Game database and GameServer process.
The GameServer has an explicit `GameServer:RealmId` configuration value (nonzero uint).
The Game database records exactly one matching identity in its `realm_binding` table.
The GameServer verifies it after schema readiness and **before** loading world content,
opening gameplay ingress, or publishing world state. A missing or mismatched identity
is a startup failure; there is no default, implicit realm 1, or fallback to another DB.

## Migration and provisioning

1. Back up the intended Game database and verify the restore procedure. Stop its GameServer.
2. Apply `20261013030000_AddGameRealmBinding` as an administrator, using
   `dotnet tool run dotnet-ef -- database update --configuration Release
   --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj
   --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj
   --context GameDbContext --connection "$GAME_DB_ADMIN_CONNECTION"`.
3. Verify `schema_compatibility` says `game / 10 / 20261013030000_AddGameRealmBinding`.
4. Look up the **actual, operator-assigned** `realm_id` in the Accounts DB `realms`
   registry. Do not infer or auto-seed it from a client label or Game database name.
5. Against the Game DB, provision exactly one row, using the verified ID:

   ```sql
   INSERT INTO `realm_binding` (`binding_key`, `realm_id`)
   VALUES (1, <ACTUAL_REALM_ID>);
   ```

   Replace the placeholder with a validated nonzero integer before execution.
   The database enforces `binding_key = 1` and `realm_id > 0`; a second row is rejected.
   Existing realm bindings must never be silently updated to repurpose a populated
   database for another world.
6. Configure `GameServer:RealmId` with that same ID, via deployment configuration
   or the GameServer's .NET User Secrets in local development:

   ```bash
   dotnet user-secrets set 'GameServer:RealmId' '<ACTUAL_REALM_ID>' \
     --project src/OpenConquer.GameServer
   ```

7. Grant the GameServer runtime DB identity only `SELECT` on `realm_binding`.
   Do not grant `INSERT`, `UPDATE`, or `DELETE` to the runtime or creation users.
8. Start the GameServer only after verifying the binding and permissions.

## Availability and failure handling

The migration intentionally creates an empty binding table. Startup refuses until
operator provisioning is complete. MySQL DDL commits outside EF's transaction: if
CREATE TABLE succeeds but migration history is not recorded, stop deployment and
inspect the exact table definition and schema markers; do not re-run blindly, edit
EF migration history, or delete the table to make a migration succeed. An unknown
preexisting table is a hard failure. The migration does not modify characters,
items, or syndicates and refuses destructive downgrade.

The binding is one half of realm isolation; the other is the realm-bound ticket
(Accounts v4, see [realm-bound tickets](realm-bound-tickets.md)), which a GameServer
redeems only for its own configured realm. Adding a realm end to end is covered in
[Realm operations](realms.md).
