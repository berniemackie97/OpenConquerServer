# Realm operations

A realm is one world a player can select at login. Every realm has its own GameServer process and its
own Game database; all realms share the AccountServer, the Accounts database and the ticket
verification keys. The design is described in [Multi-realm](../architecture/multi-realm.md).

| Part                 | Where it lives                                                         | Rules                                                                 |
| -------------------- | ---------------------------------------------------------------------- | --------------------------------------------------------------------- |
| Protocol server name | `realms.protocol_server_name`; the client catalog's `protocolServerName` | 1–15 characters from `[A-Za-z0-9_()-]`, case-sensitive, permanent     |
| Realm ID             | `realms.realm_id`; the Game database's `realm_binding`; `GameServer:RealmId` | Nonzero, permanent, never reused; internal only                  |
| Public endpoint      | `realms.public_game_address`, `public_game_port`                       | IPv4 address and port players can reach; sent to the client in 1055  |
| Availability         | `realms.availability`                                                  | `1` Enabled, `2` Maintenance, `3` Disabled                            |
| Game database        | One per realm                                                          | Never shared with another realm                                       |
| GameServer           | One process per realm                                                  | Its own listener, Game database credentials and `GameServer:RealmId`  |

The 5517 client sends the protocol server name in packet 1060 and also uses it as the folder name for
its local settings and chat logs, so renaming a realm loses those for every player. A rename is a new
realm.

## What players see

The AccountServer reads the registry for every login, before checking the password and again before
issuing the ticket.

| State of the selected realm                     | Login result                                        |
| ----------------------------------------------- | --------------------------------------------------- |
| Enabled, GameServer reporting normal load       | Ticket bound to the realm, redirect to its endpoint |
| Enabled, GameServer reporting busy              | Failure code 20, "Server is busy."                  |
| Enabled, GameServer reporting full              | Failure code 21, "Server is busy. Please try again, later." |
| Enabled, GameServer not reporting               | Failure code 10, "The Server is down."              |
| Maintenance                                     | Failure code 70, "Server Locked"                    |
| Disabled, unknown or malformed                  | Failure code 10, "The Server is down."              |

A registry change applies to the next login. Players already in the world stay connected, and a
ticket issued before the change can still be redeemed until it expires five minutes later. A
GameServer reports its load every `GameServer:RealmStatus:ReportInterval` (10 seconds by default) and
deletes its report when it shuts down, so a stopped realm refuses logins at once; one that crashes
is refused within three intervals. To close a realm at once, set it to Maintenance and then stop its
GameServer.

## Adding a realm

The examples use realm ID `2`, protocol server name `SecondRealm`, Game database
`openconquer_game_second`, public endpoint `203.0.113.10:5817`, and MySQL users
`openconquer_game_second_runtime` and `openconquer_game_second_creation`. Substitute the real values;
take the realm ID and name from your own allocation, never from a client display label. Run the SQL
as an administrator.

1. Create the Game database:

   ```sql
   CREATE DATABASE `openconquer_game_second` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_as_cs;
   ```

2. Migrate it to the current Game schema with `--context GameDbContext` and a connection string for
   the new database, as described in [Database migrations](database-migrations.md). Confirm that its
   `schema_compatibility` row shows the current Game version.

3. Bind the database to the realm. The table accepts exactly one row, and the binding is permanent:

   ```sql
   INSERT INTO `openconquer_game_second`.`realm_binding` (`binding_key`, `realm_id`) VALUES (1, 2);
   ```

4. Register the realm in Maintenance, so that no player is routed to it yet. MySQL rejects a duplicate
   ID or name, a name outside the allowed characters, `0.0.0.0`, `255.255.255.255` and port 0:

   ```sql
   INSERT INTO `openconquer_accounts`.`realms`
       (`realm_id`, `protocol_server_name`, `public_game_address`, `public_game_port`, `availability`)
   VALUES (2, 'SecondRealm', INET6_ATON('203.0.113.10'), 5817, 2);
   ```

