# Game Schema Migration Operations

This runbook covers deployment, verification, and recovery of the OpenConquer Game database schema.

Game schema migrations are managed by EF Core through `GameDbContext`. The migration history in
`__EFMigrationsHistory` and the application-level `schema_compatibility` record must agree before
the Game schema is considered ready.

The current schema chain is:

| Version | Migration                                     |
| ------- | --------------------------------------------- |
| 1       | `20260914210346_InitialGameSchema`            |
| 2       | `20260922025854_UseSignedCharacterPkPoints`   |
| 3       | `20260923223920_RetainPreRebirthLevel`        |
| 4       | `20260927121811_AddItemPersistenceFoundation` |
| 5       | `20260927225127_AddItemLifetimePersistence`   |
| 6       | `20260930033806_AddSocialRelationPersistence` |
| 7       | `20261004014402_AddWeaponSkillPersistence`     |
| 8       | `20261004193352_AddMagicPersistence`           |

The current schema contract is version 8.

## Safety rules

Treat schema migration as an administrative operation.

Before applying a migration:

- stop all processes that can write to the Game database;
- take a verified database backup or snapshot;
- confirm the target database before running any command;
- use an administrative database identity capable of schema changes;
- do not manually edit `__EFMigrationsHistory`;
- do not manually advance `schema_compatibility`;
- do not bypass migration guards;
- do not fabricate historical character, item, social-relation, weapon-skill, or magic state to make a migration pass.

Writer quiescence is a deployment invariant. Migration guards validate the schema and persisted
state they observe, but they are not a substitute for stopping concurrent Game database writers.

A migration may intentionally fail when persisted data cannot be converted without losing semantic
information. Repair the underlying data from an authoritative source and rerun the migration.

MySQL DDL can commit independently of EF migration metadata. A failed migration can therefore leave
structural work committed while `__EFMigrationsHistory` and `schema_compatibility` still describe
the previous version. The Game migrations are written to recognize their supported partial states
and resume safely.

A partial state is supported only when its existing structure exactly matches a state the migration
knows how to resume. Unknown or incompatible structures fail closed.

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

To apply or resume the current migration specifically:

    dotnet tool run dotnet-ef -- database update 20261004193352_AddMagicPersistence \
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

    schema_version = 8
    migration_id = 20261004193352_AddMagicPersistence

Inspect the character columns involved in the resumable character migrations:

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

Both constraints must exist and be enforced at schema version 3 or later.

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

Rerun:

    dotnet tool run dotnet-ef -- database update 20260923223920_RetainPreRebirthLevel \
      --configuration Release \
      --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --context GameDbContext \
      --connection "$GAME_DB_CONNECTION"

The migration recognizes the partially committed nullable-column state, validates the repaired
values, makes the column required, installs the final constraints, updates `schema_compatibility`,
and allows EF to record the migration as applied.

## Item persistence foundation migration

Migration `20260927121811_AddItemPersistenceFoundation` advances the Game schema from version 3 to
version 4 and establishes the canonical `items` persistence table.

The migration validates the existing Game schema before creating or accepting the item table. If an
`items` table already exists because MySQL committed DDL during an interrupted attempt, the table
must exactly satisfy the migration's supported structural contract before recovery can continue.

An arbitrary table named `items` is not accepted as an already-applied migration.

### Partial item-persistence migration

A supported partial upgrade can have the complete canonical `items` table present while:

    schema_version = 3
    migration_id = 20260923223920_RetainPreRebirthLevel

and while `20260927121811_AddItemPersistenceFoundation` is still absent from
`__EFMigrationsHistory`.

Do not manually insert the migration history row.

Rerun:

    dotnet tool run dotnet-ef -- database update 20260927121811_AddItemPersistenceFoundation \
      --configuration Release \
      --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --context GameDbContext \
      --connection "$GAME_DB_CONNECTION"

The migration validates the existing table, advances `schema_compatibility` to version 4, and allows
EF to record the migration.

If the existing item table is incompatible with the expected table, columns, indexes, foreign key,
or enforced checks, the migration fails closed. Do not rename, drop, or reshape an unknown
production table solely to satisfy the migration without first establishing its provenance.

