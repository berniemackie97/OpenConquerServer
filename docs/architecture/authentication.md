# Account Authentication

Authentication, AccountServer handoff, game-login authorization, account security mutations, and
ticket redemption are separate boundaries.

```text
1060 credentials
    ↓
AccountAuthenticator
    ↓
authenticated account snapshot
    ↓
GameLoginTicketIssuer
    ↓
durable ticket grant
    ↓
AccountServer 1055 handoff
    ↓
GameServer 1052 proof
    ↓
GameLoginTicketRedeemer
    ↓
authorized game-login identity
```

The AccountServer side through durable `1055` handoff is implemented.

The GameServer `1052` proof and game-session boundary are not yet implemented.

## Ownership

| Layer          | Responsibility                                                                           |
| -------------- | ---------------------------------------------------------------------------------------- |
| Domain         | Credential and account-state rules                                                       |
| Application    | Authentication, ticket issuance/redemption, account mutation orchestration               |
| Infrastructure | Password verification, abuse protection, MySQL persistence, transactional security state |
| Protocol       | 5517 login packets and cryptography                                                      |
| AccountServer  | Standard 5517 authentication transaction                                                 |
| GameServer     | Future ticket redemption and game-session composition                                    |

## Credential Contract

Username:

- trim surrounding whitespace before account lookup;
- require 1–32 .NET characters;
- preserve case and internal whitespace;
- do not lowercase or otherwise normalize.

Password:

- require 1–128 characters;
- never trim or normalize;
- empty/default memory is invalid;
- caller retains ownership of supplied password memory;
- authentication does not retain password memory.

Invalid credential shape returns `InvalidCredentials` before persistence or password derivation.

The standard 5517 packet has different wire limits:

```text
account field       128 bytes
credential field    128 bytes
transformed secret   32 bytes
```

The original account bytes must be used for the native keypad transform before application-level
username trimming.

## Authentication Result

`AccountAuthenticator` returns:

```text
Success
InvalidCredentials
Banned
```

Successful authentication carries:

- account ID;
- canonical persisted username;
- account state revision;
- password credential revision.

These revisions are required by ticket grant to reject stale authentication snapshots.

Authentication alone does not authorize a GameServer connection.

## Authentication Protection

Authentication work is bounded before expensive persistence and password verification.

Pre-resolution controls:

- global concurrent authentication limit;
- per-source request rate;
- per-source concurrency;
- bounded tracked protection state.

Resolved-account controls:

- per-account concurrent password attempts;
- per-account/source failure window;
- per-account/source lockout.

Default policy:

| Control                                     |   Default |
| ------------------------------------------- | --------: |
| Requests per source                         | 30/minute |
| Concurrent requests per source              |         4 |
| Concurrent authentication requests globally |        32 |
| Concurrent password attempts per account    |         2 |
| Failed attempts before lockout              |         8 |
| Failure window                              | 5 minutes |
| Lockout duration                            | 5 minutes |

There is no global failed-attempt lockout keyed only by account ID.

IPv4-mapped IPv6 addresses normalize to IPv4. Native IPv6 addresses are grouped by `/64`.

Protection durations use monotonic `TimeProvider` time.

Tracked state is bounded and fails closed when active state cannot be safely reclaimed.

Account misses perform decoy password verification after request admission.

The password is verified before exposing banned status.

## Password Formats

Current format:

```text
$openconquer$pbkdf2-sha256$v=1$
```

Parameters:

| Property    | Value              |
| ----------- | ------------------ |
| KDF         | PBKDF2-HMAC-SHA256 |
| Iterations  | 600,000            |
| Salt        | 16 random bytes    |
| Derived key | 32 bytes           |

Supported migration format:

```text
$openconquer$identity-v3$
```

Verified legacy profile:

| Property          | Value           |
| ----------------- | --------------- |
| Identity marker   | 1               |
| PRF               | 2 / HMAC-SHA512 |
| Iterations        | 220,000         |
| Salt              | 16 bytes        |
| Subkey            | 32 bytes        |
| Metadata integers | Big-endian      |

