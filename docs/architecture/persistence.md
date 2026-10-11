# Persistence

The database is durable storage, not live game state. The world runtime owns live state; the
database is read at login and, in future, written at defined commit points.

## Databases

There are two MySQL 8.4 databases with no foreign keys between them.

| Database | Owns                                                                                     | Schema |
| -------- | ---------------------------------------------------------------------------------------- | -----: |
| Accounts | Accounts, password credentials, security state, audit events, GameServer tickets          |      2 |
| Game     | Characters, items, friends and enemies, weapon skills, magic, syndicates and memberships |      9 |

The GameServer trusts the account ID only because it came from redeeming a ticket. That
redemption is the link between the two databases.

Both databases use the `utf8mb4` character set and `utf8mb4_0900_as_cs` collation. Character and
syndicate names use binary collation so uniqueness is exact.

## Schema contracts

Schema changes are EF Core migrations in `OpenConquer.Infrastructure`, one chain per `DbContext`.
Each database also has a `schema_compatibility` row recording the schema version and the latest
migration ID. At startup, `AccountDatabaseReadinessVerifier` and `GameDatabaseReadinessVerifier`
check the character set, collation, version, and migration ID; the host does not open a listener if
either check fails. A failed query is reported separately from an incompatible schema.

Hosts never apply migrations. Migration is an administrative step; see
[Database migrations](../operations/database-migrations.md). CI fails if either model has changes
that no migration captures.

## Game schema highlights

- One character per account, unique names, and character IDs starting at `1,000,000`.
- Name length 4–15, level 1–140, and nonzero appearance, profession, and map are enforced in the
  database as well as in code.
- Items are owned by a character and placed in the inventory or one of two equipment sets. Each
  item has a lifetime state: permanent, pending activation, or active with an expiry time.
- Friend and enemy relations are directed rows; a mutual friendship is two rows.
- Syndicate population is derived from memberships, and leader names come from `characters`,
  so neither is stored twice.

Columns whose meaning is not yet known are stored unchanged so the client sees what it expects.
Values that cannot be converted without guessing cause a migration to fail instead of being
invented.

## Reading

Login reads go through repository contracts defined in Application. Repositories use a pooled
`DbContext` factory, create one context per operation, read without change tracking, and map
records to Application types before returning; persistence records never leave Infrastructure.

Collection reads have explicit row limits that are separate from gameplay limits (items default to
4,096 per character). Item reads exclude expired items in SQL before the limit is applied, using
the UTC instant captured for that login. Corrupt rows or impossible combinations fail the read
instead of being skipped.

Catalog-dependent rules, such as whether an item type exists or a stack exceeds its maximum, are
checked in Application against the item catalog rather than in the database.

## Writing

Current writes are the account security changes and the ticket grant, redemption, and cleanup
described in [Authentication](authentication.md). All of them follow one rule: before commit,
cancellation rolls back; once the commit starts it cannot be cancelled; success is reported only
after the commit succeeds; and an uncertain commit is never retried blindly.

Character creation and gameplay writes do not exist yet. When they are added, a world operation
that needs durability will record a pending state, submit the write, and finish when the result
comes back as a command, so database latency never holds a world partition. See
[World execution](world-execution.md#durable-operations).
