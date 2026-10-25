# Character creation persistence

When an approved `GameServer:CharacterCreation` starting-state section is configured,
the GameServer enables the authenticated character-creation route. Without it, the
creation route remains unavailable and no creation persistence is attempted.

## Configuration

The store and service are registered when a valid `GameServer:CharacterCreation` starting-state
section is present, as described in [Starting state](character-creation-starting-state.md); without
it they are not registered. The store writes through `ConnectionStrings:Game`, whose `SELECT, INSERT`
on `characters` is listed in [Database identities](database-identities.md#gameserver-runtime-per-realm).
The GameServer never runs DDL itself.

## Runtime behavior

The authenticated login route sends `NEW_ROLE`, validates packet 1001 under the
original account identity, and calls the existing atomic creation service.
Invalid name, profession/body selection and taken-name responses are bounded and
use native-supported creation-channel error text. Malformed frames, cancellation,
exhausted attempts and uncertain writes fail closed. The server sends `ANSWER_OK`
only after a durable success and consumes the single permitted repeated 1052 before
joining normal existing-character bootstrap. No new database principal, migration
or ticket redemption occurs.

`CharacterCreationStore` runs one parameterized INSERT into the `characters` table and handles
uniqueness conflicts. Registering the service does not open a connection. If the configured spawn
content is invalid, starting-state validation still stops host startup before the world is
published.

Before enabling creation on a deployed realm, provision an independently reviewed
starting map/tile and the six source-derived level-one allotments. Confirm the single
GameServer database identity has only the required `SELECT, INSERT` character
permissions and existing location-writer rights. No 5095 data is imported.
Never enable an arbitrary nearby tile merely because the raw terrain says passable.
Client-side creation UI, gameplay and a live MySQL error/restart drill remain
separate release gates.
