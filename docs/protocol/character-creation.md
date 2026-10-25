# Character creation request (5517 packet 1001)

## Sources

Packet `1001` is the Conquer Online 5517 client's request to create a character or to cancel
character creation.

The client's implementation is documented in:

- `20_notes/Network/01_core_packets.md`
- `20_notes/Network/13_connection_lifecycle.md`
- `20_notes/Server/character-attributes-and-auto-allot.md`

Client functions, verified in the 5517 executable:

| Function                                                          | Address    |
| ----------------------------------------------------------------- | ---------- |
| `CMsgCreatePlayer1001__Ctor`                                      | `0x720059` |
| `CMsgCreatePlayer1001__Build`                                     | `0x7200BF` |
| `CRoleCreateFlashDlg_SubmitCreatePlayerRequest`                   | `0x51C5C3` |
| `CRoleCreateFlashDlg_SendCreatePlayerCancelAndPostHostCommand481` | `0x51C54B` |
| `CMyShellDlg_HandleCreateRoleStatusSystemTalk`                    | `0x5DCDDF` |
| `CConnectionFlow__SendGameSessionBootstrap`                       | `0x6DF1D0` |

Server references, one for the same client version and one historical:

**FoxConquer 5517**

- `ServerCore/Networking/Packets/1001-CharacterCreation.cs`
- `MsgServer/Network/GameServer/Handlers/1001-MsgRegister.cs`

**OpenConquerPublic**

- `src/OpenConquer.Protocol/Game/Packets/CreatePlayerRequest1001.cs`
- `src/OpenConquer.GameServer/Characters/GameCharacterCreationSessionProcessor.cs`
- `tests/OpenConquer.Protocol.Tests/CharacterCreationPacketTests.cs`

OpenConquerPublic is this project's previous working implementation, which was only partly
verified. It is a useful reference for implementation and regression tests.

For what the client sends on the wire, the client disassembly is the source of truth. Historical
server implementations do not, on their own, show how the retail server behaved.

## Request layout

Packet `1001` is a fixed-length, 124-byte frame sent from client to server. All multibyte integers
are little-endian.

| Offset | Width | Field                                 |
| -----: | ----: | ------------------------------------- |
|      0 |     2 | Frame length: `124`                   |
|      2 |     2 | Packet identifier: `1001`             |
|      4 |     4 | Cancellation flag                     |
|      8 |    16 | Reserved, zero                        |
|     24 |    48 | Character name storage                |
|     72 |     2 | Body selection                        |
|     74 |     2 | Profession selection                  |
|     76 |     4 | Client-reported session UID           |
|     80 |    40 | Client-reported MAC address text      |
|    120 |     1 | Written as zero by the client builder |
|    121 |     3 | Trailing bytes, meaning not confirmed |

The cancellation flag is a 32-bit unsigned integer:

- `0`: character-creation request.
- `1`: character-creation cancellation.

Any other value is rejected as malformed.

When it sends a cancellation, the client clears the name, body and profession selections but still
includes the session UID.

The parser keeps the raw body, profession and session UID values. It does not treat them as
authenticated or authorized.

The parser checks that the 16 reserved bytes at offset 8 and the byte at offset 120 are zero, as
documented for the client. The meaning of bytes 121 to 123 has not been confirmed, so the parser
does not interpret them and does not require them to be zero.

## Character name encoding

The client stores the name in a 48-byte packet field. Its creation screen limits the name to 4 to
15 bytes and runs further font and pixel-width checks before it sends the request.

The parser requires a null terminator within the first 16 bytes of the field, which matches the
client's maximum input length. The remaining bytes are not read as more name data.

Names are decoded with the existing strict Windows-1252 encoding. These undefined Windows-1252 byte
values are rejected before decoding:

- `0x81`
- `0x8D`
- `0x8F`
- `0x90`
- `0x9D`

They are rejected separately because .NET's Windows-1252 implementation can map them to control
characters without calling the configured decoder exception fallback.

Bytes after the first null terminator are not part of the decoded name, and they are not read as
separate protocol fields.

The parser checks only structure and encoding. `CharacterNamePolicy` still performs the server-side
character-name validation, including length, forbidden characters and other domain rules. A name
that decodes successfully is not necessarily a valid character name.

