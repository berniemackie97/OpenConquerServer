# Database migrations

Both databases are migrated with EF Core: `AccountDbContext` for Accounts and `GameDbContext` for
Game. Each database also has a `schema_compatibility` row (component `accounts` or `game`) recording
the schema version and latest migration ID. The servers check that row at startup and refuse to
start on a mismatch. They never migrate on their own.

| Database | Version | Migration                                     |
| -------- | ------: | --------------------------------------------- |
| Accounts |       1 | `20260906043138_InitialAccountSchema`         |
| Accounts |       2 | `20260907004603_ProtectGameLoginTicketAuthenticationKey` |
| Accounts |       3 | `20261013010000_AddRealmRegistry`             |
| Accounts |       4 | `20261013050000_BindGameLoginTicketsToRealm`  |
| Accounts |       5 | `20261013070000_AddRealmStatus`               |
| Accounts |       6 | `20261013090000_AddAccountSessions`           |
| Game     |       1 | `20260914210346_InitialGameSchema`            |
| Game     |       2 | `20260922025854_UseSignedCharacterPkPoints`   |
| Game     |       3 | `20260923223920_RetainPreRebirthLevel`        |
| Game     |       4 | `20260927121811_AddItemPersistenceFoundation` |
| Game     |       5 | `20260927225127_AddItemLifetimePersistence`   |
| Game     |       6 | `20260930033806_AddSocialRelationPersistence` |
| Game     |       7 | `20261004014402_AddWeaponSkillPersistence`    |
| Game     |       8 | `20261004193352_AddMagicPersistence`          |
| Game     |       9 | `20261008033456_AddSyndicatePersistence`      |
| Game     |      10 | `20261013030000_AddGameRealmBinding`          |
| Game     |      11 | `20261018040000_AddCharacterLocationRevision` |
| Game     |      12 | `20261018190000_AddCharacterLocationOperationId` |
| Game     |      13 | `20261019010000_AddCharacterLocationWriterFence` |

## Game v11: location revision

The v11 migration adds `characters.location_revision`, set to zero for existing characters.

1. Stop every process that writes to `characters` (gameplay, administrative, maintenance, and custom
   writers). Keep them stopped until all of them run v11 code.
2. Take a backup and confirm that it restores.
3. Update the existing location-writer account's column-scoped `SELECT`/`UPDATE` grant to include
   `location_revision` (see `docs/architecture/durable-character-location.md`). Do not give it write
   access to the whole table.

The migration cannot be reversed. A stored revision does not tell you the outcome of a SQL command
that is still running. No live transfer coordinator exists yet.

## Game v12: location operation ID

The v12 migration runs offline and adds the nullable column
`characters.last_location_operation_id BINARY(16)`. Existing rows keep `NULL`. Every successful
location-revision compare-and-swap (CAS) writes the required operation ID in the same atomic update.

1. Stop every writer to `characters`, take a backup, and confirm that it restores.
2. Apply the migration.
3. Update the column-scoped `SELECT` and `UPDATE` grants of the opt-in location writer, and no other
   account, to include `last_location_operation_id`.
4. Deploy the v12 components. Older writers must not go on writing character locations.

The migration cannot be safely downgraded.

A matching operation ID proves that one CAS completed. A missing or different ID does not prove that
an uncertain SQL command still in flight will never complete. Production transfers stay disabled.
See [durable character location](../architecture/durable-character-location.md).

## Game v13: location writer fencing

The v13 migration adds the nullable column `characters.location_writer_token BINARY(16)`. Existing
rows start without a token, so no writer holds write rights for them yet. Only the new location
writer can rotate a token, and every v13 CAS requires a matching, nonempty token. Do these steps as
one maintenance operation:

1. Stop all pre-v13 location writers.
2. Take a backup and confirm that it restores.
3. Run the migration offline.
4. Update the location writer's column-scoped `SELECT`/`UPDATE` grants.
5. Deploy the v13 binaries.

The read-only GameServer role must not get an UPDATE privilege. A v12 writer skips the new token
check, so old and new binaries must never run at the same time.

The migration accepts a database at v12, or one that already has the v13 compatibility marker. MySQL
can commit the DDL before EF records the migration as complete. When the migration runs again, it
accepts an existing writer-token column only if it is a nullable BINARY(16). Any other schema shape
or compatibility marker stops it with an error. Check the database state, then rerun the interrupted
migration through EF. The migration cannot be reversed.