At schema version 4, compatibility is:

    schema_version = 4
    migration_id = 20260927121811_AddItemPersistenceFoundation

## Item lifetime persistence migration

Migration `20260927225127_AddItemLifetimePersistence` advances the Game schema from version 4 to
version 5.

It adds the persisted lifetime contract to `items`:

    lifetime_state              TINYINT UNSIGNED NOT NULL
    lifetime_duration_seconds   INT NULL
    lifetime_expires_at_utc     DATETIME(6) NULL

The canonical column positions are:

    lifetime_state              27
    lifetime_duration_seconds   28
    lifetime_expires_at_utc     29

It also creates:

    IX_items_lifetime_state_expires_at_utc
        (lifetime_state, lifetime_expires_at_utc)

and the enforced check:

    CK_items_lifetime

The lifetime states persisted by this schema are:

    1 = permanent
    2 = pending activation
    3 = active expiry

The database invariant is equivalent to:

    permanent:
        lifetime_state = 1
        lifetime_duration_seconds IS NULL
        lifetime_expires_at_utc IS NULL

    pending activation:
        lifetime_state = 2
        lifetime_duration_seconds IS NOT NULL
        lifetime_duration_seconds > 0
        lifetime_expires_at_utc IS NULL

    active expiry:
        lifetime_state = 3
        lifetime_duration_seconds IS NULL
        lifetime_expires_at_utc IS NOT NULL

### Existing version 4 items

Version 4 item rows contain no lifetime state.

There is no authoritative value the migration can infer for whether an existing item is permanent,
pending activation, or already using an absolute expiry.

The migration therefore refuses to invent lifetime semantics.

Before migrating a version 4 database, check:

    SELECT COUNT(*) AS `item_count`
    FROM `items`;

The result must be:

    0

If version 4 contains item rows, migration to version 5 fails closed. The existing item rows remain
unchanged, `schema_compatibility` remains at version 4, and the lifetime migration is not recorded
in `__EFMigrationsHistory`.

Do not assign all existing items an arbitrary lifetime state simply to make the migration pass.
Resolve the item lifetime values only if an authoritative source exists. This migration does not
contain a conversion path for populated version 4 item data.

### Partial lifetime migration

MySQL can commit some lifetime DDL before EF records the migration.

The migration supports recovery from known partial states, including:

- one or more canonical lifetime columns already present;
- the canonical lifetime index already present;
- the canonical lifetime check already present;
- the complete version 5 structure and compatibility marker present while EF history still lacks the
  migration.

Existing lifetime columns must match the expected data type, nullability, default, and canonical
ordinal position.

An existing `IX_items_lifetime_state_expires_at_utc` must have the expected two-column index shape.

An existing `CK_items_lifetime` is not trusted merely because its name matches. Its enforced
`CHECK_CLAUSE` must express the expected lifetime invariant. A weaker, different, or otherwise
incompatible same-name check causes the migration to fail closed.

Inspect lifetime columns:

    SELECT
        `COLUMN_NAME`,
        `ORDINAL_POSITION`,
        `COLUMN_TYPE`,
        `IS_NULLABLE`,
        `COLUMN_DEFAULT`
    FROM `INFORMATION_SCHEMA`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'items'
      AND `COLUMN_NAME` IN
          ('lifetime_state',
           'lifetime_duration_seconds',
           'lifetime_expires_at_utc')
    ORDER BY `ORDINAL_POSITION`;

