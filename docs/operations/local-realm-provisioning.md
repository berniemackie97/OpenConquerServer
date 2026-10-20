# LocalRealm provisioning (development only)

This runbook uses the identities from the existing publisher-signed client catalog, not sample
names from the documentation:

| Identity | Value |
| --- | --- |
| Client region ID | `local` |
| Client server ID | `local-realm` |
| Packet 1060 `ServerName` | `LocalRealm` |
| Permanent internal `RealmId` | `1` (operator-assigned) |
| AccountServer | `127.0.0.1:9958` |
| GameServer endpoint advertised in 1055 | `127.0.0.1:5816` |
| Directory | `https://localhost/realms.json` + `.sig` (Caddy only) |

Stop both servers before provisioning, and keep a database backup that you have checked you can
restore. These identities are permanent and are not derived from the display label. The client
server ID `local-realm` is a different value from the packet 1060 name `LocalRealm`, and both
differ from the numeric realm ID `1`.

Run the guarded SQL from an administrative MySQL session. Before either insert, its checks require
Accounts schema v3 or later and Game schema v10 or later (the versions that add `realms` and
`realm_binding`), no conflicting identity, and no existing realm binding. If any check fails, the
script aborts before inserting anything. Review the whole transaction before you run it. The
transaction covers both databases only when they are on the same MySQL instance. It is not a
distributed transaction across separate instances.

The script is `ops/provision-localrealm.sql`. Run it with your MySQL administrative credentials,
for example from a MySQL Workbench SQL editor. It checks both schema versions, checks that neither
`realm_id=1` nor `LocalRealm` is registered yet, and checks that the Game database has no binding.
It then inserts the two records in one transaction. **Never edit the SQL to replace conflicting
records.** The script creates a stored procedure, which needs the administrative `CREATE ROUTINE`
privilege. Do not grant that privilege to a runtime principal.

If an earlier attempt failed, inspect the database and the stored procedure it installed before
you decide whether to retry. Do not replace stored routines without inspecting them first. A
successful run drops its temporary procedure automatically.

After provisioning, set the realm ID in the GameServer's persistent application configuration:

```bash
dotnet user-secrets set 'GameServer:RealmId' '1' --project src/OpenConquer.GameServer
```

Grant `SELECT` on `openconquer_accounts.realms` to the MySQL principal that the AccountServer
actually runs as, and `SELECT` on `openconquer_game.realm_binding` to the principal that the
GameServer actually runs as. Do not grant any DML on these tables, and do not make up a user or
host for these grants. Find each principal from the server's effective connection string, and
check its current grants with `SHOW GRANTS FOR '<principal>'@'<host>';` before you change anything.

For local development, set `GameServer:Network:BindAddress=127.0.0.1` so the GameServer listens
only on loopback. This setting is local and does not change any other deployment. To confirm the
listeners, run `lsof -nP -iTCP:5816 -sTCP:LISTEN` and `lsof -nP -iTCP:9958 -sTCP:LISTEN`. Each
shows a listener only while the matching server is running.

## Security

The signed client directory tells the client which AccountServer to use and which protocol server
name to send. Caddy serves only the directory and runs no game services. The AccountServer refuses
unknown or unavailable realm names, binds every ticket to the selected realm, and redirects the
client to that realm's registered endpoint. A GameServer redeems only tickets bound to its own
realm. To add a second realm, follow [Realm operations](realms.md).
