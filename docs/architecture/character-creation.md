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

The GameServer runtime must take account ownership from its authenticated game connection. The
character-creation route in the runtime is not enabled yet.

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
state. The default configuration shipped with the server does not include it. Without it, the world
host starts normally for existing characters and character creation stays unavailable.

When the section is present, it is bound with unknown properties rejected and validated before the
host is built. After the world maps are loaded, the world hosted service checks that the configured
starting map exists and that the starting coordinates fall within its composed collision dimensions.
If the check fails, the world is not published and no network traffic is accepted.

Coordinates are zero-based. The check covers bounds only, not whether the tile is walkable, because
the meaning of each raw terrain passability flag has not been confirmed. A starting location still
has to be chosen and checked against trusted map data before creation is enabled.

The section is an opt-in prerequisite for the later runtime work. On its own it does not enable the
character-creation route, register creation credentials, or allow character writes.

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

## Remaining runtime integration

The GameServer character-creation route is still disabled. Enabling it requires:

1. Provisioning checked starting-state values and a starting location confirmed to be walkable.
2. Provisioning and configuring dedicated creation database credentials.
3. Binding the creation application service to authenticated account identity.
4. Sending the `NEW_ROLE` control message.
5. Processing packet `1001` on the authenticated, encrypted connection.
6. Handling cancellation, invalid requests, duplicate submissions, and failures.
7. Sending the success response only after the character has been committed.
8. Validating the repeated `1052` login proof on the same encrypted connection.
9. Resolving the newly persisted character and continuing existing-character bootstrap without
   losing connection ownership.

The repeated proof is not a second chance to redeem the single-use login ticket, which has already
been consumed.

Failure messages whose wording has not been confirmed from the client must not be presented as
retail protocol behavior.
