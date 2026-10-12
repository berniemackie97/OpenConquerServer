# Character creation: dedicated persistence credentials

The GameServer can optionally compose a character-creation store and application service using a
**separate** database identity. Configuring these services does not enable the native character-
creation route. Connections resolving to the creation route still close without issuing a write.

## Configuration

`ConnectionStrings:GameCreation` is optional and omitted from the distributed appsettings files.
A deployment that supplies it must also provide the fully validated `GameServer:CharacterCreation`
starting-state section described in [Starting state](character-creation-starting-state.md).
The default deployment does not register `ICharacterCreationStore`, `CharacterCreationService`,
or the creation starting-state service.

The configuration loader checks that the creation connection targets the same host, port and
case-sensitive database name as `ConnectionStrings:Game`, and has a separate, nonempty, non-root
SQL user identity. It rejects malformed connection strings without including original driver
parsing errors that may contain secrets. This prevents an accidental reuse of the GameServer
runtime reader account or a copy-paste error targeting another database.

These checks **do not prove that MySQL grants are correct**. Credential names are not a substitute
for privilege inspection. Operators must provision the account outside the application and verify
that it has only the permissions the creation store needs:

```sql
GRANT SELECT, INSERT ON `openconquer_game`.`characters` TO 'openconquer_game_creation'@'%';
```

The example grant is a deployment template, not a request for the GameServer to execute DDL.
The actual schema, SQL account host restrictions and TLS settings must be provisioned and audited
for the deployment. Do not grant schema migration, UPDATE, DELETE, other-table writes, or global
privileges. Prefer a restrictive database-account host instead of `%` in production.

Store the separate connection string in deployment secrets or GameServer user-secrets; never commit
it to appsettings, the source tree, logs, an example file, or test fixtures with real credentials.
For local development only, the setting can be supplied with:

```bash
dotnet user-secrets set 'ConnectionStrings:GameCreation' '<development-only-connection-string>' --project src/OpenConquer.GameServer
```

Do not paste real connection strings into PRs or build logs.

## Runtime boundary

Registration is opt-in. The existing authenticated login processor is unchanged: creation requests
still cannot invoke `CharacterCreationService`. There is no schema migration, listener change,
character-creation network packet processing or automatic database write.

`CharacterCreationStore` issues one parameterized character INSERT and handles uniqueness conflicts
against the modern `characters` table. It does not open a connection merely because its service
has been registered. Starting-state validation still fails host startup before world publication
if configured spawn content is invalid.

Future route enablement requires verified native request handling, strict account identity binding,
same-connection ownership and repeated-proof continuation, exact durable-success signaling, bounded
request lifetime and failures, and explicit privilege verification. No 5095 data is imported by this
slice. Historical sources remain subjects for independent reconciliation, including corrections to
5517 content and the modern schema wherever the evidence supports changes.
