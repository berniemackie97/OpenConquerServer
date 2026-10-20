# Authentication

A player authenticates with the AccountServer, which issues a short-lived, single-use ticket. The
GameServer redeems that ticket to establish who the connection belongs to. This document covers
the rules behind each step. The connection flow is in [Networking](networking.md) and the packet
layouts are in the [protocol documentation](../protocol/README.md).

## Credentials

| Field    | Rule                                                                                  |
| -------- | ------------------------------------------------------------------------------------- |
| Username | Surrounding whitespace trimmed, 1 to 32 characters, case and inner whitespace kept    |
| Password | 1 to 128 characters, never trimmed or normalized, not kept after verification         |

Credentials that break these rules fail as `InvalidCredentials` before any database or password
work. The wire fields are larger (128 bytes each). The client's password keypad transform is keyed
on the raw account bytes, so it runs before the username is trimmed.

`AccountAuthenticator` returns `Success`, `InvalidCredentials`, or `Banned`. A success carries the
account ID, the stored username, and the current account-state and password revisions. The
revisions let the ticket grant detect that the account changed after authentication.

Unknown accounts still run a decoy password check, and the password is checked before a ban is
revealed, so response timing and content do not disclose which accounts exist.

## Rate limiting

| Limit                                         |   Default |
| --------------------------------------------- | --------: |
| Requests per source                           | 30/minute |
| Concurrent requests per source                |         4 |
| Concurrent requests overall                   |        32 |
| Concurrent password checks per account        |         2 |
| Failures per account and source before lockout |         8 |
| Failure window / lockout                      | 5 minutes |
| Tracked entries                               |   100,000 |

Sources are normalized the same way as for connection admission (IPv6 grouped by `/64`). Limits use
monotonic time. If the tracking table is full and cannot be pruned, new requests are refused.

## Password storage

| Format                            | Use       | Algorithm                               |
| --------------------------------- | --------- | --------------------------------------- |
| `$openconquer$pbkdf2-sha256$v=1$` | Current   | PBKDF2-HMAC-SHA256, 600,000 iterations  |
| `$openconquer$identity-v3$`       | Migration | PBKDF2-HMAC-SHA512, 220,000 iterations  |

Both use a 16-byte salt and a 32-byte key. Malformed or unknown records are rejected. A successful
login with an Identity V3 hash rewrites it in the current format using a compare-and-swap update.
That migration increments the password revision but keeps the password-changed time and does not
revoke existing tickets. A password reset does revoke them.

## GameServer tickets

The AccountServer checks the realm name from packet 1060 against the realm registry before it
verifies the password. After the password checks out, it reads the registry again. The second read
refuses the login if the realm was removed or disabled or the name now belongs to a different realm
ID, and it picks up the latest endpoint. A successful packet 1055 carries that registration's IPv4
address and port. A refused selection gets one of the realm-availability codes listed in
[Multi-realm](multi-realm.md#availability-replies). The ticket grant then checks on its own, under a
shared database lock, that the same realm ID is still enabled before it commits the ticket, which is
bound to that realm. Routing does not create realms or check GameServer health. Operators still
deploy each realm.

After a successful login, `GameLoginTicketIssuer` creates a ticket that is valid for five minutes.
The client receives a session ID and an authentication key, both nonzero. The ticket grant locks
`accounts`, then `account_password_credentials`, checks the selected enabled `realms` row under a
shared lock, and then inserts `game_login_tickets`. It succeeds only if the account and password
revisions still match the authentication result. Combined with ticket revocation below, this means:

- If a security change commits first, the stale login cannot receive a ticket.
- If the ticket commits first, the later security change revokes it.

The database clock decides issue and expiry times. The authentication key is not stored. The
database keeps an HMAC-SHA256 of the session ID and key together with the ID of the server key
that produced it. Verification uses a fixed-time comparison. Several server keys can be configured
at once so keys can be rotated. An unknown key ID is rejected.

### Revocation

Security changes revoke outstanding tickets in the same transaction:

| Change                                      | Revokes tickets |
| ------------------------------------------- | --------------- |
| Password reset                              | Yes             |
| Suspension, ban, or soft delete             | Yes             |
| Reactivation, unban, or restore             | No              |
| Authority role change                       | No              |
| Password hash migration                     | No              |

### Redemption

`GameLoginTicketRedeemer` locks the ticket row and requires the ticket's realm to match this
GameServer's configured RealmId, which is checked against the Game database. It then checks expiry
against the database clock, verifies the key, deletes the ticket, and commits before reporting
success. Concurrent redemptions of one ticket serialize on the row, so at most one succeeds. A wrong
key leaves the ticket in place. An expired ticket is left for cleanup. The result carries the
account ID, username, and session ID.

| Redemption limit                        |   Default |
| --------------------------------------- | --------: |
| Attempts per source                     | 30/minute |
| Concurrent attempts per source          |         4 |
| Concurrent attempts per session ID      |         1 |
| Failures per session ID before lockout  |         8 |
| Failure window / lockout                | 5 minutes |
| Concurrent attempts overall             |       512 |
| Tracked entries                         |   100,000 |

### Cleanup

The AccountServer deletes expired tickets on a schedule, starting immediately and then at the
configured interval. Each run deletes in batches of up to 1,000 rows (configurable up to 10,000)
after a five-minute grace period, stops at the first partial batch, and processes at most a
configured number of batches. Transient failures are retried on the next run. Cleanup never
decides whether a ticket is valid. Redemption does that.

## Commit rule

Ticket grants, security changes, and redemptions follow the same rule. Cancellation before the
commit rolls back. Once the commit starts, it cannot be cancelled. Success is reported only after
the commit succeeds. Future writes such as character creation must define the same cancellation
point before they are added.

## Service registration

| Extension                                     | Registers                                                        |
| --------------------------------------------- | ---------------------------------------------------------------- |
| `AddAccountLoginInfrastructure`               | Account persistence, rate limiting, password hashing, authenticator, ticket issuance, key ring |
| `AddGameLoginTicketRedemptionInfrastructure`  | Account persistence, redemption limits, key ring, ticket redeemer |

Both live in `OpenConquer.Infrastructure.DependencyInjection`. The key ring decodes the configured
keys once and clears them when the service provider is disposed. Both use `TimeProvider.System`
unless the host registers another. Listeners, workers, readiness checks, and cleanup scheduling
belong to the hosts.