Unsupported password records fail closed.

This includes:

- unprefixed values;
- Identity V2;
- unsupported Identity V3 profiles;
- malformed records;
- extended records outside the verified profile.

Legacy plaintext/custom-salt password handling is not supported.

## Password Migration

A valid legacy hash may be transparently migrated after successful authentication.

Migration uses compare-and-swap against the authenticated persistence snapshot.

If the replacement loses a race:

1. re-read the account once;
2. revalidate password and account state;
3. accept only if the resulting state still authenticates;
4. do not attempt a second migration.

Successful transparent migration:

```text
password credential Revision++
password_changed_at_utc unchanged
outstanding game-login tickets preserved
```

Transparent migration changes the stored representation, not the user's secret.

## Authentication Persistence

`AccountAuthenticationRepository` returns an authentication snapshot containing:

- account ID;
- canonical username;
- password hash;
- effective login access;
- account state revision;
- password credential revision.

Successful-login persistence is conditional on the exact authenticated snapshot.

Relevant state changes therefore prevent stale authentication results from being silently persisted
or migrated.

## Game-Login Ticket Grant

`GameLoginTicketIssuer` accepts only successful authentication results.

A ticket contains:

- account ID;
- canonical username;
- nonzero `SessionUid`;
- nonzero `AuthenticationKey`;
- issue time;
- expiration time.

Ticket lifetime:

```text
5 minutes
```

Application owns the duration.

MySQL owns the durable issue and expiration timestamps.

`SessionUid` collisions are retried with a fresh session UID and authentication key. Allocation is
bounded.

Persistent collision exhaustion fails.

## Atomic Ticket Grant

`GameLoginTicketGrantStore` uses an explicit MySQL `READ COMMITTED` transaction.

Lock order:

```text
accounts
    ↓
account_password_credentials
    ↓
game_login_tickets
```

Grant requires:

- matching account ID;
- exact canonical username;
- active account;
- account not deleted;
- exact account state revision;
- exact password credential revision.

Only then may the ticket be inserted.

This closes the authentication-to-ticket race:

```text
security mutation commits first
    -> authentication snapshot becomes stale
    -> grant denied

grant commits first
    -> later invalidating mutation revokes the ticket
```

## Ticket Time Authority

MySQL establishes durable ticket time:

```text
issued_at_utc  = UTC_TIMESTAMP(6)
expires_at_utc = issued_at_utc + 5 minutes
```

The AccountServer process clock is not authoritative for ticket issue or expiration.

The persisted timestamps are returned as part of the final granted ticket.

## Authentication-Key Protection

The native client requires the raw 32-bit `AuthenticationKey`.

The database does not persist that bearer secret directly.

Stored ticket credential data includes:

- `SessionUid`;
- account identity;
- canonical username;
- issue/expiration timestamps;
- HMAC-SHA256 authentication-key verifier;
- verifier key ID.

The verifier binds:

```text
SessionUid || AuthenticationKey
```

Authentication-key comparison uses fixed-time comparison.

Verification keys are externally supplied server secrets.

Historical verification keys may remain available during controlled rotation.

Unknown verifier key IDs fail closed.

## AccountServer Handoff

The standard 5517 AccountServer authentication transaction is implemented:

```text
1059 seed
    ↓
1060 credentials
    ↓
AccountAuthenticator
    ↓
GameLoginTicketIssuer
    ↓
durable MySQL grant
    ↓
1055 success
```

A successful `1055` is not emitted until durable ticket grant completes.

Successful `1055` uses:

```text
SessionUid             = ticket.SessionUid
AuthenticationKey      = ticket.AuthenticationKey
GameServerPort         = configured port
AdditionalSessionField = ticket.AuthenticationKey
GameServerIp           = configured IPv4 address
```

Ticket grant returning a stale-authentication result maps to generic native credential failure `1`.

Post-success `1100` and AccountServer `1052` reports are telemetry and do not revoke the issued
ticket.

