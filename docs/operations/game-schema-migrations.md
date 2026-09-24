# Game Schema Migration Operations

This runbook covers deployment, verification, and recovery of the OpenConquer Game database schema.

Game schema migrations are managed by EF Core through `GameDbContext`. The migration history in
`__EFMigrationsHistory` and the application-level `schema_compatibility` record must agree before
the Game schema is considered ready.

The current schema chain is:

| Version | Migration                                   |
| ------- | ------------------------------------------- |
| 1       | `20260914210346_InitialGameSchema`          |
| 2       | `20260922025854_UseSignedCharacterPkPoints` |
| 3       | `20260923223920_RetainPreRebirthLevel`      |

The current schema contract is version 3.

## Safety rules

Treat schema migration as an administrative operation.

Before applying a migration:

- stop processes that can write to the Game database;
- take a verified database backup or snapshot;
- confirm the target database before running any command;
- use an administrative database identity capable of schema changes;
- do not manually edit `__EFMigrationsHistory`;
- do not manually advance `schema_compatibility`;
- do not fabricate historical character values to make a migration pass.

A migration may intentionally fail when persisted data cannot be converted without losing semantic
information. Repair the underlying data from an authoritative source and rerun the migration.

MySQL DDL can commit independently of EF migration metadata. A failed migration can therefore leave
structural work committed while `__EFMigrationsHistory` and `schema_compatibility` still describe
the previous version. The Game migrations are written to recognize their supported partial states
and resume safely.

## Applying migrations

Restore the repository-local tools first:

    dotnet tool restore

Set the administrative Game database connection string for the target environment:

    export GAME_DB_CONNECTION='<administrative connection string>'

Apply all pending Game migrations:

    dotnet tool run dotnet-ef -- database update \
      --configuration Release \
      --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --context GameDbContext \
      --connection "$GAME_DB_CONNECTION"

To apply or resume a specific migration, provide its migration ID:

    dotnet tool run dotnet-ef -- database update 20260923223920_RetainPreRebirthLevel \
      --configuration Release \
      --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --context GameDbContext \
      --connection "$GAME_DB_CONNECTION"

Do not assume a failed command means that no schema changes committed. Inspect the database before
attempting manual recovery.

## Inspecting migration state

Check EF migration history:

    SELECT `MigrationId`
    FROM `__EFMigrationsHistory`
    ORDER BY `MigrationId`;

Check the OpenConquer compatibility marker:

    SELECT
        `component_name`,
        `schema_version`,
        `migration_id`,
        `applied_at_utc`
    FROM `schema_compatibility`
    WHERE `component_name` = 'game';

For the current schema, the compatibility row must report:

    schema_version = 3
    migration_id = 20260923223920_RetainPreRebirthLevel

Inspect the character columns involved in the resumable migrations:

    SELECT
        `COLUMN_NAME`,
        `COLUMN_TYPE`,
        `IS_NULLABLE`,
        `COLUMN_DEFAULT`,
        `EXTRA`
    FROM `INFORMATION_SCHEMA`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'characters'
      AND `COLUMN_NAME` IN
          ('pk_points', 'rebirth_count', 'pre_rebirth_level')
    ORDER BY `COLUMN_NAME`;

The current schema requires:

- `pk_points`: `smallint`, not nullable;
- `rebirth_count`: `tinyint unsigned`, not nullable;
- `pre_rebirth_level`: `tinyint unsigned`, not nullable.

