# Realm identity and registry

All realms share one AccountServer and one Accounts database. Each realm has its own GameServer
process and Game database. A realm is not a map or a world partition. This layout follows
`20_notes/Server/multi-realm-architecture-5517.md`, a trace of how the 5517 client selects a realm,
and the TQ 15.10 server topology. The original TQ AccountServer binary is not available, and this
project does not claim to have recovered its internal routing or status-code mapping.

## Identity

`ProtocolServerName` is the realm name carried by the 1060 login request. It is not the label shown
in the Flash realm list. The original client also uses it as a namespace for local files. This
project accepts 1 to 15 ASCII characters from `[A-Za-z0-9_()-]` and compares names ordinally and
case-sensitively. `RealmId` is a stable internal ID added by this project. It is never taken from
the client or trusted as authorization.

Several rows in the client's realm list can select the same realm. The database has one row per
protocol name, not one per list entry. Operators must never rename an existing protocol name or
reuse a realm ID. A rename would need its own migration of client state, accounts, and game data.

## Accounts schema v3

Operators fill the `realms` table, and the servers only read it at runtime. Each row holds a stable
nonzero `realm_id`, a unique case-sensitive `protocol_server_name`, the GameServer's IPv4 address
stored as `BINARY(4)`, a nonzero GameServer port, and an availability state (`1` enabled, `2`
maintenance, `3` disabled). An empty registry is a valid state right after the migration, but it
cannot route a real login once realm selection is enabled.

A SQL-only EF migration owns the table. It is not part of the `AccountDbContext` entity model: the
runtime reads it with one parameterized lookup through the existing keyed `MySqlDataSource`, and
operators manage its rows. The model snapshot does not change, because it covers only EF-mapped
tables. Do not generate a second EF `realms` entity or migration unless you have first decided to
move the table into the EF model. MySQL enforces unique realm names and basic integrity. The
registry rejects malformed rows instead of filling in default endpoint values.

`IRealmRegistry.FindByNameAsync` returns a validated, immutable registration, or `null` for an
unknown name. Before it verifies credentials, the AccountServer requires the 1060 name to match an
enabled registry entry, case-sensitively. After the credentials check out, it reads the registration
again. If the realm is gone, unavailable, or now has a different ID, the login fails before a ticket
is issued. If only the endpoint changed for the same realm, the packet 1055 redirect uses the new
endpoint. The ticket (Accounts v4) is bound to that realm, and a GameServer for any other realm
refuses to redeem it. The AccountServer has no GameServer endpoint of its own. Every redirect comes
from the registry.

## Availability replies

A refused selection uses the 5517 client's failure codes whose text describes the realm rather than
the account:

| Selection | Code | Client text |
| --- | ---: | --- |
| Unknown, malformed or `Disabled` | `10` | "The Server is down." |
| Enabled, but its GameServer has no current status report | `10` | "The Server is down." |
| GameServer reports busy | `20` | "Server is busy." |
| GameServer reports full | `21` | "Server is busy. Please try again, later." |
| `Maintenance` | `70` | "Server Locked" |

The same check runs again after authentication, so a realm that is disabled, fills up or stops while
a password is verified is still refused before a ticket is granted. Unknown and disabled share one
reply, so retiring a realm by disabling it or by deleting its row looks the same to players. The
check runs before credential verification, so it reveals nothing about accounts.

Codes 10, 20 and 21 are the ones TQ's AccountServer sent for an unknown or unlinked world, a busy
world and a full world. The source for this is TQ's AccountServer source code, not the shipped
binary, and the codes match the 5517 client's text table one to one. TQ had no maintenance state.
Code 70 is the client's own text for it. A realm disabled in the instant between the
post-authentication lookup and the locked ticket grant still fails, with code `1`, because the grant
reports every lost precondition as one outcome.

## Realm status

TQ's world servers kept a connection open to the AccountServer and reported their online count, and
whether they were full or busy, every 30 seconds. A world whose connection dropped was treated as
down. Here each GameServer writes the same report to the Accounts database `realm_status` table
instead:

| | |
| --- | --- |
| Interval | `GameServer:RealmStatus:ReportInterval` (default 10 seconds) |
| Lifetime | Three intervals, in database time. A report that is not refreshed in time stops counting, and the realm reads as down. |
| Full | Resident characters reached `GameServer:World:MaxCharacters` |
| Busy | A map partition's command queue is full and refusing work (TQ: a map group's message backlog passed 5,000) |
| Start | First report once the listener is open |
| Stop | Report deleted before the listener closes, so logins are refused at once |

