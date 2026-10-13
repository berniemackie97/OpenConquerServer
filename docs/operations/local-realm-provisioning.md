# LocalRealm — operator provisioning (development only)

This runbook uses the **existing publisher-signed client catalog**, not documentation sample names:

| Identity | Value |
| --- | --- |
| Client region ID | `local` |
| Client server ID | `local-realm` |
| Native packet 1060 `ServerName` | `LocalRealm` |
| Permanent internal `RealmId` | `1` (operator-assigned) |
| AccountServer | `127.0.0.1:9958` |
| GameServer endpoint advertised in 1055 | `127.0.0.1:5816` |
| Directory | `https://localhost/realms.json` + `.sig` (Caddy only) |

Stop both servers before provisioning; keep a verified database backup. These are
permanent identities, not names derived from the display label. The client-selected
`local-realm` is **not** packet 1060 `LocalRealm`, and neither is numeric `1`.

Apply the guarded SQL using a privileged **administrative** MySQL session. The
preflight guards require both exact schema markers, no conflicting identity and no
existing realm binding, and abort before either insert. Review the full transaction
first. The transaction covers both databases only when hosted on the same MySQL
instance; do not treat it as a distributed transaction across separate instances.

Run the accompanying `ops/provision-localrealm.sql` with the actual MySQL
administrative credentials, for example from a MySQL Workbench SQL editor.
It validates both schema markers, that neither `realm_id=1` nor `LocalRealm`
has been registered, and that the Game database contains no binding. It then
inserts the two records within one transaction. **Never edit the SQL to
replace conflicting records.** Procedure creation requires administrative
`CREATE ROUTINE` rights; do not grant these rights to a runtime principal.

If a previous attempt failed, inspect the database and the installed stored
procedure before deciding whether to retry; do not blindly replace stored
routines. A successful run drops its temporary procedure automatically.

After provisioning, set the GameServer's persistent application configuration:

```bash
dotnet user-secrets set 'GameServer:RealmId' '1' --project src/OpenConquer.GameServer
```

Grant `SELECT` on `openconquer_accounts.realms` to the **actual** AccountServer
runtime MySQL principal, and `SELECT` on `openconquer_game.realm_binding` to the
**actual** GameServer runtime principal. Do not invent a user/host or grant any DML
on these tables. Determine the principals from their effective connection strings,
then inspect `SHOW GRANTS FOR '<principal>'@'<host>';` before changes.

To restrict local GameServer binding without touching global deployments, configure
`GameServer:Network:BindAddress=127.0.0.1` for local development. `lsof -nP
-iTCP:5816 -sTCP:LISTEN` and `lsof -nP -iTCP:9958 -sTCP:LISTEN` confirm actual
listeners only when the corresponding server is running.

## Security boundary

The signed client directory establishes the selected AccountServer and native
protocol server name; Caddy does not run game services. The AccountServer now
refuses unknown/unavailable realm names and mismatched **static** GameServer
endpoints. **Tickets are still not realm-bound** and an unrelated GameServer
configured against the shared Accounts DB may still redeem an unbound ticket.
Do not expose additional realm destinations until account-ticket schema v4 and
realm-bound atomic redemption are implemented and verified.
