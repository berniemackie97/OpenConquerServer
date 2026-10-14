# Realm identity and registry

The architecture is one shared AccountServer and Accounts database, with one GameServer process and Game database per realm. A realm is not a map or a world partition. The decision is grounded in the updated `20_notes/Server/multi-realm-architecture-5517.md`, the native 5517 selection trace, and the historical TQ 15.10 topology. The original TQ AccountServer binary remains unavailable; its internal routing and status-code mapping are not claimed as recovered.

## Identity

`ProtocolServerName` is the name carried by the native 1060 login request, not the Flash display label. It is also used by the original client as a local file namespace. This project accepts 1–15 ASCII characters from `[A-Za-z0-9_()-]`, compared with ordinal, case-sensitive semantics. The stable `RealmId` is a modern internal identity and is never accepted from the client as authorization evidence.

Distinct UI rows can select the same logical realm; the database has one row per unique protocol name, not one row per display entry. Operators must never rename an existing protocol name or recycle a realm ID. A rename requires a separately designed migration of client state, accounts, and game data.

## Accounts schema v3

`realms` is an operator-provisioned, read-only runtime directory. Each row contains a stable nonzero `realm_id`, a case-sensitive unique `protocol_server_name`, a concrete IPv4 GameServer address stored as `BINARY(4)`, a nonzero GameServer port, and an availability state (`1` enabled, `2` maintenance, `3` disabled). A zero-row registry is a supported initial migration state but cannot route a real login once realm selection is enabled.

The table is deliberately owned by a SQL-only EF migration rather than the `AccountDbContext` entity model: runtime access is a single parameterized lookup through the existing keyed `MySqlDataSource`, and operators manage changes separately. The model snapshot remains unchanged because it contains only EF-mapped tables; do not generate a second EF `realms` entity or migration without first deciding to transfer schema ownership. MySQL enforces realm name uniqueness and fundamental integrity; the registry refuses malformed rows rather than inventing endpoint defaults.

`IRealmRegistry.FindByNameAsync` returns a validated immutable registration or `null` for an unknown name. AccountServer requires the native 1060 name to resolve to an enabled, case-sensitive registry entry before credential verification. After successful verification, it re-reads the registration: a missing, unavailable or different-ID result fails before ticket issuance. An updated endpoint for the same immutable identity is used for the successful packet 1055 redirect. The durable Accounts v4 ticket is bound to that realm, and GameServer redemption denies cross-realm use. The AccountServer has no GameServer endpoint of its own; every redirect comes from the registry.

## Availability replies

A refused selection uses the native failure codes whose client text describes the realm, not the account:

| Selection | Code | Client text |
| --- | ---: | --- |
| Unknown, malformed or `Disabled` | `10` | "The Server is down." |
| Enabled, but its GameServer has no current status report | `10` | "The Server is down." |
| GameServer reports busy | `20` | "Server is busy." |
| GameServer reports full | `21` | "Server is busy. Please try again, later." |
| `Maintenance` | `70` | "Server Locked" |

The same check runs again after authentication, so a realm that is disabled, fills up or stops while a password is verified is still refused before a ticket is granted. Unknown and disabled share one reply, so retiring a realm by disabling or by deleting its row looks the same to players. The check runs before credential verification, so it reveals nothing about accounts.

Codes 10, 20 and 21 are the ones TQ's AccountServer sent for an unknown or unlinked world, a busy world and a full world (its source, not the shipped binary, is the evidence; the codes match the 5517 client's text table one to one). TQ had no maintenance state; 70 is the client's own text for it. A realm disabled in the instant between the post-authentication lookup and the locked ticket grant still fails with code `1`, because the grant reports every lost precondition as one outcome.

## Realm status

TQ's world servers kept a connection open to the AccountServer and reported, every 30 seconds, their online count and whether they were full or busy; a world whose connection dropped was treated as down. Here each GameServer writes the same report to the Accounts database `realm_status` table instead:

| | |
| --- | --- |
| Interval | `GameServer:RealmStatus:ReportInterval` (default 10 seconds) |
| Lifetime | Three intervals, in database time. A report that is not refreshed in time stops counting, and the realm reads as down. |
| Full | Resident characters reached `GameServer:World:MaxCharacters` |
| Busy | A map partition's command queue is full and refusing work (TQ: a map group's message backlog passed 5,000) |
| Start | First report once the listener is open |
| Stop | Report deleted before the listener closes, so logins are refused at once |

