# Character creation persistence

The GameServer can set up a character-creation store and application service. Configuring them does
not enable the 5517 character-creation route. Connections that reach the creation route still close
without writing anything.

## Configuration

The store and service are registered when a valid `GameServer:CharacterCreation` starting-state
section is present, as described in [Starting state](character-creation-starting-state.md); without
it they are not registered. The store writes through `ConnectionStrings:Game`, whose `SELECT, INSERT`
on `characters` is listed in [Database identities](database-identities.md#gameserver-runtime-per-realm).
The GameServer never runs DDL itself.

## Runtime behavior

The authenticated login processor is unchanged, so creation requests still
cannot call `CharacterCreationService`. No schema migration, listener change, creation packet
handling, or automatic database write comes with this feature.

`CharacterCreationStore` runs one parameterized INSERT into the `characters` table and handles
uniqueness conflicts. Registering the service does not open a connection. If the configured spawn
content is invalid, starting-state validation still stops host startup before the world is
published.

Before the creation route can be enabled, the server needs checked handling of the 5517 request, a
strict binding to the account identity, ownership tied to the same connection, continuation of the
login when the client repeats its login proof, a reliable signal that the write is durable, time
limits on requests and their failures, and a check of the account's privileges. No 5095 data is
imported. Historical sources are reconciled separately, and that work may correct 5517 content and
the schema where the sources support it.
