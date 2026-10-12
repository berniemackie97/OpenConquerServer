# Character creation

Character creation is split into independent protocol, application, persistence, and GameServer
session responsibilities.

The native packet layout and client transitions are documented in
[Character creation protocol](../protocol/character-creation.md).

## Established boundaries

The protocol layer decodes packet `1001` without granting authorization.

The application layer validates the character name and selections, resolves trusted starting state,
and invokes creation persistence.

The persistence layer atomically inserts a fully resolved character and returns its generated
identity or an authoritative uniqueness conflict.

The GameServer runtime must establish account ownership through its authenticated game connection.
The character-creation runtime route is not yet enabled.

## Starting attributes

The client derives profession family arithmetically:

`family = (profession % 1000) / 10`

The six creatable professions are:

| Code | Profession | Family |
| ---: | ---------- | -----: |
|   10 | Trojan     |      1 |
|   20 | Warrior    |      2 |
|   40 | Archer     |      4 |
|   50 | Ninja      |      5 |
|   60 | Monk       |      6 |
|  100 | Taoist     |     10 |

Creation uses the profession family's level-one auto-allotment.

The level-one attribute budget is ten points.

The native base maximum-life formula is:

`(strength + agility + spirit + vitality * 8) * 3`

The required level-one allotments are established by the previous tested OpenConquerPublic
implementation and its corrected auto-allot data.

The native evidence establishes the client arithmetic and attribute budget. For Ninja and Monk, the
corrected later auto-allot data remains the source used by the previous server implementation.

## Starting-state content

`CharacterCreationStartingState` is constructed from six validated level-one allotments and a
trusted starting map and position.

The constructor rejects:

- Missing or invalid starting map identity.
- Missing profession families.
- Duplicate profession families.
- Unsupported profession families.
- Attribute allocations that violate the level-one budget.

Validated allotments are copied into an immutable lookup.

No client-provided attributes, appearance, spawn position, or profession-family identity are
trusted.

An optional `GameServer:CharacterCreation` configuration section supplies this immutable starting
state. It is deliberately absent from the distributed default configuration. When omitted, the
existing-character world host starts normally and character creation stays unavailable.

When supplied, the section is bound with strict unknown-property rejection and validated before
the host is built. Once the canonical world maps are loaded, the world hosted service checks that
the configured starting map exists and the starting coordinates are within its composed collision
dimensions. A failure prevents world publication and network ingress.

Coordinates are zero-based. The check establishes **bounds only**, not walkability: the meaning
of every raw terrain passability flag has not been proven. A configured starting location must
still be selected and confirmed against authoritative map data before enabling creation.

The section does not itself enable the character-creation route, register creation credentials,
or authorize character writes. It is an opt-in prerequisite for later runtime integration.

See [Character creation starting-state operations](../operations/character-creation-starting-state.md).

## Appearance

Native packet `1001` supplies the selected body.

The four accepted bodies are `1003`, `1004`, `2001`, and `2002`.

The initial face and hairstyle follow the previous OpenConquerPublic implementation:

- Male face identifier: `201`.
- Female face identifier: `1`.
- Appearance composite: `face * 10000 + body`.
- Hairstyle color component: `3` through `8`.
- Hairstyle style component: `30` through `55`.
- Hair composite: `color * 100 + style`.

Cryptographic random-number generation supplies the hairstyle components.

These choices are explicitly server implementation policy. They are not independently established
retail-server defaults.

## Application service

`CharacterCreationService` accepts:

- An authoritative account ID.
- The requested character name.
- The requested body.
- The requested profession.

It does not accept client-reported session identity, MAC address, starting attributes, appearance,
or location.

The service:

1. Rejects an invalid authenticated account ID.
2. Observes cancellation before performing work.
3. Validates the character name.
4. Validates the body and profession selections.
5. Resolves the starting state using trusted allotment and spawn configuration.
6. Passes the resulting immutable candidate to character persistence.
7. Translates the persistence result into an application result.

Invalid names and selections never reach persistence.

Duplicate account ownership and duplicate names remain distinct outcomes.

Unexpected persistence statuses fail closed.

Persistence failures are propagated without automatic retries.

## Concurrency and durability

Character name and account uniqueness are enforced by the database.

Application validation is not a substitute for these constraints.

The creation service does not retry a database operation after an uncertain
write outcome.

A request may be rejected before persistence without modifying durable state.

The persistence store owns the write and its cancellation boundary.

## Remaining runtime integration

The GameServer character-creation route remains disabled.

Enabling it requires:

1. Provisioning verified starting-state values and a confirmed walkable starting location.
2. Provisioning and configuring dedicated creation database credentials.
3. Binding the creation application service to authenticated account identity.
4. Sending the native `NEW_ROLE` control message.
5. Processing native packet `1001` on the authenticated secured connection.
6. Handling cancellation, invalid requests, duplicate submissions, and failures.
7. Sending the verified success response only after durable creation succeeds.
8. Validating the repeated `1052` login proof on the same encrypted connection.
9. Resolving the newly persisted character and continuing existing-character bootstrap without
   losing connection ownership.

The repeated proof must not be treated as a second opportunity to redeem an already consumed
single-use login ticket.

Failure-message wording not established by native evidence must not be presented as confirmed retail
protocol behavior.