The report is advisory. The GameServer still enforces its own capacity: a login that arrives while
the realm is full or busy is answered with a login-channel talk (`1004`, channel 2101) and the
connection is closed, the way TQ's world server refused it. The 5517 client shows that text as a
login error. TQ let accounts at or below a configured GM account ID bypass full and busy. There are
no staff accounts yet, so nothing bypasses the limit.

## One realm at a time

An account plays in one realm at a time. TQ's AccountServer kept an online table that its world
servers filled. A login for an account still online in another world sent that world a kick (fee
message type 4) and was refused with code 11, "Please try again later." The player's retry got
through once the old world had let the account go. The AccountServer did not refuse a login to the
world the account was already in. That world dealt with it. Here:

| | |
| --- | --- |
| Record | Accounts v6 `account_sessions`: one row per account naming the realm, character and login session it plays in. The GameServer writes it when an existing character's login passes the map check and removes it when that connection closes. |
| AccountServer | After authentication, if the account's record names another realm that is reporting status, it queues a kick for that realm in `account_session_kicks` and refuses with code `11`. |
| GameServer | Writes the record in a transaction that locks the account's row. If another reporting realm holds it, the GameServer queues the same kick and refuses with a login-channel talk, "Your account is still online. Please try again later.", so two realms entered at the same moment cannot both admit the account. A record held by this realm or by a realm that is not reporting is taken over. |
| Kick | Each GameServer reads its kicks every second. A kicked account that is connected receives the kick notice (`1115`, reason 0) and is disconnected. Its record goes with the connection. A kicked account that is not connected only has its leftover record removed. |
| Start and stop | A GameServer removes all of its realm's records and kicks when it starts and when it stops. |

A second login to the same realm replaces the first at once, and the replaced session also receives
`1115`. TQ's world server instead kicked the session it had and refused the new login too, so the
player had to log in a third time. This server replaces the old session on purpose so the second
login succeeds. TQ could also hold a logged-out account for a configured delay
(`REJOINTIMEWAITSECS`) before letting it into another world. There is no such delay here.

The 5517 client turns `1115` reason 0 into its "logged in from another location" state, but its
packaged login screen has no handler for that state, so the player sees the client's usual
"disconnected" text. The notice is sent because it is the client's own message for this case. TQ's
2008 world server used a talk on channel 2100 instead, which the 5517 client puts in its
character-creation queue.

## Routing and deployment

The realm check before ticket issue, the realm-bound tickets of Accounts v4, and successful 1055
redirects taken from the registry are all implemented. Each additional realm needs its own
provisioned Game database, numeric realm binding, reachable endpoint, and secrets.
[Realm operations](../operations/realms.md) describes the procedure. The registry is a configuration
source. It does not check realm health or create realms automatically.

Game schema v10 adds a one-row realm binding, provisioned by the operator, to each separate Game
database. The GameServer checks it against its configured realm before it publishes the world. A
ticket for another realm is rejected inside the locked, atomic transaction that consumes the ticket,
before any character lookup. Neither a shared Game database nor a distributed control plane is
needed.

## Operational integrity

See [database migrations](../operations/database-migrations.md). Deploy schema v4 with the
AccountServer and every GameServer stopped. After that, the hosts reject older schema versions. The
migration does not create a default realm, because endpoint addresses and world identities must
never be guessed. An administrator adds the first row by hand, and the runtime accounts are granted
`SELECT` only. Keep regular backups, and protect the database credentials and the signing/HMAC
credentials separately.

## Accounts schema v4: realm-bound tickets

The realm selected in 1060 is checked before credentials. Its immutable `RealmId` is passed to the
ticket grant and stored with each new ticket. In the same transaction that consumes the single-use
ticket, GameServer redemption compares the stored `realm_id` with the GameServer's configured realm,
which has been checked against its database. On a mismatch the ticket is not consumed and no
character is authorized. The migration invalidates outstanding pre-v4 tickets instead of guessing
their realm. A successful 1055 response uses the current IPv4 endpoint of the selected enabled
registration, read in a second lookup. Failure responses carry only the failure code, because the
client's failure branch reads nothing else.