Inspect the lifetime index:

    SELECT
        `INDEX_NAME`,
        `SEQ_IN_INDEX`,
        `COLUMN_NAME`,
        `NON_UNIQUE`
    FROM `INFORMATION_SCHEMA`.`STATISTICS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'items'
      AND `INDEX_NAME` = 'IX_items_lifetime_state_expires_at_utc'
    ORDER BY `SEQ_IN_INDEX`;

Inspect the actual lifetime check definition:

    SELECT
        `tc`.`CONSTRAINT_NAME`,
        `tc`.`ENFORCED`,
        `cc`.`CHECK_CLAUSE`
    FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS` AS `tc`
    INNER JOIN `INFORMATION_SCHEMA`.`CHECK_CONSTRAINTS` AS `cc`
        ON `cc`.`CONSTRAINT_SCHEMA` = `tc`.`CONSTRAINT_SCHEMA`
        AND `cc`.`CONSTRAINT_NAME` = `tc`.`CONSTRAINT_NAME`
    WHERE `tc`.`CONSTRAINT_SCHEMA` = DATABASE()
      AND `tc`.`TABLE_NAME` = 'items'
      AND `tc`.`CONSTRAINT_NAME` = 'CK_items_lifetime';

Do not manually replace a conflicting same-name check or rearrange columns until the unexpected
state has been investigated.

For a known supported partial state with zero item rows, rerun:

    dotnet tool run dotnet-ef -- database update 20260927225127_AddItemLifetimePersistence \
      --configuration Release \
      --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --context GameDbContext \
      --connection "$GAME_DB_CONNECTION"

The migration validates the existing partial structure, creates only the missing canonical
structures, verifies the complete version 5 contract, advances `schema_compatibility`, and allows EF
to record the migration.

At schema version 5, compatibility is:

    schema_version = 5
    migration_id = 20260927225127_AddItemLifetimePersistence

## Social relation persistence migration

Migration `20260930033806_AddSocialRelationPersistence` advances the Game schema from version 5 to
version 6 and establishes the canonical `social_relations` table.

The table stores directed social relationships:

    owner_character_id         INT UNSIGNED NOT NULL
    counterpart_character_id   INT UNSIGNED NOT NULL
    kind                       TINYINT UNSIGNED NOT NULL

The canonical primary key is:

    PK_social_relations
        (owner_character_id, kind, counterpart_character_id)

The reverse-lookup index is:

    IX_social_relations_counterpart_kind_owner
        (counterpart_character_id, kind, owner_character_id)

Both character identifiers reference `characters.character_id` with `ON DELETE RESTRICT` and
`ON UPDATE RESTRICT`.

The persisted relation kinds are:

    1 = friend
    2 = enemy

A row is directed from `owner_character_id` to `counterpart_character_id`.

Operationally, a reciprocal friendship is represented by two directed friend rows. An enemy
relationship is one-sided unless a separate reverse row also exists. The schema permits the same
owner/counterpart pair to have both a friend and an enemy row because `kind` is part of the primary
key.

The enforced checks require:

- owner character IDs to be in the player-character entity range;
- counterpart character IDs to be in the player-character entity range;
- owner and counterpart to be different characters;
- `kind` to be either `1` or `2`.

No counterpart name, online state, peerage state, packet action, or gameplay friend-count limit is
stored in `social_relations`.

### Partial social-relation migration

MySQL may commit creation of `social_relations` before EF records the migration.

A supported partial upgrade can therefore have the complete canonical table present while:

    schema_version = 5
    migration_id = 20260927225127_AddItemLifetimePersistence

and while `20260930033806_AddSocialRelationPersistence` is absent from `__EFMigrationsHistory`.

The migration also supports the state where the complete canonical table and version 6
`schema_compatibility` marker are present while EF history still lacks the migration.

An existing table named `social_relations` is not accepted merely because its name matches. The
migration validates the exact canonical:

- table collation;
- column count, order, types, nullability, defaults, and extra metadata;
- primary-key column order;
- reverse-index column order;
- both character foreign keys and their update/delete rules;
- all four enforced check constraints and their expected definitions.

An unknown or incompatible existing table causes the migration to fail closed.

Inspect the table:

    SELECT
        `TABLE_NAME`,
        `TABLE_COLLATION`
    FROM `INFORMATION_SCHEMA`.`TABLES`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'social_relations'
      AND `TABLE_TYPE` = 'BASE TABLE';

Inspect the columns:

    SELECT
        `COLUMN_NAME`,
        `ORDINAL_POSITION`,
        `COLUMN_TYPE`,
        `IS_NULLABLE`,
        `COLUMN_DEFAULT`,
        `EXTRA`
    FROM `INFORMATION_SCHEMA`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'social_relations'
    ORDER BY `ORDINAL_POSITION`;

