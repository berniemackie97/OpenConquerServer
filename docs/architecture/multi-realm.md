# Realm identity and registry

The architecture is one shared AccountServer and Accounts database, with one GameServer process and Game database per realm. A realm is not a map or a world partition. The decision is grounded in the updated `20_notes/Server/multi-realm-architecture-5517.md`, the native 5517 selection trace, and the historical TQ 15.10 topology. The original TQ AccountServer binary remains unavailable; its internal routing and status-code mapping are not claimed as recovered.

## Identity

`ProtocolServerName` is the name carried by the native 1060 login request, not the Flash display label. It is also used by the original client as a local file namespace. This project accepts 1–15 ASCII characters from `[A-Za-z0-9_()-]`, compared with ordinal, case-sensitive semantics. The stable `RealmId` is a modern internal identity and is never accepted from the client as authorization evidence.

Distinct UI rows can select the same logical realm; the database has one row per unique protocol name, not one row per display entry. Operators must never rename an existing protocol name or recycle a realm ID. A rename requires a separately designed migration of client state, accounts, and game data.

## Accounts schema v3

`realms` is an operator-provisioned, read-only runtime directory. Each row contains a stable nonzero `realm_id`, a case-sensitive unique `protocol_server_name`, a concrete IPv4 GameServer address stored as `BINARY(4)`, a nonzero GameServer port, and an availability state (`1` enabled, `2` maintenance, `3` disabled). A zero-row registry is a supported initial migration state but cannot route a real login once realm selection is enabled.

The table is deliberately owned by a SQL-only EF migration rather than the `AccountDbContext` entity model: runtime access is a single parameterized lookup through the existing keyed `MySqlDataSource`, and operators manage changes separately. The model snapshot remains unchanged because it contains only EF-mapped tables; do not generate a second EF `realms` entity or migration without first deciding to transfer schema ownership. MySQL enforces realm name uniqueness and fundamental integrity; the registry refuses malformed rows rather than inventing endpoint defaults.

`IRealmRegistry.FindByNameAsync` returns a validated immutable registration or `null` for an unknown name. AccountServer now requires the native 1060 name to resolve to an enabled registry row whose endpoint exactly matches its currently configured static 1055 redirect. Invalid or mismatched selection returns a generic authentication failure before credential verification and ticket creation. Registry failures are not converted into success. Accounts v4 binds tickets to the authoritative selected realm and rejects cross-realm redemption. The static endpoint restriction must be replaced only together with the full realm-bound ticket enforcement and routing cutover.

## Pending security gates

The native pre-issuance lookup gate and Accounts v4 realm-bound ticket cutover are implemented. Before enabling multiple GameServer destinations, integrate dynamic endpoint selection and enforce its routing invariants; packet 1055 still advertises one statically configured endpoint.

Game schema v10 adds an operator-provisioned one-row binding in each separate Game database, checked against the configured GameServer realm before world publication. Cross-realm ticket redemption is rejected during the locked, atomic ticket-consumption transaction, before character lookup. Neither a shared Game database nor a distributed control plane is needed.

## Operational integrity

See [database migrations](../operations/database-migrations.md). Deploy schema v4 with the AccountServer and every GameServer stopped; hosts reject older schema versions afterward. Migration does not create a default realm, because unknown endpoint addresses and world identities must never be guessed. An administrator provisions the first row deliberately, with runtime accounts granted `SELECT` only. Maintain regular backups and protect the database and signing/HMAC credentials independently.

## Accounts schema v4 — realm-bound tickets

The native 1060 selection is validated before credentials. The selected immutable `RealmId`
is propagated to ticket grant and persisted with each new ticket. Under the same transaction
that consumes the single-use ticket, GameServer redemption checks the stored `realm_id`
against the configured and database-verified GameServer realm. A mismatch does not consume
the ticket and does not authorize a character. Pre-v4 outstanding tickets are invalidated
by the migration instead of receiving a guessed realm. The existing static redirect is
unchanged; this is not dynamic multi-realm routing.
