# Character creation: separate database credentials

The GameServer can optionally set up a character-creation store and application service that use
their own database account. Configuring them does not enable the 5517 character-creation route.
Connections that reach the creation route still close without writing anything.

## Configuration

`ConnectionStrings:GameCreation` is optional and is not in the shipped appsettings files. If you set
it, you must also provide a valid `GameServer:CharacterCreation` starting-state section, as
described in [Starting state](character-creation-starting-state.md). When
`ConnectionStrings:GameCreation` is not set, the GameServer does not register
`ICharacterCreationStore`, `CharacterCreationService`, or the creation starting-state service.

The configuration loader checks that the creation connection uses the same host, port, and database
name (compared case-sensitively) as `ConnectionStrings:Game`, with a different SQL user that is not
empty and not root. It rejects malformed connection strings without repeating the driver's parse
error, which could contain secrets. These checks catch accidental reuse of the GameServer's runtime
reader account and copy-paste mistakes that point at another database.

**These checks do not show that the MySQL grants are correct.** A different user name says nothing
about that user's privileges. Create the account outside the application and confirm that it has
only the permissions the creation store needs:

```sql
GRANT SELECT, INSERT ON `openconquer_game`.`characters` TO 'openconquer_game_creation'@'%';
```

The grant above is a template for your deployment. The GameServer never runs DDL itself. Set up and
audit the schema, the account's host restrictions, and TLS for your deployment. Do not grant schema
migration, UPDATE, DELETE, writes to other tables, or global privileges. In production, restrict the
account to specific hosts instead of using `%`.

Keep this connection string in your deployment's secret store or in the GameServer's user secrets.
Never commit it to appsettings, the source tree, logs, an example file, or test fixtures with real
credentials. For local development only, you can set it with:

```bash
dotnet user-secrets set 'ConnectionStrings:GameCreation' '<development-only-connection-string>' --project src/OpenConquer.GameServer
```

Do not paste real connection strings into pull requests or build logs.

## Runtime behavior

Registration is opt-in. The authenticated login processor is unchanged, so creation requests still
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