Expected:

    owner_character_id         1   int unsigned       NO   NULL
    counterpart_character_id   2   int unsigned       NO   NULL
    kind                       3   tinyint unsigned   NO   NULL

Inspect the indexes:

    SELECT
        `INDEX_NAME`,
        `SEQ_IN_INDEX`,
        `COLUMN_NAME`,
        `NON_UNIQUE`
    FROM `INFORMATION_SCHEMA`.`STATISTICS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'social_relations'
      AND `INDEX_NAME` IN
          ('PRIMARY',
           'IX_social_relations_counterpart_kind_owner')
    ORDER BY `INDEX_NAME`, `SEQ_IN_INDEX`;

Expected index shapes:

    PRIMARY:
        1   owner_character_id
        2   kind
        3   counterpart_character_id

    IX_social_relations_counterpart_kind_owner:
        1   counterpart_character_id
        2   kind
        3   owner_character_id

Inspect foreign keys:

    SELECT
        `kcu`.`CONSTRAINT_NAME`,
        `kcu`.`COLUMN_NAME`,
        `kcu`.`REFERENCED_TABLE_NAME`,
        `kcu`.`REFERENCED_COLUMN_NAME`,
        `rc`.`DELETE_RULE`,
        `rc`.`UPDATE_RULE`
    FROM `INFORMATION_SCHEMA`.`KEY_COLUMN_USAGE` AS `kcu`
    INNER JOIN `INFORMATION_SCHEMA`.`REFERENTIAL_CONSTRAINTS` AS `rc`
        ON `rc`.`CONSTRAINT_SCHEMA` = `kcu`.`CONSTRAINT_SCHEMA`
        AND `rc`.`CONSTRAINT_NAME` = `kcu`.`CONSTRAINT_NAME`
    WHERE `kcu`.`CONSTRAINT_SCHEMA` = DATABASE()
      AND `kcu`.`TABLE_NAME` = 'social_relations'
      AND `kcu`.`REFERENCED_TABLE_NAME` IS NOT NULL
    ORDER BY `kcu`.`CONSTRAINT_NAME`;

Both relationships must reference `characters.character_id` and report:

    DELETE_RULE = RESTRICT
    UPDATE_RULE = RESTRICT

Inspect the enforced checks:

    SELECT
        `tc`.`CONSTRAINT_NAME`,
        `tc`.`ENFORCED`,
        `cc`.`CHECK_CLAUSE`
    FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS` AS `tc`
    INNER JOIN `INFORMATION_SCHEMA`.`CHECK_CONSTRAINTS` AS `cc`
        ON `cc`.`CONSTRAINT_SCHEMA` = `tc`.`CONSTRAINT_SCHEMA`
        AND `cc`.`CONSTRAINT_NAME` = `tc`.`CONSTRAINT_NAME`
    WHERE `tc`.`CONSTRAINT_SCHEMA` = DATABASE()
      AND `tc`.`TABLE_NAME` = 'social_relations'
      AND `tc`.`CONSTRAINT_TYPE` = 'CHECK'
    ORDER BY `tc`.`CONSTRAINT_NAME`;

The enforced checks must be:

    CK_social_relations_owner_character_id
    CK_social_relations_counterpart_character_id
    CK_social_relations_distinct_characters
    CK_social_relations_kind

For a known supported partial state, rerun:

    dotnet tool run dotnet-ef -- database update 20260930033806_AddSocialRelationPersistence \
      --configuration Release \
      --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --context GameDbContext \
      --connection "$GAME_DB_CONNECTION"

The migration validates the existing canonical structure, advances `schema_compatibility` to version
6 when necessary, and allows EF to record the migration.

At schema version 6, compatibility is:

    schema_version = 6
    migration_id = 20260930033806_AddSocialRelationPersistence

## Weapon skill persistence migration

Migration `20261004014402_AddWeaponSkillPersistence` advances the Game schema from version 6 to
version 7 and establishes the canonical `weapon_skills` table.

