# Account Authentication

This document defines authentication and game-login security invariants.

General architecture is documented in [Architecture](README.md). Network/session behavior is
documented in [Networking](networking.md). Wire behavior is documented in the
[Protocol Reference](../protocol/README.md).

## Credential Contract

| Field    | Contract                                                                             |
| -------- | ------------------------------------------------------------------------------------ |
| Username | Trim surrounding whitespace; 1–32 characters; preserve case and internal whitespace  |
| Password | 1–128 characters; never trim or normalize; never retain caller-owned password memory |

Invalid credential shape returns `InvalidCredentials` before persistence or password derivation.

Native packet limits are separate from application limits:

| Field              | Wire limit |
| ------------------ | ---------: |
| Account            |  128 bytes |
| Credential         |  128 bytes |
| Transformed secret |   32 bytes |

The native keypad transform uses the original account bytes before application username trimming.

## Authentication

`AccountAuthenticator` returns:

```text
Success
InvalidCredentials
Banned
```

Successful authentication includes:

- account ID;
- canonical username;
- account-state revision;
- password-credential revision.

The revisions prevent a stale authentication result from receiving a game-login ticket after an
authentication-invalidating state change.

Authentication alone does not authorize GameServer access.

## Authentication Protection

Default production limits:

| Control                                       |   Default |
| --------------------------------------------- | --------: |
| Requests per source                           | 30/minute |
| Concurrent requests per source                |         4 |
| Global concurrent requests                    |        32 |
| Concurrent password attempts per account      |         2 |
| Failed attempts before source/account lockout |         8 |
| Failure window                                | 5 minutes |
| Lockout                                       | 5 minutes |
| Tracked-state limit                           |   100,000 |

Additional invariants:

- IPv4-mapped IPv6 normalizes to IPv4;
- native IPv6 is grouped by `/64`;
- protection uses monotonic `TimeProvider` time;
- tracked state is bounded and fails closed under unrecoverable capacity pressure;
- account misses perform decoy password verification;
- password verification occurs before banned status is exposed.

## Password Storage

Supported formats:

| Format                            | Purpose   | KDF                                    |
| --------------------------------- | --------- | -------------------------------------- |
| `$openconquer$pbkdf2-sha256$v=1$` | Current   | PBKDF2-HMAC-SHA256, 600,000 iterations |
| `$openconquer$identity-v3$`       | Migration | PBKDF2-HMAC-SHA512, 220,000 iterations |

Both use a 16-byte salt and 32-byte derived key.

Unsupported or malformed password records fail closed.

A successful Identity V3 login may migrate to the current format using compare-and-swap.

Transparent migration:

```text
password credential Revision++
password_changed_at_utc unchanged
outstanding game-login tickets preserved
```

Explicit password reset changes the secret and revokes outstanding tickets.

## Game-Login Ticket Grant

`GameLoginTicketIssuer` creates a durable five-minute ticket after successful authentication.

Client-visible ticket credentials remain:

```text
SessionUid
AuthenticationKey
```

Both values are nonzero.

MySQL is authoritative for ticket issue and expiration timestamps.

Ticket grant transaction lock order:

```text
accounts
    ↓
account_password_credentials
    ↓
game_login_tickets
```

Grant requires the authenticated account identity and both authentication revisions to still match
durable state.

Therefore:

```text
security mutation commits first
    -> stale authentication cannot receive a ticket

ticket grant commits first
    -> later invalidating mutation revokes the ticket
```

A successful AccountServer `1055` is not sent until the ticket grant commits.

See [Networking](networking.md) for the complete AccountServer transaction.

## Authentication-Key Protection

The raw 32-bit `AuthenticationKey` is required by the native client but is not stored directly.

Persistence stores:

- `SessionUid`;
- account identity;
- issue/expiration timestamps;
- HMAC-SHA256 authentication-key verifier;
- verifier key ID.

The verifier binds `SessionUid` and `AuthenticationKey`.

Verification uses fixed-time comparison.

Verification keys are externally supplied server secrets. Historical keys may remain configured
during rotation. Unknown key IDs fail closed.

AccountServer configuration supplies the active verification-key ID and encoded verification-key set
during startup.

The encoded configuration is decoded when the authentication infrastructure is composed. The
resulting key ring owns the decoded key bytes and clears them when the service provider is disposed.

Cross-host secret deployment and key-rotation orchestration remain deployment responsibilities and
are not yet implemented.

## Security Mutations

Supported durable account-security operations include:

- access-status changes;
- authority-role changes;
- soft delete and restore;
- explicit password reset.

Account `StateRevision` advances for access-status, authority-role, deletion, and restoration
changes.

Password credential `Revision` advances for explicit password reset and transparent password
migration.

Outstanding game-login tickets are revoked when a committed mutation invalidates previously
authenticated credentials:

| Mutation                       | Revoke tickets |
| ------------------------------ | -------------- |
| Explicit password reset        | Yes            |
| Active → Suspended             | Yes            |
| Active → Banned                | Yes            |
| Suspended → Banned             | Yes            |
| Soft delete                    | Yes            |
| Reactivation / unban           | No             |
| Restore                        | No             |
| Authority-role change          | No             |
| Transparent password migration | No             |

Mutation and ticket-grant persistence use compatible lock ordering.

## Ticket Redemption

`GameLoginTicketRedeemer` requires:

- nonzero `SessionUid`;
- nonzero `AuthenticationKey`;
- remote source address for attempt protection.

Redemption locks the ticket, checks database-authoritative expiration, verifies the authentication
key, deletes the exact ticket, and commits before authorization succeeds.

| Ticket state | Result                    |
| ------------ | ------------------------- |
| Missing      | Deny                      |
| Expired      | Deny; leave for cleanup   |
| Wrong key    | Deny; preserve ticket     |
| Valid        | Delete, commit, authorize |

Concurrent valid redemption attempts serialize on the ticket row. At most one succeeds.

The authorized identity contains account ID, canonical username, and `SessionUid`; the raw
authentication key is not retained.

GameServer network integration is not yet implemented.

## Redemption Protection

Default production limits:

| Control                                |   Default |
| -------------------------------------- | --------: |
| Attempts per source                    | 30/minute |
| Concurrent attempts per source         |         4 |
| Concurrent attempts per `SessionUid`   |         1 |
| Failed attempts before session lockout |         8 |
| Failure window                         | 5 minutes |
| Lockout                                | 5 minutes |
| Global concurrent attempts             |       512 |
| Tracked-state limit                    |   100,000 |

Protection uses monotonic time and bounded state.

## Expired-Ticket Cleanup

Cleanup is bounded maintenance; it does not determine authorization.

Infrastructure owns expiration eligibility and bounded deletion:

| Property                   |   Default |
| -------------------------- | --------: |
| Expiration grace           | 5 minutes |
| Maximum rows per batch     |     1,000 |
| Maximum configurable batch |    10,000 |

Eligibility uses MySQL time.

AccountServer owns scheduling and the maximum number of batches processed per maintenance run.

Cleanup runs immediately when its hosted service starts and then on the configured interval.

A partial batch ends the current run early. The configured maximum batch count prevents an
accumulated cleanup backlog from monopolizing the process.

Transient cleanup failures are logged and retried on the next scheduled run.

A violated cleaner invariant faults the maintenance background service so the host can terminate
rather than continue in an unknown state.

## Durable Operation Rule

Ticket grant, account-security mutation, and ticket redemption follow the same durability rule:

```text
before commit
    -> cancellation may rollback

commit phase entered
    -> commit is non-cancelable

success
    -> returned only after commit succeeds
```

Ambiguous commit outcomes are not blindly retried.

## Production Login Composition

`AddAccountLoginInfrastructure` composes the AccountServer authentication graph:

- account persistence;
- authentication protection;
- password hashing;
- `AccountAuthenticator`;
- game-login ticket grant persistence;
- cryptographic ticket token generation;
- `GameLoginTicketIssuer`;
- container-owned verification-key ring.

The host supplies:

- account database connection string;
- `AccountAuthenticationProtectionOptions`;
- active verification-key ID;
- encoded verification-key set.

`TimeProvider.System` is used only when the host has not supplied another `TimeProvider`.

This infrastructure boundary does not own:

- database readiness execution;
- expired-ticket cleanup scheduling;
- listener or admission lifecycle;
- login workers;
- host observability.

Those responsibilities belong to `OpenConquer.AccountServer`.

## AccountServer Host Security Boundary

The runnable AccountServer host fails closed during startup.

```text
load strict deployment configuration
    ↓
construct authentication infrastructure
    ↓
verify database/schema readiness
    ↓
start bounded maintenance
    ↓
create login listener
    ↓
accept authentication traffic
```

Invalid configuration, invalid verification-key configuration, or failed database readiness prevents
the login listener from being exposed.

Login ingress remains bounded independently at transport admission, worker concurrency, and
authentication-protection boundaries.

Fatal listener or worker failure terminates the supervised login runtime and requests host shutdown.

## Current Status

Implemented:

- runnable AccountServer Generic Host composition;
- readiness-gated AccountServer startup;
- bounded admission and fixed login-worker lifecycle;
- AccountServer runtime observability;
- AccountServer authentication transaction through durable `1055`;
- authentication protection and password migration;
- durable ticket grant and revocation;
- single-use ticket redemption;
- bounded redemption protection;
- scheduled bounded expired-ticket cleanup;
- production AccountServer authentication/game-login dependency composition.

Not yet implemented:

- verification-key deployment and rotation orchestration;
- least-privilege production database identities;
- authenticated self-service password changes;
- staff/admin mutation authorization;
- GameServer network/session integration;
- GameServer handshake and `1052` proof;
- GameServer host composition.
