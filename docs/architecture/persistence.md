# Persistence

The database holds durable data. Live game state belongs to the world runtime. The database is
read at login and written at defined commit points.

## Databases

The intended topology is one shared Accounts database and one independent Game database per realm,
all on MySQL 8.4, with no foreign keys between Accounts and Game.

| Database | Owns                                                                                     | Schema |
| -------- | ---------------------------------------------------------------------------------------- | -----: |
| Accounts | Accounts, password credentials, security state, audit events, GameServer tickets, realm registry, realm status, account sessions | 6 |
| Game     | Characters, items, friends and enemies, weapon skills, magic, syndicates and memberships; realm binding | 10 |

The GameServer currently trusts the account ID only because it came from redeeming a ticket. That
redemption is the link between the two databases. Tickets are bound to a realm, and the GameServer
verifies its configured realm identity against the single-row Game database binding before world
publication. See [multi-realm](multi-realm.md).

Both databases use the `utf8mb4` character set and `utf8mb4_0900_as_cs` collation. Character and
syndicate names use binary collation, so uniqueness is checked byte for byte. The `realms` table
also uses ASCII/binary collation, so realm names compare byte for byte with the names in the 5517
protocol.

## Schema versions

Schema changes are EF Core migrations in `OpenConquer.Infrastructure`, one chain per `DbContext`.
Each database also has a `schema_compatibility` row recording the schema version and the latest
migration ID. At startup, `AccountDatabaseReadinessVerifier` and `GameDatabaseReadinessVerifier`
check the character set, collation, version, and migration ID; the host does not open a listener if
either check fails. A failed query is reported separately from an incompatible schema.

Hosts never apply migrations. Migration is an administrative step; see
[Database migrations](../operations/database-migrations.md). CI fails if either model has changes
that no migration captures. The operator-controlled `realms` table has a SQL-only EF migration and
is not part of the mapped `AccountDbContext` model. Its owner and rollback rules are documented
separately.

## Game schema highlights

- One character per account, unique names, and character IDs starting at `1,000,000`.
- Name length 4-15, level 1-140, and nonzero appearance, profession, and map are enforced in the
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

Login reads go through repository interfaces defined in Application. Repositories use a pooled
`DbContext` factory, create one context per operation, read without change tracking, and map records
to Application types before returning. Persistence records never leave Infrastructure.

The realm registry is the one exception. Its single parameterized read uses the existing shared
`MySqlDataSource`, and it still never exposes provider-specific records to Application.

Collection reads have their own row limits, separate from gameplay limits (items default to 4,096
per character). Item reads exclude expired items in SQL before the limit is applied, using the
UTC instant captured for that login. Corrupt rows or impossible combinations fail the read instead
of being skipped.

Catalog-dependent rules, such as whether an item type exists or a stack exceeds its maximum, are
checked in Application against the item catalog rather than in the database.

## Writing

Account security changes and ticket grant, redemption, and cleanup are implemented.

Character creation also has a persistence implementation that can be tested on its own. It is not
connected to the hosted GameServer character-creation route yet.

### Character creation persistence

`CharacterCreationCandidate` holds the fully resolved initial state of a character. It validates:

- A nonzero account ID that the server trusts.
- The existing character-name policy.
- Starting body and profession values that the 5517 client supports.
- Agreement between the selected body and the appearance composite.
- The level-one allocation of ten attribute points.
- Maximum life calculated with the documented 5517 client formula.
- A nonzero starting map ID.

The server's creation policy must supply the profession-family attribute allotment, hairstyle,
starting map and position. The persistence layer does not make them up.

`CharacterCreationStore` writes one fully resolved character through a single parameterized MySQL
`INSERT`. The database generates the character ID.

The insert sets these starting values, which are server policy:

- Level 1 and zero experience.
- Fully allotted initial attributes.
- Current and historical profession fields initialized to the selected profession, following the
  previous OpenConquerPublic implementation.
- Zero rebirth state, unspent points, money, Conquer Points, bound Conquer Points, PK points, title,
  and enlightenment points.

The initial profession-history values are this server's choice. No independent source shows that
the retail server used the same defaults.

The current game schema already provides:

- Unique character account ownership.
- Unique character names.
- Generated player character identities.
- Character progression and other integrity checks.

A single-row insert is atomic, so it needs no extra application transaction, prior existence
query or distributed lock.

On a duplicate-key error, the store checks the saved account ownership and names to find out what
collided. A repeated creation attempt is never reported as newly created just because the account
now has a character. The store does not retry an insert whose outcome may be uncertain.

Cancellation is honored until the insert starts. After that, the statement is awaited without
request cancellation, and the database command timeout still limits how long it can run.

### Database privileges

Each server process has one database identity. The GameServer's may insert characters and update only
their location and fence columns; character creation (`AddGameCharacterCreationPersistence`) and the
location writer use it. The complete grants are in
[Database identities](../operations/database-identities.md).