The internal coordinator that runs without a session gets a new write generation after it seals its
source, and refuses to continue if the stored source has changed. This orders earlier in-flight CAS
writes before the token rotation. It does not give one process or session exclusive ownership across
restarts. Live transfers stay disabled until login admission and realm leadership enforce that.

## Before you start

- Stop every process that writes to the database. Migration checks validate what they see. They
  cannot protect against concurrent writers.
- Take a backup and confirm you can restore it.
- Double-check the connection string points at the intended database, using an account that can
  change the schema.
- Never edit `__EFMigrationsHistory` or `schema_compatibility` by hand, and never invent data to
  get a migration through.

## Applying

```bash
dotnet tool restore

dotnet tool run dotnet-ef -- database update \
  --configuration Release \
  --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
  --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
  --context GameDbContext \
  --connection "$GAME_DB_CONNECTION"
```

Use `--context AccountDbContext` for the Accounts database. Add a migration ID after `update` to
migrate to, or resume, a specific version.

## Checking state

```sql
SELECT `MigrationId` FROM `__EFMigrationsHistory` ORDER BY `MigrationId`;

SELECT `component_name`, `schema_version`, `migration_id`, `applied_at_utc`
FROM `schema_compatibility`;
```

The history must contain every migration up to the current version, and the compatibility row must
show that version and its migration ID. `SHOW CREATE TABLE <name>;` is the quickest way to compare a
table with what the migration expects.

## Recovering from a failed migration

MySQL commits DDL immediately, independently of EF's migration history. A failed migration can
therefore leave some tables or columns created while the history and the compatibility row still
show the previous version. Every Game migration recognizes the partial states it can produce and
resumes from them. If it finds a structure it does not recognize, such as a table or constraint with
the expected name but a different definition, it stops instead of guessing.

To recover: inspect the actual structure, fix any data the migration rejected (see below), and run
the same migration again. Do not mark it complete manually.

| Version | Change                                     | Fails when                                         |
| ------: | ------------------------------------------ | -------------------------------------------------- |
|       2 | `characters.pk_points` becomes signed      | Any value is above 32,767                          |
|       3 | Adds `characters.pre_rebirth_level`        | A reborn character has no known pre-rebirth level  |
|       4 | Adds `items`                               | An incompatible `items` table already exists       |
|       5 | Adds item lifetime columns, index, check   | `items` contains rows                              |
|       6 | Adds `social_relations`                    | An incompatible table already exists               |
|       7 | Adds `weapon_skills`                       | An incompatible table already exists               |
|       8 | Adds `magic`                               | An incompatible table already exists               |
|       9 | Adds `syndicates`, `syndicate_memberships` | An incompatible table already exists               |

### Game version 10: realm identity binding

Each Game database has a `realm_binding` table with one row, which the operator fills in. The
migration does not insert or guess a realm ID. The GameServer refuses to start unless
`GameServer:RealmId` matches that row, and the runtime account needs only `SELECT`. See
[game realm binding](game-realm-binding.md). Downgrading from v10 to v9 is blocked because it would
discard the world's stored realm identity.

### Accounts version 3: realm registry

The operator-managed `realms` table is created by an EF migration written in plain SQL, and the
table has no EF model entity. An interrupted `CREATE TABLE` can leave a committed table while the
migration history still shows the old version. The migration refuses to overwrite an existing table.
Inspect that table's DDL and recover by hand instead of guessing. See
[realm registry](realm-registry.md) for provisioning, validation, and runtime grants. Accounts
version 4 binds tickets to realms (below). Adding a realm, including its Game database, is covered
in [Realm operations](realms.md).

### Version 2: signed PK points

Values above 32,767 cannot be stored as signed `SMALLINT`. Find them before migrating and correct
them from a trusted source. Do not clamp or wrap them.

```sql
SELECT `character_id`, `name`, `pk_points` FROM `characters` WHERE `pk_points` > 32767;
```

### Version 3: pre-rebirth level

Characters that have not been reborn get `0`. A reborn character's level before rebirth cannot be
derived from its current level, so the migration stops, leaving `pre_rebirth_level` as a nullable
column with the non-reborn rows filled in. A MySQL check-constraint error (3819) at this point is
expected. Fill in the missing values from a trusted source:

