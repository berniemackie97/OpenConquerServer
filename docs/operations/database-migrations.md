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

## Game v11 location revision deployment

The v11 migration adds `characters.location_revision` with zero as its initial value for
existing characters. Stop **all** gameplay, administrative, maintenance and custom
writers to `characters` before migration and keep them stopped until all are running v11
code. Back up and verify restore. Re-provision the existing dedicated location-writer
principal's column-scoped `SELECT`/`UPDATE` grant with `location_revision` (see
`docs/architecture/durable-character-location.md`); do not grant it table-wide writes.
The migration is intentionally not reversible. A stored revision does not prove the
result of a still-running SQL command; no live transfer coordinator exists yet.

## Game v12 commit evidence deployment

The v12 offline migration adds nullable `characters.last_location_operation_id BINARY(16)`.
Existing records retain `NULL`; the mandatory operation ID is written atomically with a
successful location revision CAS. Stop **all** writers to `characters` and perform a
verified backup before applying the migration. Update only the opt-in location writer's
column-scoped `SELECT` and `UPDATE` grants to include `last_location_operation_id`.
Deploy v12 components only after migration and grant reconciliation; older writers must
not continue writing character locations. The migration cannot be downgraded safely.

The marker is positive evidence of one completed CAS; an absent or different marker
cannot prove an uncertain in-flight SQL command will never complete. Production transfers
remain disabled. See [durable character location](../architecture/durable-character-location.md).

## Before you start

- Stop every process that writes to the database. Migration checks validate what they see; they
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
resumes from them; a structure it does not recognize, including a same-named table or constraint
with a different definition, makes it stop rather than guess.

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

Each Game database has a one-row operator-provisioned `realm_binding` table. The
migration does **not** insert or infer an identity. The GameServer refuses startup
without a matching `GameServer:RealmId`, and the runtime requires `SELECT` only.
See [game realm binding](game-realm-binding.md). Downgrading v10 to v9 is blocked
because it would discard the persistent world identity.

### Accounts version 3: realm registry

The operator-owned `realms` table uses a SQL-only EF migration and deliberately has no EF model
entity. An interrupted `CREATE TABLE` may leave a committed table with old migration history.
The migration refuses to overwrite any existing table; inspect its exact DDL and recover under
operator control rather than silently guessing. See [realm registry](realm-registry.md) for
provisioning, validation and runtime grants. Accounts version 4 binds tickets to realms (below).
Adding a realm, including its Game database, is covered in [Realm operations](realms.md).

### Version 2: signed PK points

Values above 32,767 cannot be stored as signed `SMALLINT`. Find them before migrating and correct
them from a trusted source; do not clamp or wrap them.

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
`characters` for syndicate loading at login. The integration tests grant these; production
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

| From → to | Removes                          | Refuses when                       |
| --------- | -------------------------------- | ---------------------------------- |
| 9 → 8     | Syndicate tables                 | Either table has rows              |
| 8 → 7     | `magic`                          | The table has rows                 |
| 7 → 6     | `weapon_skills`                  | The table has rows                 |
| 6 → 5     | `social_relations`               | The table has rows                 |
| 5 → 4     | Item lifetime columns            | `items` has rows                   |
| 4 → 3     | `items`                          | Always destructive; no guard       |
| 3 → 2     | `pre_rebirth_level`              | Always destructive; no guard       |
| 2 → 1     | Signed `pk_points`               | Any value is negative              |

Game v10 → v9 is deliberately blocked: a Game database cannot silently lose its realm identity.

Accounts v3 → v2 is deliberately blocked: dropping `realms` could destroy operator identities.
Restore a verified backup or write an independently reviewed, data-preserving downgrade.

A downgrade that stops partway can be resumed by running it again through EF. Before any downgrade,
read the migration's `Down` method, confirm what will be lost, and take a backup.

### Accounts version 6: account sessions

Adds `account_sessions`, one row per account naming the realm it plays in, and
`account_session_kicks`, the requests that ask a realm to disconnect an account. Both hold only live
login state, which every GameServer clears when it starts, so the migration starts with empty tables
and its downgrade drops them. Stop the AccountServer and every GameServer before migrating either way.
Grant the AccountServer `SELECT` on `account_sessions` and `INSERT, UPDATE` on
`account_session_kicks`, and each GameServer `SELECT, INSERT, UPDATE, DELETE` on both; see
[Realm operations](realms.md#grants).

### Accounts version 5: realm status

Adds `realm_status`, one row per realm, written by that realm's GameServer and read by the
AccountServer. Rows are short-lived runtime state, so the migration starts with an empty table and
its downgrade drops the table. Stop the AccountServer and every GameServer before migrating either
way. Grant the AccountServer `SELECT` and each GameServer `SELECT, INSERT, UPDATE, DELETE` on the
table; see [Realm operations](realms.md#grants).

### Accounts version 4: realm-bound tickets

The migration invalidates outstanding pre-v4 five-minute game-login tickets, then adds a
required, positive `realm_id` field to new tickets. **Stop both servers, back up Accounts,
confirm the permanent realm records, then migrate with an administrative EF connection.**
Do not run old binaries against v4. In case of a partial MySQL DDL failure, inspect the
column, constraints, compatibility marker and EF history before retrying; do not guess
a realm identity or hand-edit migration history. See `realm-bound-tickets.md`.
