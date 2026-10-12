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

The hosted GameServer must validate the configured starting location against loaded world-map and
collision content before constructing the runtime creation service.

The starting-state snapshot is not automatically loaded from disk or registered with the GameServer
in this slice. Its authoritative content source and startup integration remain separate
responsibilities.

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

The creation service does not retry a database operation after an uncertain write outcome.

A request may be rejected before persistence without modifying durable state.

The persistence store owns the write and its cancellation boundary.

## Remaining runtime integration

The GameServer character-creation route remains disabled.

Enabling it requires:

1. Loading and validating authoritative starting-state content.
2. Validating the starting location against world-map content.
3. Provisioning and configuring dedicated creation database credentials.
4. Binding the application service to the authenticated account identity.
5. Sending the native `NEW_ROLE` control message.
6. Processing native packet `1001` on the authenticated secured connection.
7. Handling cancellation, invalid requests, duplicate submissions, and failures.
8. Sending the verified success response only after durable creation succeeds.
9. Validating the repeated `1052` login proof on the same encrypted connection.
10. Resolving the newly persisted character and continuing existing-character bootstrap without
    losing connection ownership.

The repeated proof must not be treated as a second opportunity to redeem an already consumed
single-use login ticket.

Failure-message wording not established by native evidence must not be presented as confirmed retail
protocol behavior.