See [Protocol Reference](../protocol/README.md).

## Account Security Mutations

`AccountMutationService` supports trusted operational mutation orchestration.

Implemented durable operations:

- access-status change;
- authority-role change;
- soft delete;
- restore;
- explicit password reset.

Current mutation actors:

```text
System
Migration
```

Authenticated self-service mutation and staff authorization are not yet implemented.

Mutation outcomes:

```text
Applied
AccountNotFound
StateConflict
InvalidState
NoChange
```

Expected revisions are concurrency contracts.

Deleted accounts cannot be changed through normal status, role, or password-reset operations.

Restore is the explicit lifecycle operation.

## Revision Semantics

Account `StateRevision` advances for:

- access-status change;
- authority-role change;
- deletion;
- restoration.

Password credential `Revision` advances for:

- explicit password reset;
- transparent password rehash.

Important distinction:

```text
explicit password reset
    -> credential Revision++
    -> outstanding tickets revoked

transparent password rehash
    -> credential Revision++
    -> outstanding tickets preserved
```

Explicit password reset does not increment `StateRevision`.

## Ticket Revocation

Invariant:

> An issued bearer credential must not survive a newly committed authentication-invalidating
> mutation.

Current revocation matrix:

| Mutation                       | Revoke outstanding tickets |
| ------------------------------ | -------------------------- |
| Explicit password reset/change | Yes                        |
| Active → Suspended             | Yes                        |
| Active → Banned                | Yes                        |
| Suspended → Banned             | Yes                        |
| Soft delete                    | Yes                        |
| Suspended → Active             | No                         |
| Banned → Active                | No                         |
| Banned → Suspended             | No                         |
| Restore                        | No                         |
| Authority-role change          | No                         |
| Transparent password rehash    | No                         |

`Banned → Suspended` is an unban event and does not revoke tickets.

Revocation follows mutation semantics, not destination status alone.

## Mutation Audit Events

Implemented event kinds:

```text
AccountCreated
AccountSuspended
AccountReactivated
AccountBanned
AccountUnbanned
AccountDeleted
AccountRestored
PasswordChanged
PasswordMigrated
AuthorityRoleChanged
```

Audit data includes:

- target account;
- event kind;
- database-authoritative timestamp;
- actor;
- optional reason code;
- correlation ID;
- previous/new access status where applicable;
- previous/new authority role where applicable.

Audit insertion occurs in the same transaction as the mutation and required ticket revocation.

## Atomic Mutation Persistence

`AccountMutationStore` uses explicit MySQL `READ COMMITTED` transactions.

Lock order:

```text
accounts
    ↓
account_password_credentials   when required
    ↓
game_login_tickets             when revocation is required
    ↓
account_audit_events INSERT
```

The account row is the serialization point.

This order matches ticket grant and avoids account/ticket lock inversion.

Mutation time is read from:

```text
UTC_TIMESTAMP(6)
```

after required locks are acquired.

The same database timestamp is used for state mutation and audit data.

## Mutation / Redemption Concurrency

Ticket redemption does not lock the account row.

Mutation and redemption linearize through the ticket row:

```text
redemption wins
    -> ticket consumed
    -> mutation commits afterward

mutation wins
    -> ticket revoked
    -> redemption cannot authorize
```

Redemption never acquires ticket → account locks, avoiding reverse lock ordering.

## Game-Login Ticket Redemption

`GameLoginTicketRedeemer` validates:

- nonzero `SessionUid`;
- nonzero `AuthenticationKey`;
- remote source address for attempt protection.

Successful redemption returns:

- account ID;
- canonical username;
- `SessionUid`.

The raw authentication key is not retained in the authorized identity.

GameServer integration for this boundary is not yet implemented.

## Redemption Protection

Default production limits:

| Control                                |   Default |
| -------------------------------------- | --------: |
| Attempts per source                    | 30/minute |
| Concurrent attempts per source         |         4 |
| Concurrent attempts per `SessionUid`   |         1 |
| Failed attempts before session lockout |         8 |
| Failure window                         | 5 minutes |
| Lockout duration                       | 5 minutes |
| Global concurrent attempts             |       512 |
| Tracked state limit                    |   100,000 |