5. Create the realm's database users and grant them only what the [grants](#grants) table lists.

6. Configure the GameServer: `GameServer:RealmId` = `2`, `GameServer:Network:Port` = `5817`,
   `ConnectionStrings:Game` for the new database, `ConnectionStrings:GameCreation` if character
   creation is configured, `ConnectionStrings:Accounts`, and the same verification keys as the
   AccountServer. See [GameServer operations](game-server.md).

7. Start the GameServer. It refuses to start unless `GameServer:RealmId` matches the database's
   binding, and once its listener is open it starts reporting the realm's load. Until the first report
   the AccountServer answers logins to the realm with "The Server is down."

8. Publish the realm in the client catalog with `protocolServerName` = `SecondRealm` and the
   **AccountServer** endpoint as its account host and port. Clients never connect to a GameServer
   endpoint from the catalog; the AccountServer sends it after login.

9. Enable the realm:

   ```sql
   UPDATE `openconquer_accounts`.`realms` SET `availability` = 1 WHERE `realm_id` = 2;
   ```

## Changing a realm

Run these against the Accounts database.

| Change          | Statement                                                                      |
| --------------- | ------------------------------------------------------------------------------ |
| Maintenance     | `UPDATE realms SET availability = 2 WHERE realm_id = 2;`                      |
| Enable          | `UPDATE realms SET availability = 1 WHERE realm_id = 2;`                      |
| Move endpoint   | `UPDATE realms SET public_game_address = INET6_ATON('203.0.113.11'), public_game_port = 5817 WHERE realm_id = 2;` |
| Retire          | `UPDATE realms SET availability = 3 WHERE realm_id = 2;`                      |

Retiring keeps the row so the ID and name stay reserved; also remove the realm from the client
catalog. Never update `realm_id` or `protocol_server_name`, and never delete or update a
`realm_binding` row.

## Grants

| User                              | Database          | Privileges                                                                                 |
| --------------------------------- | ----------------- | ------------------------------------------------------------------------------------------ |
| AccountServer runtime             | Accounts          | `SELECT, INSERT, UPDATE` on `accounts`, `account_password_credentials`; `SELECT, INSERT` on `account_audit_events`; `SELECT, INSERT, DELETE` on `game_login_tickets`; `SELECT` on `schema_compatibility`, `realms`, `realm_status` |
| GameServer runtime, each realm    | Accounts          | `SELECT, DELETE` on `game_login_tickets`; `SELECT, INSERT, UPDATE, DELETE` on `realm_status`; `SELECT` on `schema_compatibility` |
| GameServer runtime, each realm    | Its Game database | `SELECT` on `characters`, `items`, `social_relations`, `weapon_skills`, `magic`, `syndicates`, `syndicate_memberships`, `schema_compatibility`, `realm_binding` |
| GameServer creation, each realm   | Its Game database | `SELECT, INSERT` on `characters`                                                           |
| Administrator                     | Both              | Schema changes and writes to `realms` and `realm_binding`; never used by a server          |

Give each realm its own Game database users, so that one GameServer's credentials cannot reach another
realm's characters. All realms remain one trust domain on the Accounts side: every GameServer holds the
ticket verification keys, can read and delete any realm's tickets, and could write another realm's
status report. Realms run by different parties
need separate deployments.

## Several realms on one machine

User secrets belong to the project, so every GameServer started from the same source tree reads the
same ones. Give each additional GameServer its own values through environment variables, which
override user secrets:

```bash
GameServer__RealmId=2 \
GameServer__Network__Port=5817 \
ConnectionStrings__Game="$SECOND_GAME_DB_CONNECTION" \
DOTNET_ENVIRONMENT=Development dotnet run --project src/OpenConquer.GameServer -c Release --no-build
```

For a loopback realm, register `127.0.0.1` with the GameServer's port.
