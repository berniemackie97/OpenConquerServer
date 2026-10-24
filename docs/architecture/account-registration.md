# Account registration

Self-service account creation for the OpenConquer client. Retail 5517 opened a registration
website from the Sign Up button; the in-client flow and its packets are a project extension. The
wire format is in the [protocol documentation](../protocol/README.md#account-registration-0xf001--0xf002-openconquer-extension).

## Flow

```text
client connects → 1059 seed → 0xF001 name + password envelope → 0xF002 result → close
```

`LoginHandshakeProcessor` reads the first frame. `0xF001` goes to `AccountRegistrationService`; it
never authenticates, issues a ticket, kicks a session or reads post-authentication reports. With
registration disabled the reply is `4` (unavailable).

## Policy

| Field    | Rule |
| -------- | ---- |
| Username | 4–16 characters, printable ASCII without spaces (`!` … `~`) |
| Password | 6–15 characters |

The 5517 client sends account names as ANSI bytes in its active code page, so only ASCII is
accepted: a name stays identical on every client. Its password field holds 15 characters and its
keypad transform carries only letters, digits and a few symbols; because the server decodes the
registration through the same transform, every accepted password can be typed back at login. The
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
Connection admission limits still apply before any of this.

## Persistence

`AccountRegistrationStore` runs one transaction through a dedicated registration identity:

1. Insert the account: active, player role, state revision 1, `created_by_actor_kind = 1`
   (self-service), initial state set by the system (`3`), a fresh `creation_operation_id`.
2. Insert the password credential (current PBKDF2 format, revision 1, changed at the creation time).
3. Insert the `AccountCreated` audit event with actor self-service and the operation ID as its
   correlation ID.

A duplicate on `UX_accounts_username` rolls back and reports the name as taken. No existence query
runs first, so concurrent requests for one name cannot both succeed. If the commit acknowledgment
is lost, the store reads the account back by its operation ID: found means created, absent means
not created, and neither is retried. Any other failure reports `3` to the client and is surfaced to
the worker, which logs it.

Nothing is logged with the username or password.

## Configuration

Registration is off unless `AccountServer:Registration:Enabled` is `true`, which also requires
`ConnectionStrings:AccountRegistration`. That identity must use the same Accounts database
endpoint as `ConnectionStrings:Accounts`, must not be `root` and must differ from the AccountServer
identity. Grants are in the [AccountServer operations guide](../operations/account-server.md#registration-identity).