The table stores one weapon proficiency per character and skill type:

    owner_character_id   INT UNSIGNED NOT NULL
    weapon_skill_type    INT UNSIGNED NOT NULL
    level                TINYINT UNSIGNED NOT NULL
    experience           INT UNSIGNED NOT NULL

The canonical primary key is:

    PK_weapon_skills
        (owner_character_id, weapon_skill_type)

`owner_character_id` references `characters.character_id` with `ON DELETE RESTRICT` and
`ON UPDATE RESTRICT`.

The enforced checks require:

- owner character IDs to be in the player-character entity range;
- weapon-skill levels to be between `0` and `20`, inclusive.

`weapon_skill_type` is intentionally stored as an opaque unsigned value. The schema does not impose
a fabricated numeric range or require three-digit types. Historical 5517 data includes four-digit
types such as `1050`.

The table does not persist legacy surrogate IDs, `old_level`, or `unlearn`. Those values are not
part of the current login-hydration contract and must not be invented without gameplay semantics
that require them.

### Partial weapon-skill migration

MySQL may commit creation of `weapon_skills` before EF records the migration.

A supported partial upgrade can therefore have the complete canonical table present while:

    schema_version = 6
    migration_id = 20260930033806_AddSocialRelationPersistence

and while `20261004014402_AddWeaponSkillPersistence` is absent from `__EFMigrationsHistory`.

The migration also supports the complete version 7 structure and compatibility marker being present
while EF history still lacks the migration.

An existing table named `weapon_skills` is accepted only when its complete structure matches the
canonical contract. The migration validates:

- table collation;
- all four columns, including order, type, nullability, default, and extra metadata;
- the exact two-column primary key;
- the character foreign key and its update/delete rules;
- both enforced check constraints and their definitions.

Unexpected indexes, foreign keys, checks, columns, or other incompatible structure cause the
migration to fail closed.

Inspect the columns:

    SELECT
        `COLUMN_NAME`,
        `ORDINAL_POSITION`,
        `COLUMN_TYPE`,
        `IS_NULLABLE`,
        `COLUMN_DEFAULT`,
        `EXTRA`
    FROM `INFORMATION_SCHEMA`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'weapon_skills'
    ORDER BY `ORDINAL_POSITION`;

Expected:

    owner_character_id   1   int unsigned       NO   NULL
    weapon_skill_type    2   int unsigned       NO   NULL
    level                3   tinyint unsigned   NO   NULL
    experience           4   int unsigned       NO   NULL

For a known supported partial state, rerun:

    dotnet tool run dotnet-ef -- database update 20261004014402_AddWeaponSkillPersistence \
      --configuration Release \
      --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --context GameDbContext \
      --connection "$GAME_DB_CONNECTION"

The migration validates the existing canonical structure, advances `schema_compatibility` to version
7 when necessary, and allows EF to record the migration.

At schema version 7, compatibility is:

    schema_version = 7
    migration_id = 20261004014402_AddWeaponSkillPersistence

## Magic persistence migration

Migration `20261004193352_AddMagicPersistence` advances the Game schema from version 7 to version 8
and establishes the canonical `magic` table.

The table stores one magic entry per character and magic type:

    owner_character_id   INT UNSIGNED NOT NULL
    magic_type           SMALLINT UNSIGNED NOT NULL
    level                SMALLINT UNSIGNED NOT NULL
    experience           INT UNSIGNED NOT NULL

The canonical primary key is:

    PK_magic
        (owner_character_id, magic_type)

`owner_character_id` references `characters.character_id` with `ON DELETE RESTRICT` and
`ON UPDATE RESTRICT`.

The only enforced magic-specific check requires owner character IDs to be in the player-character
entity range.

`magic_type` and `level` intentionally preserve their full unsigned 16-bit storage ranges.
`experience` preserves its full unsigned 32-bit range. The schema does not fabricate a universal
magic-type range, universal maximum magic level, nonzero-type rule, or relationship between
experience and a next-level requirement.