In-flight attempts count toward the session failure budget.

Protection uses monotonic time.

Tracked state is bounded and fails closed under unrecoverable capacity pressure.

## Atomic Redemption

`GameLoginTicketRedemptionStore` uses an explicit `READ COMMITTED` transaction.

Flow:

```text
SELECT ticket FOR UPDATE
    ↓
SELECT UTC_TIMESTAMP(6)
    ↓
check expiration
    ↓
verify AuthenticationKey
    ↓
DELETE exact unexpired ticket
    ↓
COMMIT
    ↓
authorized identity
```

The ticket row is the durable authorization grant.

Redemption does not re-query account or password tables.

Missing ticket:

```text
no authorization
```

Expired ticket:

```text
no authorization
ticket left for cleanup
```

Invalid authentication key:

```text
no authorization
ticket preserved
```

Valid ticket:

```text
delete exact ticket
commit
return identity
```

Concurrent valid redemption attempts serialize on the ticket row. At most one can authorize.

A fresh database-time condition is also applied during deletion to prevent authorization if
expiration crosses between validation and consumption.

## Expired-Ticket Cleanup

`GameLoginTicketExpirationCleaner` performs bounded physical cleanup.

Logical expiration remains owned by redemption.

Default cleanup policy:

| Property                    |   Default |
| --------------------------- | --------: |
| Expiration grace            | 5 minutes |
| Maximum rows per invocation |     1,000 |
| Maximum configurable batch  |    10,000 |
| Maximum grace               |     1 day |

Eligibility:

```text
expires_at_utc <= UTC_TIMESTAMP(6) - expiration_grace
```

Deletion order:

```text
expires_at_utc
session_uid
```

Cleanup performs one bounded database batch per invocation.

Scheduling belongs to future host composition.

## Cancellation and Durable Failure

Grant, mutation, and redemption operations follow the same durable rule:

```text
before commit phase
    -> cancellation may rollback

commit phase entered
    -> commit uses non-cancelable token

success
    -> returned only after commit succeeds
```

Ambiguous durable commit failures propagate.

Grant, security mutation, and destructive redemption are not blindly retried after an unknown commit
result.

Expiration cleanup is different: it is idempotent maintenance and can safely continue in a later
invocation.

## Persistence Composition

Account persistence uses one canonical MySQL connection policy:

```text
UseAffectedRows=false
GuidFormat=Binary16
DateTimeKind=Utc
```

Explicit MySqlConnector transaction stores additionally use:

```text
AutoEnlist=false
```

EF Core and explicit transaction stores share the same account database while retaining separate
transaction ownership.

## Current Boundary

Implemented:

- account credential validation;
- bounded authentication protection;
- current PBKDF2 password storage;
- verified Identity V3 migration;
- authentication-state revisions;
- durable five-minute GameServer tickets;
- MySQL-authoritative ticket time;
- protected ticket credential persistence;
- AccountServer `1055` durable handoff;
- transactional account security mutations;
- transactional ticket revocation;
- single-use ticket redemption;
- bounded redemption protection;
- bounded expired-ticket cleanup.

Not yet implemented:

- production AccountServer host composition;
- authenticated self-service password change;
- staff/admin mutation authorization;
- production least-privilege database identities;
- verification-key deployment/rotation orchestration;
- GameServer handshake;
- GameServer `1052` login proof;
- GameServer host composition.

## Compatibility Boundary

Native 5517 GameServer login ultimately uses:

```text
SessionUid
AuthenticationKey
```

Those externally visible credentials are preserved.

Internal rewrite behavior does not need to preserve legacy implementation weaknesses such as:

- raw bearer-key persistence;
- legacy transaction ownership;
- legacy socket/threading structure;
- redundant internal identifiers.

Native client-visible behavior remains the compatibility authority.
