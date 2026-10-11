# Database migrations

Both databases are migrated with EF Core: `AccountDbContext` for Accounts and `GameDbContext` for
Game. Each database also has a `schema_compatibility` row (component `accounts` or `game`) recording
the schema version and latest migration ID. The servers check that row at startup and refuse to
start on a mismatch. They never migrate on their own.

| Database | Version | Migration                                     |
| -------- | ------: | --------------------------------------------- |
| Accounts |       1 | `20260906043138_InitialAccountSchema`         |
| Accounts |       2 | `20260907004603_ProtectGameLoginTicketAuthenticationKey` |
| Game     |       1 | `20260914210346_InitialGameSchema`            |
| Game     |       2 | `20260922025854_UseSignedCharacterPkPoints`   |
| Game     |       3 | `20260923223920_RetainPreRebirthLevel`        |
| Game     |       4 | `20260927121811_AddItemPersistenceFoundation` |
| Game     |       5 | `20260927225127_AddItemLifetimePersistence`   |
| Game     |       6 | `20260930033806_AddSocialRelationPersistence` |
| Game     |       7 | `20261004014402_AddWeaponSkillPersistence`    |
| Game     |       8 | `20261004193352_AddMagicPersistence`          |
| Game     |       9 | `20261008033456_AddSyndicatePersistence`      |

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

A downgrade that stops partway can be resumed by running it again through EF. Before any downgrade,
read the migration's `Down` method, confirm what will be lost, and take a backup.