The report is advisory. The GameServer still enforces its own capacity: a login that arrives while the realm is full or busy is answered with a login-channel talk (`1004`, channel 2101) and the connection is closed, the way TQ's world server refused it. The 5517 client shows that text as a login error. TQ let accounts at or below a configured GM account ID bypass full and busy; there are no staff accounts yet, so nothing bypasses the limit.

## One realm at a time

An account plays in one realm at a time. TQ's AccountServer kept an online table that its world servers filled. A login
for an account still online in another world sent that world a kick (fee message type 4) and was refused with code 11,
"Please try again later."; the player's retry got through once the old world had let the account go. The AccountServer
did not refuse a login to the world the account was already in; that world dealt with it. Here:

| | |
| --- | --- |
| Record | Accounts v6 `account_sessions`: one row per account naming the realm, character and login session it plays in. The GameServer writes it when an existing character's login passes the map check and removes it when that connection closes. |
| AccountServer | After authentication, if the account's record names another realm that is reporting status, it queues a kick for that realm in `account_session_kicks` and refuses with code `11`. |
| GameServer | Writes the record in a transaction that locks the account's row. If another reporting realm holds it, the GameServer queues the same kick and refuses with a login-channel talk, "Your account is still online. Please try again later.", so two realms entered at the same moment cannot both admit the account. A record held by this realm or by a realm that is not reporting is taken over. |
| Kick | Each GameServer reads its kicks every second. A kicked account that is connected receives the kick notice (`1115`, reason 0) and is disconnected; its record goes with the connection. A kicked account that is not connected only has its leftover record removed. |
| Start and stop | A GameServer removes all of its realm's records and kicks when it starts and when it stops. |

A second login to the same realm replaces the first at once, and the replaced session also receives `1115`. TQ's
world server instead kicked the session it had and refused the new login too, so the player had to log in a third
time; replacing it is a deliberate difference. TQ could also hold a logged-out account for a configured delay
(`REJOINTIMEWAITSECS`) before letting it into another world; there is no such delay here.

The 5517 client turns `1115` reason 0 into its "logged in from another location" state, but its packaged login screen has no
handler for that state, so the player sees the client's usual "disconnected" text. The notice is sent because it is the
client's own message for this case; TQ's 2008 world server used a talk on channel 2100 instead, which the 5517 client
puts in its character-creation queue.

## Routing and remaining deployment gates

The native pre-issuance lookup gate, Accounts v4 realm-bound ticket cutover and registry-directed successful 1055 routing are implemented. Each additional realm needs its own provisioned Game database, numeric realm binding, reachable endpoint and secrets; [Realm operations](../operations/realms.md) is the procedure. The registry is a configuration source, not a health check or an automatic realm provisioning system.

Game schema v10 adds an operator-provisioned one-row binding in each separate Game database, checked against the configured GameServer realm before world publication. Cross-realm ticket redemption is rejected during the locked, atomic ticket-consumption transaction, before character lookup. Neither a shared Game database nor a distributed control plane is needed.

## Operational integrity

See [database migrations](../operations/database-migrations.md). Deploy schema v4 with the AccountServer and every GameServer stopped; hosts reject older schema versions afterward. Migration does not create a default realm, because unknown endpoint addresses and world identities must never be guessed. An administrator provisions the first row deliberately, with runtime accounts granted `SELECT` only. Maintain regular backups and protect the database and signing/HMAC credentials independently.

## Accounts schema v4 — realm-bound tickets

The native 1060 selection is validated before credentials. The selected immutable `RealmId`
is propagated to ticket grant and persisted with each new ticket. Under the same transaction
that consumes the single-use ticket, GameServer redemption checks the stored `realm_id`
against the configured and database-verified GameServer realm. A mismatch does not consume
the ticket and does not authorize a character. Pre-v4 outstanding tickets are invalidated
by the migration instead of receiving a guessed realm. Successful native 1055 responses
use the selected enabled registration's current IPv4 endpoint after a second lookup.
Failure responses carry only the failure code: the client's failure branch reads nothing else.