Inspect the pre-rebirth constraints:

    SELECT
        `CONSTRAINT_NAME`,
        `ENFORCED`
    FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
    WHERE `CONSTRAINT_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'characters'
      AND `CONSTRAINT_TYPE` = 'CHECK'
      AND `CONSTRAINT_NAME` IN
          ('CK_characters_pre_rebirth_level',
           'CK_characters_rebirth_state')
    ORDER BY `CONSTRAINT_NAME`;

Both constraints must exist and be enforced at schema version 3.

## Signed PK points migration

Migration `20260922025854_UseSignedCharacterPkPoints` changes `characters.pk_points` from
`SMALLINT UNSIGNED` to signed `SMALLINT`.

The migration preserves values that are representable by the signed storage contract.

Before migrating a populated version 1 database, inspect values that cannot be represented:

    SELECT
        `character_id`,
        `account_id`,
        `name`,
        `pk_points`
    FROM `characters`
    WHERE `pk_points` > 32767
    ORDER BY `character_id`;

If this query returns rows, stop the migration procedure. Those values cannot be converted to signed
`SMALLINT` without changing their meaning.

Correct them only from authoritative character data. Do not clamp, wrap, reinterpret, or guess the
intended value.

### Partial signed-PK migration

Because MySQL DDL is not transactionally coupled to EF migration metadata, the `pk_points` column
may already be signed while migration history and `schema_compatibility` still report version 1.

Inspect the column:

    SELECT
        `COLUMN_TYPE`,
        `IS_NULLABLE`
    FROM `INFORMATION_SCHEMA`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'characters'
      AND `COLUMN_NAME` = 'pk_points';

A partially committed upgrade can legitimately show:

    COLUMN_TYPE = smallint
    IS_NULLABLE = NO

while the compatibility row still reports version 1.

Do not manually mark the migration complete.

Rerun:

    dotnet tool run dotnet-ef -- database update 20260922025854_UseSignedCharacterPkPoints \
      --configuration Release \
      --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --context GameDbContext \
      --connection "$GAME_DB_CONNECTION"

The migration recognizes the supported signed-column partial state, completes any remaining cleanup,
and advances both EF history and `schema_compatibility`.

After recovery, verify:

    SELECT
        `COLUMN_TYPE`,
        `IS_NULLABLE`
    FROM `INFORMATION_SCHEMA`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'characters'
      AND `COLUMN_NAME` = 'pk_points';

Expected:

    smallint
    NO

Then verify schema version 2 or later and confirm the migration appears in `__EFMigrationsHistory`.

## Pre-rebirth retained-level migration

Migration `20260923223920_RetainPreRebirthLevel` adds the persisted `pre_rebirth_level` character
value.

The semantic invariant is:

- `rebirth_count = 0` requires `pre_rebirth_level = 0`;
- `rebirth_count > 0` requires `pre_rebirth_level > 0`;
- retained pre-rebirth levels must be between `1` and `140`.

For non-reborn characters, the migration can safely populate `pre_rebirth_level = 0`.

For an already reborn character, the migration cannot derive the historical level from the current
character row. The current level is not a substitute for the retained pre-rebirth level.

The migration therefore fails closed rather than inventing history.

### Expected failure state

When version 2 contains reborn characters without authoritative retained-level data, the migration
can commit creation of a nullable `pre_rebirth_level` column before failing validation.

After the failure, this is an expected recoverable state:

- `pre_rebirth_level` exists;
- the column is `TINYINT UNSIGNED NULL`;
- non-reborn rows have been populated with `0`;
- reborn rows that need repair remain `NULL`;
- `schema_compatibility` remains at version 2;
- `20260923223920_RetainPreRebirthLevel` is absent from `__EFMigrationsHistory`;
- the final pre-rebirth constraints have not been installed.

A MySQL check-constraint violation such as error 3819 during this stage is evidence that the
migration rejected semantically incomplete data. It is not a reason to advance migration metadata
manually.

Find rows requiring operator repair:

    SELECT
        `character_id`,
        `account_id`,
        `name`,
        `level`,
        `rebirth_count`,
        `pre_rebirth_level`
    FROM `characters`
    WHERE `rebirth_count` > 0
      AND
      (
          `pre_rebirth_level` IS NULL
          OR `pre_rebirth_level` = 0
          OR `pre_rebirth_level` > 140
      )
    ORDER BY `character_id`;

For each returned character, obtain the actual retained pre-rebirth level from an authoritative
source.

Do not use the current character level as a fallback and do not assign an arbitrary nonzero value
merely to satisfy the constraint.

Repair only verified values:

    UPDATE `characters`
    SET `pre_rebirth_level` = @verified_retained_level
    WHERE `character_id` = @character_id
      AND `rebirth_count` > 0;

Verify that no unresolved rows remain:

    SELECT
        `character_id`,
        `account_id`,
        `name`,
        `rebirth_count`,
        `pre_rebirth_level`
    FROM `characters`
    WHERE `pre_rebirth_level` IS NULL
       OR `pre_rebirth_level` > 140
       OR (`rebirth_count` = 0 AND `pre_rebirth_level` <> 0)
       OR (`rebirth_count` > 0 AND `pre_rebirth_level` = 0)
    ORDER BY `character_id`;

This query must return zero rows before retrying the migration.

Rerun the same migration:

    dotnet tool run dotnet-ef -- database update 20260923223920_RetainPreRebirthLevel \
      --configuration Release \
      --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --context GameDbContext \
      --connection "$GAME_DB_CONNECTION"

The migration recognizes the partially committed nullable-column state, validates the repaired
values, makes the column required, installs the final constraints, updates `schema_compatibility`,
and allows EF to record the migration as applied.

## Verifying current schema after recovery

After any recovery, verify that no pending model changes exist in the repository:

    dotnet tool run dotnet-ef -- migrations has-pending-model-changes \
      --configuration Release \
      --no-build \
      --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --context GameDbContext

Expected:

    No changes have been made to the model since the last migration.

Verify migration history:

    SELECT `MigrationId`
    FROM `__EFMigrationsHistory`
    ORDER BY `MigrationId`;

For the current schema, the Game migration chain must contain:

    20260914210346_InitialGameSchema
    20260922025854_UseSignedCharacterPkPoints
    20260923223920_RetainPreRebirthLevel

Verify compatibility:

    SELECT
        `schema_version`,
        `migration_id`
    FROM `schema_compatibility`
    WHERE `component_name` = 'game';

Expected:

    3
    20260923223920_RetainPreRebirthLevel

The Infrastructure layer implements `GameDatabaseReadinessVerifier` for validating this contract.
The current `OpenConquer.GameServer` host is not yet wired as a runnable server and does not
currently invoke that verifier. Any future runnable GameServer composition must complete Game
database readiness verification before accepting connections.

## Downgrades

Treat Game schema downgrades as destructive operations unless the specific migration has been
reviewed for the current data.

Downgrading from version 3 removes `pre_rebirth_level`. That discards retained pre-rebirth history
from the Game database.

Downgrading the signed PK migration requires all `pk_points` values to be nonnegative before
conversion back to `SMALLINT UNSIGNED`.

Before any downgrade:

- take a verified backup;
- inspect the migration's `Down` implementation;
- verify all downgrade preconditions against production data;
- understand which semantic information will be lost;
- do not bypass migration guards or manually rewrite migration history to force the downgrade.
