# Database identities

Each server process connects with one MySQL identity, and schema changes use a separate
administrator. A second identity inside the same process would sit in the same memory and
configuration as the first, so it adds secrets to manage without isolating anything. Isolation comes
from scoping each identity's grants to the tables and columns it uses, and from giving every realm
its own GameServer identity.

| Identity                          | Configured as                                              | Used for                                                                     |
| --------------------------------- | ---------------------------------------------------------- | ---------------------------------------------------------------------------- |
| AccountServer runtime             | AccountServer `ConnectionStrings:Accounts`                 | Authentication, tickets, sessions, the realm directory, account registration |
| GameServer runtime, one per realm | That GameServer's `ConnectionStrings:Accounts` and `ConnectionStrings:Game` | Tickets, sessions, realm status, characters, character creation, location writes |
| Administrator                     | Never in a server's configuration                          | Migrations, realm registration (`realms`, `realm_binding`)                   |

`ConnectionStrings:AccountRegistration`, `ConnectionStrings:GameCreation` and
`ConnectionStrings:GameLocationWriter` named separate identities in earlier versions. They are no
longer read, and a server that finds one refuses to start so that nobody relies on an isolation that
no longer exists.

No identity has `DELETE` on account or character rows, DDL, or global privileges. Restrict each
user's host to the machine its server runs on, use protected transport where the database is not
local, and keep connection strings in user secrets (development) or the deployment's secret store.
Check the result with `SHOW GRANTS FOR '<user>'@'<host>';`.

## AccountServer runtime

```sql
GRANT SELECT, INSERT, UPDATE ON `openconquer_accounts`.`accounts`                     TO '<account_server>'@'<host>';
GRANT SELECT, INSERT, UPDATE ON `openconquer_accounts`.`account_password_credentials` TO '<account_server>'@'<host>';
GRANT SELECT, INSERT         ON `openconquer_accounts`.`account_audit_events`         TO '<account_server>'@'<host>';
GRANT SELECT, INSERT, DELETE ON `openconquer_accounts`.`game_login_tickets`           TO '<account_server>'@'<host>';
GRANT SELECT ON `openconquer_accounts`.`schema_compatibility` TO '<account_server>'@'<host>';
GRANT SELECT ON `openconquer_accounts`.`realms`               TO '<account_server>'@'<host>';
GRANT SELECT ON `openconquer_accounts`.`realm_status`         TO '<account_server>'@'<host>';
GRANT SELECT ON `openconquer_accounts`.`account_sessions`     TO '<account_server>'@'<host>';
GRANT INSERT, UPDATE ON `openconquer_accounts`.`account_session_kicks` TO '<account_server>'@'<host>';
```

Registration needs nothing more: it inserts into `accounts`, `account_password_credentials` and
`account_audit_events`, and reads `accounts` for its `INSERT … SELECT` statements and for the
read-back of a commit whose acknowledgment was lost.

## GameServer runtime, per realm

On Accounts, which every realm shares:

```sql
GRANT SELECT, DELETE                 ON `openconquer_accounts`.`game_login_tickets`    TO '<game_server>'@'<host>';
GRANT SELECT, INSERT, UPDATE, DELETE ON `openconquer_accounts`.`realm_status`          TO '<game_server>'@'<host>';
GRANT SELECT, INSERT, UPDATE, DELETE ON `openconquer_accounts`.`account_sessions`      TO '<game_server>'@'<host>';
GRANT SELECT, INSERT, UPDATE, DELETE ON `openconquer_accounts`.`account_session_kicks` TO '<game_server>'@'<host>';
GRANT SELECT ON `openconquer_accounts`.`schema_compatibility` TO '<game_server>'@'<host>';
```

On the realm's own Game database:

```sql
GRANT SELECT, INSERT ON `<game_database>`.`characters` TO '<game_server>'@'<host>';
GRANT UPDATE (`map_id`, `position_x`, `position_y`, `location_revision`, `last_location_operation_id`, `location_writer_token`)
    ON `<game_database>`.`characters` TO '<game_server>'@'<host>';
GRANT SELECT ON `<game_database>`.`items`                 TO '<game_server>'@'<host>';
GRANT SELECT ON `<game_database>`.`social_relations`      TO '<game_server>'@'<host>';
GRANT SELECT ON `<game_database>`.`weapon_skills`         TO '<game_server>'@'<host>';
GRANT SELECT ON `<game_database>`.`magic`                 TO '<game_server>'@'<host>';
GRANT SELECT ON `<game_database>`.`syndicates`            TO '<game_server>'@'<host>';
GRANT SELECT ON `<game_database>`.`syndicate_memberships` TO '<game_server>'@'<host>';
GRANT SELECT ON `<game_database>`.`schema_compatibility`  TO '<game_server>'@'<host>';
GRANT SELECT ON `<game_database>`.`realm_binding`         TO '<game_server>'@'<host>';
```

`INSERT` on `characters` serves character creation. The column-scoped `UPDATE` covers the location
CAS and the writer fence; every other character column stays read-only. A network-enabled
GameServer runs a no-row `SELECT` and `UPDATE` against those columns before it opens its listener,
so a missing grant stops startup. The probe does not show that excessive privileges are absent.

Give each realm its own GameServer user, so that one GameServer's credentials cannot reach another
realm's characters. On the Accounts side all realms are still one trust domain: every GameServer
holds the ticket verification keys, can read and delete any realm's tickets, and could write another
realm's status report, account session records or kicks. Realms run by different parties need
separate deployments.

## Administrator

The schema owner, with DDL rights on both databases (`root` in local development). Use it for
`dotnet ef database update` and for writing `realms` and `realm_binding`, as described in
[Database migrations](database-migrations.md) and [Realm registry](realm-registry.md). Never put its
connection string in a server's configuration.