Historical 5517 `cq_magic` storage also carried a surrogate ID, `unlearn`, and `old_level`. Those
fields are not part of the verified login-hydration contract and are intentionally not persisted by
this model.

### Partial magic migration

MySQL may commit creation of `magic` before EF records the migration.

A supported partial upgrade can therefore have the complete canonical table present while:

    schema_version = 7
    migration_id = 20261004014402_AddWeaponSkillPersistence

and while `20261004193352_AddMagicPersistence` is absent from `__EFMigrationsHistory`.

The migration also supports the complete version 8 structure and compatibility marker being present
while EF history still lacks the migration.

An existing table named `magic` is accepted only when its complete structure matches the canonical
contract. The migration validates:

- table collation;
- all four columns, including order, type, nullability, default, and extra metadata;
- the exact two-column primary key;
- the character foreign key and its update/delete rules;
- the single enforced owner-character check constraint and its definition.

Unexpected indexes, foreign keys, checks, columns, or other incompatible structure cause the
migration to fail closed.

Inspect the columns:

    SELECT
        `COLUMN_NAME`,
        `ORDINAL_POSITION`,
        `COLUMN_TYPE`,
        `IS_NULLABLE`,
        `COLUMN_DEFAULT`,
        `EXTRA`
    FROM `INFORMATION_SCHEMA`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'magic'
    ORDER BY `ORDINAL_POSITION`;

Expected:

    owner_character_id   1   int unsigned        NO   NULL
    magic_type           2   smallint unsigned   NO   NULL
    level                3   smallint unsigned   NO   NULL
    experience           4   int unsigned        NO   NULL

For a known supported partial state, rerun:

    dotnet tool run dotnet-ef -- database update 20261004193352_AddMagicPersistence \
      --configuration Release \
      --project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --startup-project src/OpenConquer.Infrastructure/OpenConquer.Infrastructure.csproj \
      --context GameDbContext \
      --connection "$GAME_DB_CONNECTION"

The migration validates the existing canonical structure, advances `schema_compatibility` to version
8 when necessary, and allows EF to record the migration.

At schema version 8, compatibility is:

    schema_version = 8
    migration_id = 20261004193352_AddMagicPersistence

## Verifying current schema after recovery

After any recovery, verify that no pending model changes exist in the repository:

    dotnet tool run dotnet-ef -- migrations has-pending-model-changes \
      --configuration Release \
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
    20260927121811_AddItemPersistenceFoundation
    20260927225127_AddItemLifetimePersistence
    20260930033806_AddSocialRelationPersistence
    20261004014402_AddWeaponSkillPersistence
    20261004193352_AddMagicPersistence

Verify compatibility:

    SELECT
        `schema_version`,
        `migration_id`
    FROM `schema_compatibility`
    WHERE `component_name` = 'game';

Expected:

    8
    20261004193352_AddMagicPersistence

Verify the lifetime columns:

    SELECT
        `COLUMN_NAME`,
        `ORDINAL_POSITION`,
        `COLUMN_TYPE`,
        `IS_NULLABLE`,
        `COLUMN_DEFAULT`
    FROM `INFORMATION_SCHEMA`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'items'
      AND `COLUMN_NAME` IN
          ('lifetime_state',
           'lifetime_duration_seconds',
           'lifetime_expires_at_utc')
    ORDER BY `ORDINAL_POSITION`;

Expected:

    lifetime_state              27   tinyint unsigned   NO    NULL
    lifetime_duration_seconds   28   int                YES   NULL
    lifetime_expires_at_utc     29   datetime(6)        YES   NULL

