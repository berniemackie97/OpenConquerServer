# Persistence

The database is durable storage, not live game state. The world runtime owns live state; the
database is read at login and written at defined commit points.

## Databases

The intended topology is one shared Accounts database and one independent Game database per realm,
all on MySQL 8.4, with no foreign keys between Accounts and Game.

| Database | Owns                                                                                     | Schema |
| -------- | ---------------------------------------------------------------------------------------- | -----: |
| Accounts | Accounts, password credentials, security state, audit events, GameServer tickets, realm registry | 3 |
| Game     | Characters, items, friends and enemies, weapon skills, magic, syndicates and memberships; realm binding | 10 |

The GameServer currently trusts the account ID only because it came from redeeming a ticket. That
redemption is the link between the two databases. Tickets are bound to a realm, and the GameServer
verifies its configured realm identity against the single-row Game database binding before world
publication. See [multi-realm](multi-realm.md).

Both databases use the `utf8mb4` character set and `utf8mb4_0900_as_cs` collation. Character and
syndicate names use binary collation so uniqueness is exact. The `realms` table additionally uses
ASCII/binary collation for exact native protocol-name identity.

## Schema contracts

Schema changes are EF Core migrations in `OpenConquer.Infrastructure`, one chain per `DbContext`.
Each database also has a `schema_compatibility` row recording the schema version and the latest
migration ID. At startup, `AccountDatabaseReadinessVerifier` and `GameDatabaseReadinessVerifier`
check the character set, collation, version, and migration ID; the host does not open a listener if
either check fails. A failed query is reported separately from an incompatible schema.

Hosts never apply migrations. Migration is an administrative step; see
[Database migrations](../operations/database-migrations.md). CI fails if either model has changes
that no migration captures. The operator-controlled `realms` table has a SQL-only EF migration and
is intentionally not part of the mapped `AccountDbContext` model; its owner and rollback rules are
documented separately.

## Game schema highlights

- One character per account, unique names, and character IDs starting at `1,000,000`.
- Name length 4–15, level 1–140, and nonzero appearance, profession, and map are enforced in the
  database as well as in code.
- Items are owned by a character and placed in the inventory or one of two equipment sets. Each item
  has a lifetime state: permanent, pending activation, or active with an expiry time.
- Friend and enemy relations are directed rows; a mutual friendship is two rows.
- Syndicate population is derived from memberships, and leader names come from `characters`, so
  neither is stored twice.

Columns whose meaning is not yet known are stored unchanged so the client sees what it expects.
Values that cannot be converted without guessing cause a migration to fail instead of being
invented.

## Reading

Login reads go through repository contracts defined in Application. Repositories use a pooled
`DbContext` factory, create one context per operation, read without change tracking, and map records
to Application types before returning; persistence records never leave Infrastructure.

The realm registry is the intentional exception: its single parameterized read uses the existing
shared `MySqlDataSource`, and never exposes provider-specific records to Application.

Collection reads have explicit row limits that are separate from gameplay limits (items default to
4,096 per character). Item reads exclude expired items in SQL before the limit is applied, using the
UTC instant captured for that login. Corrupt rows or impossible combinations fail the read instead
of being skipped.

Catalog-dependent rules, such as whether an item type exists or a stack exceeds its maximum, are
checked in Application against the item catalog rather than in the database.

## Writing

Account security changes and ticket grant, redemption, and cleanup are implemented.

Character creation also has an independently testable persistence implementation. It is not yet
connected to the hosted GameServer character-creation route.

### Character creation persistence

`CharacterCreationCandidate` represents fully resolved initial character state.

It validates:

- Nonzero authoritative account identity.
- The existing character-name policy.
- Native-supported starting body and profession values.
- Agreement between selected body and appearance composite.
- The level-one allocation of ten attribute points.
- Maximum life calculated using the documented native client formula.
- Nonzero starting map identity.

The exact profession-family allotment, hairstyle, starting map and position must be supplied by the
server's authoritative creation policy. The persistence layer does not manufacture them.

`CharacterCreationStore` writes one fully resolved character through a single parameterized MySQL
`INSERT`. The database generates the character ID.

The insertion establishes these server starting-state policies:

- Level 1 and zero experience.
- Fully allotted initial attributes.
- Current and historical profession fields initialized to the selected profession, following the
  previous OpenConquerPublic implementation.
- Zero rebirth state, unspent points, money, Conquer Points, bound Conquer Points, PK points, title,
  and enlightenment points.

The initial profession-history values are a server implementation policy, not an independently
proven retail-server default.

The current game schema already provides:

- Unique character account ownership.
- Unique character names.
- Generated player character identities.
- Character progression and other integrity checks.

A single-row insertion is atomic. It does not require an additional application transaction,
preliminary existence query, or distributed lock.

Duplicate-key errors are reconciled against persisted account ownership and names.

A repeated creation attempt is never reported as newly created merely because the account now
contains a character.

The store does not automatically retry an insertion whose outcome may be uncertain.

Cancellation is honored before insertion. Once insertion begins, the statement is awaited without
request cancellation. Database command timeouts remain bounded.

### Database privileges

Existing GameServer runtime credentials are read-only for character persistence.

Character creation has a separate, opt-in persistence registration:

`AddGameCharacterCreationPersistence`

The required database permissions are:

```sql
GRANT SELECT, INSERT
ON `openconquer_game`.`characters`
TO 'openconquer_game_creation'@'%';
```

The AccountServer runtime identity also needs `SELECT` on `realms` and `realm_status`, but no DML
privilege for either. Each GameServer's Accounts identity needs `SELECT, INSERT, UPDATE, DELETE` on
`realm_status` to report its own realm.
