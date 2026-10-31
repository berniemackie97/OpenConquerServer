# Character creation

Character creation is split across the protocol, application, persistence, and GameServer session
layers, each with its own job. The packet layout and client transitions are documented in
[Character creation protocol](../protocol/character-creation.md).

## Layers

The protocol layer decodes packet `1001`. Decoding it grants nothing.

The application layer validates the character name and selections, resolves the trusted starting
state, and calls creation persistence.

The persistence layer inserts a fully resolved character in one atomic step and returns its
generated ID, or a uniqueness conflict reported by the database.

The GameServer takes account ownership from the authenticated connection. When the
optional, validated starting state is configured, the creation route is supervised outside
fixed login workers, uses a bounded creation-admission gate, and rejoins the existing
bootstrap on the same secured connection.

## Starting attributes

The client computes the profession family from the profession code:

`family = (profession % 1000) / 10`

The six professions a player can create are:

| Code | Profession | Family |
| ---: | ---------- | -----: |
|   10 | Trojan     |      1 |
|   20 | Warrior    |      2 |
|   40 | Archer     |      4 |
|   50 | Ninja      |      5 |
|   60 | Monk       |      6 |
|  100 | Taoist     |     10 |

A new character gets the level-one auto-allotment for its profession family. The level-one attribute
budget is ten points. The client's base maximum-life formula is:

`(strength + agility + spirit + vitality * 8) * 3`

The level-one allotments come from the earlier, tested OpenConquerPublic implementation and its
corrected auto-allot data. The client research confirms the arithmetic and the attribute budget. For
Ninja and Monk, the source is still the corrected later auto-allot data that the earlier server
used.

## Starting-state content

`CharacterCreationStartingState` is built from six validated level-one allotments and a trusted
starting map and position. The constructor rejects:

- Missing or invalid starting map identity.
- Missing profession families.
- Duplicate profession families.
- Unsupported profession families.
- Attribute allocations that violate the level-one budget.

The validated allotments are copied into an immutable lookup. Attributes, appearance, spawn
position, and profession family sent by the client are never trusted.

The optional `GameServer:CharacterCreation` configuration section supplies this immutable starting
state. The production `appsettings.json` does not include it. Local Development explicitly configures
map 1010 portal0 at (60,108) and the six corrected profession allotments in
`appsettings.Development.json`. Without the section, the world host starts normally for
existing characters and creation stays unavailable.

When the section is present, it is bound with unknown properties rejected and validated before the
host is built. After the world maps are loaded, the world hosted service checks that the configured
starting map exists and that the starting coordinates fall within its composed collision dimensions.
If the check fails, the world is not published and no network traffic is accepted.

Coordinates are zero-based. Startup validates the selected tile against the effective
composed static collision: any nonzero passability flag is refused. This does not
replace a separate approval of the spawn map and tile from native/content evidence.

The section explicitly enables the authenticated runtime creation route and registers
its existing store and service. Production environments without the explicit section
continue to allow existing-character login with creation disabled.

See [Character creation starting-state operations](../operations/character-creation-starting-state.md).

## Appearance

Packet `1001` carries the selected body. The four accepted bodies are `1003`, `1004`, `2001`, and
`2002`.

The initial face and hairstyle follow the earlier OpenConquerPublic implementation:

- Male face identifier: `201`.
- Female face identifier: `1`.
- Appearance composite: `face * 10000 + body`.
- Hairstyle color component: `3` through `8`.
- Hairstyle style component: `30` through `55`.
- Hair composite: `color * 100 + style`.

The hairstyle components come from a cryptographic random-number generator.

These choices are this server's own policy. They are not confirmed retail-server defaults.

## Application service

`CharacterCreationService` accepts:

- The authenticated account ID.
- The requested character name.
- The requested body.
- The requested profession.

It does not accept a session identity or MAC address reported by the client, or any starting
attributes, appearance, or location.

The service:

1. Rejects an invalid authenticated account ID.
2. Checks for cancellation before doing any work.
3. Validates the character name.
4. Validates the body and profession selections.
5. Resolves the starting state using trusted allotment and spawn configuration.
6. Passes the resulting immutable candidate to character persistence.
7. Translates the persistence result into an application result.

Invalid names and selections never reach persistence. An account that already has a character and a
name that is already taken are reported as separate outcomes. An unexpected status from persistence
is treated as a failure. Persistence errors are passed to the caller without automatic retries.

## Concurrency and durability