## Client-reported MAC address

The packet reserves 40 bytes starting at offset 80 for MAC address text.

OpenConquerPublic accepted either an empty value or 12 uppercase hexadecimal characters, and the
client's account login code also formats the MAC address as uppercase hexadecimal. These facts do
not show that the client always uses that one format in character creation, under every condition
the client supports.

The parser therefore:

- Reads the whole 40-byte field.
- Stops decoding text at the first null terminator.
- Requires the decoded part to be ASCII.
- Keeps the resulting string without interpreting it as an identity.

It does not copy OpenConquerPublic's MAC address format rule.

Hardware identifiers reported by the client are untrusted. They must not be used to establish
account ownership, authorization or identity.

## Character selections

The client's creation screen maps vocation indices to profession codes:

| Vocation indices | Profession code |
| ---------------- | --------------: |
| 0, 1             |             100 |
| 2, 3             |              10 |
| 4, 5             |              40 |
| 6, 7             |              20 |
| 8, 9             |              50 |
| 10, 11           |              60 |

Body selection:

| Vocation index parity | Body-size selection | Body identifier |
| --------------------- | ------------------- | --------------: |
| Even                  | 1                   |            1003 |
| Even                  | Other               |            1004 |
| Odd                   | 1                   |            2001 |
| Odd                   | Other               |            2002 |

These mappings show the values that the client sends.

The request parser keeps the raw integer selections. It does not normalize professions, validate
classes or initialize the character. The server validates the selections later, in the
authenticated character-creation application flow.

## Security

Packet `1001` arrives on an authenticated GameServer connection, but the packet itself does not
identify the account. Character ownership must come from the authenticated GameServer session,
never from fields the client supplies. The session UID in the packet does not on its own
authenticate the client or prove ownership, and the MAC address is not an authentication
credential.

The existing character-name policy, account ownership rules and database uniqueness constraints
remain the final word on what is accepted. Character creation must be transactional and safe under
concurrent requests. The existing unique database constraints on character names and account
ownership remain the last line of defense against concurrent duplicates. Creation persistence must
also keep the repository's least-privilege model for database credentials.

## Continuing on the same connection

The client creates a character on its existing encrypted GameServer connection. The successful
sequence, verified in the client, is:

1. GameServer authenticates the connection.
2. GameServer sends packet `1004` with `NEW_ROLE`.
3. The client enters character-creation mode.
4. The client sends packet `1001`.
5. On successful creation, GameServer sends packet `1004` with `ANSWER_OK`.
6. The client calls `CConnectionFlow__SendGameSessionBootstrap`.
7. The client sends another `1052` game-login proof after the DH exchange.
8. World-entry bootstrap continues on the same connection.

After a successful creation, the client does not need a new AccountServer login or a new
GameServer connection.

The client's create-role control-message handler also passes messages other than success to the
creation screen's `ERROR` variable.

The success path is verified. The complete set of retail server failure messages and the retail
persistence behavior still need their own sources.

## Remaining implementation work

The packet layout, the client's selections and the same-connection continuation after a successful
creation are known. Before full character creation can be enabled, the implementation still has to
settle:

1. Server-side rules for which professions and bodies to accept.
2. The meaning of every character-creation failure response.
3. The initial appearance, attributes and character state, decided by the server.
4. Initial map and position policy.
5. Transactional character persistence and database permissions.
6. Behavior for duplicate submissions and concurrent creation.
7. Ownership by the authenticated session for the whole of creation.
8. Successful continuation through the repeated login proof and the world bootstrap.
9. Cleanup after cancellation, failure, timeout and disconnect.
10. Integration and database concurrency tests.

OpenConquerPublic is an existing reference implementation for several of these.

Its historical starting-state defaults and server policies are not automatically the retail 5517
behavior. Retail server policies that are still unknown must not be presented as facts from the
client disassembly.

## Runtime status

The packet `1001` request parser can be tested on its own. It validates the request structure and
exposes the decoded fields as untrusted values.

The character-creation route in the running GameServer stays disabled. The current code does not
create characters, change database permissions, send guessed creation responses or transfer
connection ownership. Full character creation is separate work.