```sql
SELECT `character_id`, `name`, `level`, `rebirth_count`, `pre_rebirth_level`
FROM `characters`
WHERE `rebirth_count` > 0
  AND (`pre_rebirth_level` IS NULL OR `pre_rebirth_level` = 0 OR `pre_rebirth_level` > 140);

UPDATE `characters`
SET `pre_rebirth_level` = @level
WHERE `character_id` = @character_id AND `rebirth_count` > 0;
```

When the first query returns no rows, run the migration again. It makes the column required and
adds the constraints: non-reborn characters have `0`, reborn characters have a value from 1 to 140.

### Version 5: item lifetimes

Existing version 4 items have no lifetime, and the migration cannot decide whether each is
permanent, pending activation, or expiring. It therefore requires `items` to be empty. There is no
conversion path for populated version 4 data.

| `lifetime_state` | `lifetime_duration_seconds` | `lifetime_expires_at_utc` |
| ---------------- | --------------------------- | ------------------------- |
| 1 permanent      | null                        | null                      |
| 2 pending        | positive                    | null                      |
| 3 active         | null                        | set                       |

### Version 9: syndicates

The runtime Game database account needs `SELECT` on `syndicates`, `syndicate_memberships`, and
`characters` for syndicate loading at login. The integration tests grant these, but production
accounts must be granted them separately.

## After any migration

```bash
dotnet tool run dotnet-ef -- migrations has-pending-model-changes \
  --configuration Release \
  --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
  --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
  --context GameDbContext
```

Expect "No changes have been made to the model since the last migration." Then check the history
and compatibility row as above and start the server, which repeats the same checks.

## Downgrades

Downgrades remove data, so each one refuses to run if it would destroy rows:

| Downgrade | Removes                          | Refuses when                       |
| --------- | -------------------------------- | ---------------------------------- |
| 9 to 8    | Syndicate tables                 | Either table has rows              |
| 8 to 7    | `magic`                          | The table has rows                 |
| 7 to 6    | `weapon_skills`                  | The table has rows                 |
| 6 to 5    | `social_relations`               | The table has rows                 |
| 5 to 4    | Item lifetime columns            | `items` has rows                   |
| 4 to 3    | `items`                          | Never (always destructive)         |
| 3 to 2    | `pre_rebirth_level`              | Never (always destructive)         |
| 2 to 1    | Signed `pk_points`               | Any value is negative              |

Downgrading Game from v10 to v9 is blocked so that a Game database cannot lose its realm identity.

Downgrading Accounts from v3 to v2 is blocked because dropping `realms` could destroy the realm
identities the operator assigned. To go back, restore a tested backup, or write a downgrade that
keeps the data and have it reviewed separately.

A downgrade that stops partway can be resumed by running it again through EF. Before any downgrade,
read the migration's `Down` method, confirm what will be lost, and take a backup.

### Accounts version 6: account sessions

Adds `account_sessions`, one row per account naming the realm it plays in, and
`account_session_kicks`, the requests that ask a realm to disconnect an account. Both hold only live
login state, which every GameServer clears when it starts, so the migration starts with empty tables
and its downgrade drops them. Stop the AccountServer and every GameServer before migrating either
way. Grant the AccountServer `SELECT` on `account_sessions` and `INSERT, UPDATE` on
`account_session_kicks`, and each GameServer `SELECT, INSERT, UPDATE, DELETE` on both (see
[Realm operations](realms.md#grants)).

### Accounts version 5: realm status

Adds `realm_status`, one row per realm, written by that realm's GameServer and read by the
AccountServer. Rows are short-lived runtime state, so the migration starts with an empty table and
its downgrade drops the table. Stop the AccountServer and every GameServer before migrating either
way. Grant the AccountServer `SELECT` and each GameServer `SELECT, INSERT, UPDATE, DELETE` on the
table (see [Realm operations](realms.md#grants)).

### Accounts version 4: realm-bound tickets

The migration invalidates outstanding game-login tickets issued before v4 (these last five minutes),
then adds a required, positive `realm_id` field to new tickets. Stop both servers, back up Accounts,
confirm the permanent realm records are in place, and then migrate with an administrative EF
connection. Do not run old binaries against v4. If the MySQL DDL fails partway, inspect the column,
constraints, compatibility marker, and EF history before retrying. Do not guess a realm ID or
hand-edit the migration history. See `realm-bound-tickets.md`.