The database enforces unique character names and one character per account. Application validation
does not replace these constraints.

The creation service never retries a database operation whose write outcome is uncertain. A request
rejected before persistence leaves stored data unchanged. The persistence store owns the write and
decides up to which point it can be cancelled.

## Historical 15.10 behavior versus this server's security contract

The recovered TQ 15.10 socket-to-registration path
(`CMsgRegister__ProcessCharacterCreation_15_10` at `0x4B0B17`,
`CUserList__CreateNewUser` at `0x4AA79A`) performs one
`INSERT cq_user` using a **request-supplied account ID**. It has no
creation-time account ownership or existing-character precheck on that
identified route. An invalid body, missing map, or SQL failure can all
produce the same duplicate-name refusal; that message does **not**
establish a name collision. The historical profession branch preserves
10, 40, 50 and 100 and normalizes other values to 20. These are
**15.10 observations**, not a verified 5517 server acceptance policy.

Our GameServer intentionally does not reproduce those weaknesses. It
derives account ownership from the redeemed session, restricts the
client's creation selections, and relies on database-enforced
unique name and account constraints. Its explicit application result
categories describe OpenConquer behavior; they must not be presented
as original retail server refusal semantics. The 5517 client packet
layout and character creation UI remain independently authoritative.

On historical 15.10 SQL success, the handler attempts the client
`ANSWER_OK` response, then an AccountServer notice. The identified
path performs no compensating deletion after either notification.
The recovered database wrapper does not distinguish duplicate-key
errors from other query failures and does not prove that every false
query result left storage unchanged. Our one-INSERT store likewise
does not infer a failed commit from a transport error: ambiguous write
outcomes fail closed without an automatic retry, and a missed success
response may leave a character that is recovered on a later login.
Proving failure atomicity for a deployment still requires its actual
database schema/engine/settings and fault-injection testing.

Source: `20_notes/Server/character-creation-decision-15.10.md`
(2026-10-30, IDA path and byte-validated evidence). This historical
finding cannot certify 5517 GameServer parity, deployed database
behavior, or end-to-end notification delivery.

## Runtime creation continuation

A newly authenticated account without a character receives login-channel `NEW_ROLE`.
The server accepts bounded packet `1001` requests on that authenticated socket;
clients cannot supply account ownership. Cancellation and malformed requests close
the connection. The server only emits creation-channel `ANSWER_OK` after a committed
character result. It then consumes exactly one same-session repeated `1052`, without
redeeming the ticket again, resolves the exact created character ID and transfers
connection ownership to the established existing-character bootstrap.

Invalid name, selection and duplicate-name results use explicit server-authored
creation-channel messages, which the native client displays through its verified
error-message path. Up to eight submissions may be attempted within the five-minute
creation window. These are OpenConquer policy strings, not claims of retail wording.
Already-created accounts, malformed requests and uncertain persistence outcomes close
the session without retrying a database write.

The creation waiting period is five minutes; the post-success proof deadline is
thirty seconds. Once that proof is accepted, character resolution and the existing-character
bootstrap receive a fresh `GameServer:Network:ConnectionTimeout` window. Up to
`min(AdmissionCapacity, 1024)` creations can wait independently of the fixed handshake workers. The server shutdown token cancels and drains them.
The Development configuration provides the archive's map-1010 portal0 coordinate
and six corrected profession-family allotments. Production continues to leave
creation disabled until the deployment opts in explicitly.

## Remaining operational validation

Before enabling on a live realm, verify the starting tile against canonical collision,
confirm the six profession-family allotments, and validate the GameServer's `INSERT` grant.
Perform live 5517 client testing and MySQL fault-injection/restart tests separately.
OpenConquerClient PR #147 now has a retail Flash role stage, a secured 1001 continuation,
same-session repeated 1052 and an initial world-entry action ladder. Those protocol changes
do not make this server ready to accept new characters: live creation remains disabled
on a production realm until an approved starting map and tile are configured.
The Development configuration uses the 5517 map catalog's portal0 candidate at
(60,108), and verifies the tile against composed collision at startup.
Live operation still requires verification of the runtime database INSERT grant,
the original role INI in the client content set, and a complete world renderer. The client does not yet host the active world
scene after the map bootstrap. Neither repository's isolated CI substitutes for
a live client/server/database smoke test.

The repeated proof is not a second chance to redeem the single-use login ticket, which has already
been consumed.

Failure messages whose wording has not been confirmed from the client must not be presented as
retail protocol behavior.
