# Account registration

Self-service account creation for the OpenConquer client. Retail 5517 opened a registration
website from the Sign Up button; the in-client flow and its packets are a project extension. The
wire format is in the [protocol documentation](../protocol/README.md#account-registration-0xf001--0xf002-openconquer-extension).

## Flow

```text
client connects → 1059 seed → 0xF001 name + password envelope → 0xF002 result → close
```

`LoginRequestReader` classifies the first frame and returns an owning, disposable request result.
`LoginHandshakeProcessor` routes authentication to `LoginAuthenticationHandler` and registration
to `AccountRegistrationHandler`. Each handler depends only on its own operation's services.
The shared `Login` namespace denotes the seeded AccountServer transport, not an instruction to
authenticate every request.

Registration calls `AccountRegistrationService`; it never authenticates, looks up a realm, issues
a ticket, kicks a session or reads post-authentication reports. With registration disabled a
well-formed request receives `4` (unavailable). Malformed registration packets receive `1`.
The worker owns connection shutdown after the single response. The dispatcher disposes the
request on every exit; temporary password copies are also wiped before response delivery.

## Policy

| Field    | Rule |
| -------- | ---- |
| Username | 4–16 characters, printable ASCII without spaces (`!` … `~`) |
| Password | 6–15 characters: ASCII letters, digits, `@`, `.`, `>`, `-`, `_` |

The 5517 client sends account names as ANSI bytes in its active code page, so only ASCII is
accepted: a name stays identical on every client. Its password field holds 15 characters and its
keypad transform carries only letters, digits and a few symbols; because the server decodes the
registration through the same transform, the wire path produces only that alphabet. The domain
policy also checks the alphabet so direct application callers cannot create an unusable password. The
English client additionally refuses passwords over 14 characters before sending. Login keeps the
looser [authentication rules](authentication.md#credentials). The bounds are project policy, not
retail evidence.

Names are unique case-insensitively (`UX_accounts_username`, `utf8mb4_0900_ai_ci`).

## Abuse limits

`AccountRegistrationProtection` admits an attempt only when all of these hold:

| Limit | Default |
| ----- | ------: |
| Attempts per source (IPv4 address or IPv6 /64) per window | 3 |
| Window | 1 hour |
| Attempts in flight per source | 1 |
| Attempts in flight overall (bounds PBKDF2 work) | 2 |
| Sources tracked | 10,000 |

The policy check runs first, so a malformed form does not use a source's quota. Every admitted
attempt counts, including one refused because the name is taken, so names cannot be probed faster
than the quota. When the tracking table is full, new sources are refused until a window expires.
Connection admission limits still apply before any of this. An expired source with an outstanding
attempt retains its concurrency slot until completion, but does not prevent other expired sources
from being removed. These quotas are local to one AccountServer process and reset on restart;
multiple instances require source affinity or an external shared admission policy.

## Persistence

`AccountRegistrationStore` runs one transaction through a dedicated registration identity:

1. Insert the account: active, player role, state revision 1, `created_by_actor_kind = 1`
   (self-service), initial state set by the system (`3`), a fresh `creation_operation_id`.
2. Insert the password credential (current PBKDF2 format, revision 1, changed at the creation time).
3. Insert the `AccountCreated` audit event with actor self-service and the operation ID as its
   correlation ID.

A duplicate on `UX_accounts_username` rolls back and reports the name as taken. No existence query
runs first, so concurrent requests for one name cannot both succeed. Connection establishment and
all pre-commit statements share a 15-second cancellation budget linked to the caller. Cancellation
before commit rolls back the transaction, including any earlier inserts. Cancellation after
password hashing is checked before persistence starts.

Once commit begins it uses an independent 5-second budget. If its acknowledgment is lost, a separate
read with a 5-second budget checks the creation operation ID. Only the expected account ID confirms
success. A missing row, a different ID, or an unavailable read leaves the outcome unknown: the
server never retries the insert or claims the account was not created. MySQL command cancellation
and transaction disposal still have to finish before the connection and admission lease are released.

Unconfirmed persistence failures produce `3` when the connection can still accept a response.
Driver exceptions are replaced by `AccountRegistrationPersistenceException`, retaining only a
numeric database error and whether commit resolution failed. Raw messages and inner exceptions
are discarded because they can contain SQL values or connection information. If sending the failure
response also fails, both failures are retained for the worker. No registration username, password,
hash or connection string is added to diagnostics.

## Configuration

Registration is off unless `AccountServer:Registration:Enabled` is `true`, which also requires
`ConnectionStrings:AccountRegistration`. That identity must use the same Accounts database
endpoint as `ConnectionStrings:Accounts`, must not be `root` and must differ from the AccountServer
identity. Grants are in the [AccountServer operations guide](../operations/account-server.md#registration-identity).

## Verification

The registration tests cover protocol dispatch, disabled and malformed requests, isolation from
realm/session/ticket work, credential ownership, quota expiry and concurrent admission. MySQL 8.4
tests use the restricted registration identity to check atomic writes, case-insensitive duplicates,
concurrent creation, credential and audit failures, cancellation during the final insert, and
registration followed by authentication through the production repository. A TCP fault proxy drops
the commit exchange before delivery, after the server's OK reply, or before recovery can connect;
it verifies positive settlement, unknown outcomes and exactly one insert attempt.
