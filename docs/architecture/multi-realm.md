# Realm identity and registry

The architecture is one shared AccountServer and Accounts database, with one GameServer process and Game database per realm. A realm is not a map or a world partition. The decision is grounded in the updated `20_notes/Server/multi-realm-architecture-5517.md`, the native 5517 selection trace, and the historical TQ 15.10 topology. The original TQ AccountServer binary remains unavailable; its internal routing and status-code mapping are not claimed as recovered.

## Identity

`ProtocolServerName` is the name carried by the native 1060 login request, not the Flash display label. It is also used by the original client as a local file namespace. This project accepts 1–15 ASCII characters from `[A-Za-z0-9_()-]`, compared with ordinal, case-sensitive semantics. The stable `RealmId` is a modern internal identity and is never accepted from the client as authorization evidence.

Distinct UI rows can select the same logical realm; the database has one row per unique protocol name, not one row per display entry. Operators must never rename an existing protocol name or recycle a realm ID. A rename requires a separately designed migration of client state, accounts, and game data.

## Accounts schema v3

`realms` is an operator-provisioned, read-only runtime directory. Each row contains a stable nonzero `realm_id`, a case-sensitive unique `protocol_server_name`, a concrete IPv4 GameServer address stored as `BINARY(4)`, a nonzero GameServer port, and an availability state (`1` enabled, `2` maintenance, `3` disabled). A zero-row registry is a supported initial migration state but cannot route a real login once realm selection is enabled.

The table is deliberately owned by a SQL-only EF migration rather than the `AccountDbContext` entity model: runtime access is a single parameterized lookup through the existing keyed `MySqlDataSource`, and operators manage changes separately. The model snapshot remains unchanged because it contains only EF-mapped tables; do not generate a second EF `realms` entity or migration without first deciding to transfer schema ownership. MySQL enforces realm name uniqueness and fundamental integrity; the registry refuses malformed rows rather than inventing endpoint defaults.

`IRealmRegistry.FindByNameAsync` returns a validated immutable registration or `null` for an unknown name. It does not grant tickets, redirect logins, cache availability, select a Game database, or authorize an identity. Existing authentication, rate limiting, and ticket behavior are unchanged by this slice.

## Pending security gates

Before enabling any realm-aware login: resolve the selected 1060 name against this registry, reject unavailable realms without disclosing account information, bind newly issued tickets to the selected stable realm ID, and require that exact realm at atomic redemption. The next schema migration **must be Accounts v4**, because the registry is already committed as v3; two distinct deployed schema shapes may not share one version.

Game schema v10 adds an operator-provisioned one-row binding in each separate Game database, checked against the configured GameServer realm before world publication. Cross-realm ticket redemption must still be denied before character lookup in a subsequent slice. Neither a shared Game database nor a distributed control plane is needed.

## Operational integrity

See [database migrations](../operations/database-migrations.md). Deploy schema v3 with the AccountServer and GameServer stopped; hosts reject schema v2 afterward. Migration does not create a default realm, because unknown endpoint addresses and world identities must never be guessed. An administrator provisions the first row deliberately, with runtime accounts granted `SELECT` only. Maintain regular backups and protect the database and signing/HMAC credentials independently.