Verify the lifetime index:

    SELECT
        `SEQ_IN_INDEX`,
        `COLUMN_NAME`,
        `NON_UNIQUE`
    FROM `INFORMATION_SCHEMA`.`STATISTICS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'items'
      AND `INDEX_NAME` = 'IX_items_lifetime_state_expires_at_utc'
    ORDER BY `SEQ_IN_INDEX`;

Expected:

    1   lifetime_state            1
    2   lifetime_expires_at_utc   1

Verify the lifetime check exists and is enforced:

    SELECT
        `tc`.`CONSTRAINT_NAME`,
        `tc`.`ENFORCED`,
        `cc`.`CHECK_CLAUSE`
    FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS` AS `tc`
    INNER JOIN `INFORMATION_SCHEMA`.`CHECK_CONSTRAINTS` AS `cc`
        ON `cc`.`CONSTRAINT_SCHEMA` = `tc`.`CONSTRAINT_SCHEMA`
        AND `cc`.`CONSTRAINT_NAME` = `tc`.`CONSTRAINT_NAME`
    WHERE `tc`.`CONSTRAINT_SCHEMA` = DATABASE()
      AND `tc`.`TABLE_NAME` = 'items'
      AND `tc`.`CONSTRAINT_TYPE` = 'CHECK'
      AND `tc`.`CONSTRAINT_NAME` = 'CK_items_lifetime';

Exactly one enforced `CK_items_lifetime` must exist and its clause must represent the canonical
lifetime invariant described above.

Verify the social-relation table:

    SELECT
        `COLUMN_NAME`,
        `ORDINAL_POSITION`,
        `COLUMN_TYPE`,
        `IS_NULLABLE`,
        `COLUMN_DEFAULT`
    FROM `INFORMATION_SCHEMA`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'social_relations'
    ORDER BY `ORDINAL_POSITION`;

Expected:

    owner_character_id         1   int unsigned       NO   NULL
    counterpart_character_id   2   int unsigned       NO   NULL
    kind                       3   tinyint unsigned   NO   NULL

Verify the social-relation indexes:

    SELECT
        `INDEX_NAME`,
        `SEQ_IN_INDEX`,
        `COLUMN_NAME`,
        `NON_UNIQUE`
    FROM `INFORMATION_SCHEMA`.`STATISTICS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'social_relations'
      AND `INDEX_NAME` IN
          ('PRIMARY',
           'IX_social_relations_counterpart_kind_owner')
    ORDER BY `INDEX_NAME`, `SEQ_IN_INDEX`;

Verify both social-relation foreign keys are `RESTRICT`:

    SELECT
        `kcu`.`CONSTRAINT_NAME`,
        `kcu`.`COLUMN_NAME`,
        `rc`.`DELETE_RULE`,
        `rc`.`UPDATE_RULE`
    FROM `INFORMATION_SCHEMA`.`KEY_COLUMN_USAGE` AS `kcu`
    INNER JOIN `INFORMATION_SCHEMA`.`REFERENTIAL_CONSTRAINTS` AS `rc`
        ON `rc`.`CONSTRAINT_SCHEMA` = `kcu`.`CONSTRAINT_SCHEMA`
        AND `rc`.`CONSTRAINT_NAME` = `kcu`.`CONSTRAINT_NAME`
    WHERE `kcu`.`CONSTRAINT_SCHEMA` = DATABASE()
      AND `kcu`.`TABLE_NAME` = 'social_relations'
      AND `kcu`.`REFERENCED_TABLE_NAME` = 'characters'
    ORDER BY `kcu`.`CONSTRAINT_NAME`;

Verify all social-relation checks exist and are enforced:

    SELECT
        `CONSTRAINT_NAME`,
        `ENFORCED`
    FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
    WHERE `CONSTRAINT_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'social_relations'
      AND `CONSTRAINT_TYPE` = 'CHECK'
    ORDER BY `CONSTRAINT_NAME`;

Verify the weapon-skill table:

    SELECT
        `COLUMN_NAME`,
        `ORDINAL_POSITION`,
        `COLUMN_TYPE`,
        `IS_NULLABLE`,
        `COLUMN_DEFAULT`
    FROM `INFORMATION_SCHEMA`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'weapon_skills'
    ORDER BY `ORDINAL_POSITION`;

Expected:

    owner_character_id   1   int unsigned       NO   NULL
    weapon_skill_type    2   int unsigned       NO   NULL
    level                3   tinyint unsigned   NO   NULL
    experience           4   int unsigned       NO   NULL

Verify its primary key, character foreign key, and enforced checks through
`INFORMATION_SCHEMA.STATISTICS`, `KEY_COLUMN_USAGE`, `REFERENTIAL_CONSTRAINTS`, and
`TABLE_CONSTRAINTS`. The primary key must be `(owner_character_id, weapon_skill_type)`, the foreign
key must use `RESTRICT` for updates and deletes, and exactly
`CK_weapon_skills_owner_character_id` and `CK_weapon_skills_level` must be enforced.

Verify the magic table:

    SELECT
        `COLUMN_NAME`,
        `ORDINAL_POSITION`,
        `COLUMN_TYPE`,
        `IS_NULLABLE`,
        `COLUMN_DEFAULT`
    FROM `INFORMATION_SCHEMA`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'magic'
    ORDER BY `ORDINAL_POSITION`;

Expected:

    owner_character_id   1   int unsigned        NO   NULL
    magic_type           2   smallint unsigned   NO   NULL
    level                3   smallint unsigned   NO   NULL
    experience           4   int unsigned        NO   NULL

Verify the primary key is `(owner_character_id, magic_type)`, the character foreign key uses
`RESTRICT` for updates and deletes, and exactly `CK_magic_owner_character_id` is enforced. No
additional magic check constraints or indexes are part of the canonical version 8 contract.

The Infrastructure layer implements `GameDatabaseReadinessVerifier` for validating the schema
compatibility contract. The current `OpenConquer.GameServer` host is not yet wired as a runnable
server and does not currently invoke that verifier. Any future runnable GameServer composition must
complete Game database readiness verification before accepting connections.

## Downgrades

Treat Game schema downgrades as destructive operations unless the specific migration has been
reviewed for the current data.

Downgrading version 8 to version 7 removes the `magic` table. Because this would destroy persisted
magic progression, the migration requires `magic` to contain zero rows before dropping the table.
If any rows exist, the downgrade fails closed and leaves the version 8 schema and compatibility
marker intact.

A partially committed version 8 downgrade can be resumed when `magic` has already been removed but
migration metadata still reports version 8. Keep all Game database writers stopped and rerun the
downgrade through EF rather than manually editing migration metadata.

Downgrading version 7 to version 6 removes the `weapon_skills` table. Because this would destroy
persisted weapon proficiencies, the migration requires `weapon_skills` to contain zero rows before
dropping the table. If any rows exist, the downgrade fails closed and leaves the version 7 schema
and compatibility marker intact.

A partially committed version 7 downgrade can be resumed when `weapon_skills` has already been
removed but migration metadata still reports version 7. Keep all Game database writers stopped and
rerun the downgrade through EF rather than manually editing migration metadata.

Downgrading version 6 to version 5 removes the `social_relations` table. Because this would destroy
persisted social relationships, the migration requires `social_relations` to contain zero rows
before dropping the table. If any social-relation rows exist, the downgrade fails closed and leaves
the version 6 schema and compatibility marker intact.

A partially committed version 6 downgrade can be resumed when `social_relations` has already been
removed but migration metadata still reports version 6. Keep all Game database writers stopped and
rerun the downgrade through EF rather than manually editing migration metadata.

Downgrading version 5 to version 4 removes the persisted lifetime columns, lifetime index, and
lifetime check. Because doing so would discard lifetime semantics, the migration requires the
`items` table to contain zero rows before performing the destructive downgrade. If item rows exist,
the downgrade fails closed and leaves version 5 intact.

A partially committed version 5 downgrade can be resumed when the expected lifetime structures have
already been removed but migration metadata still reports version 5. Keep all Game database writers
stopped and rerun the downgrade through EF rather than manually editing metadata.

Downgrading version 4 to version 3 removes the `items` table. This destroys all persisted item data.

Downgrading version 3 removes `pre_rebirth_level`. That discards retained pre-rebirth history from
the Game database.

Downgrading the signed PK migration requires all `pk_points` values to be nonnegative before
conversion back to `SMALLINT UNSIGNED`.

Before any downgrade, take a verified backup, inspect the migration's `Down` implementation, verify
all downgrade preconditions against production data, understand which semantic information will be
lost, and do not bypass migration guards or manually rewrite migration history to force the
downgrade.
