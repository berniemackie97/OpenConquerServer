# Character Creation — Native 5517 Registration Request

## Evidence

Packet `1001` is the native Conquer Online 5517 character-creation and cancellation request.

The native-client implementation is documented in:

- `20_notes/Network/01_core_packets.md`
- `20_notes/Network/13_connection_lifecycle.md`
- `20_notes/Server/character-attributes-and-auto-allot.md`

Verified native functions:

| Function                                                          | Address    |
| ----------------------------------------------------------------- | ---------- |
| `CMsgCreatePlayer1001__Ctor`                                      | `0x720059` |
| `CMsgCreatePlayer1001__Build`                                     | `0x7200BF` |
| `CRoleCreateFlashDlg_SubmitCreatePlayerRequest`                   | `0x51C5C3` |
| `CRoleCreateFlashDlg_SendCreatePlayerCancelAndPostHostCommand481` | `0x51C54B` |
| `CMyShellDlg_HandleCreateRoleStatusSystemTalk`                    | `0x5DCDDF` |
| `CConnectionFlow__SendGameSessionBootstrap`                       | `0x6DF1D0` |

Version-matched and historical server references:

**FoxConquer 5517**

- `ServerCore/Networking/Packets/1001-CharacterCreation.cs`
- `MsgServer/Network/GameServer/Handlers/1001-MsgRegister.cs`

**OpenConquerPublic**

- `src/OpenConquer.Protocol/Game/Packets/CreatePlayerRequest1001.cs`
- `src/OpenConquer.GameServer/Characters/GameCharacterCreationSessionProcessor.cs`
- `tests/OpenConquer.Protocol.Tests/CharacterCreationPacketTests.cs`

OpenConquerPublic is the project's previous, partially verified working implementation. It provides
useful implementation and regression-test evidence.

The native-client disassembly remains authoritative for client wire behavior. Historical server
implementations do not independently establish retail server-side behavior.

## Request Layout

Packet `1001` is a fixed-length, 124-byte client-to-server frame.

| Offset | Width | Field                            |
| -----: | ----: | -------------------------------- |
|      0 |     2 | Frame length: `124`              |
|      2 |     2 | Packet identifier: `1001`        |
|      4 |     4 | Cancellation flag                |
|      8 |    16 | Reserved, zero                   |
|     24 |    48 | Character name storage           |
|     72 |     2 | Body selection                   |
|     74 |     2 | Profession selection             |
|     76 |     4 | Client-reported session UID      |
|     80 |    40 | Client-reported MAC-address text |
|    120 |     1 | Native builder writes zero       |
|    121 |     3 | Unverified trailing bytes        |

All multibyte integers are little-endian.

The cancellation flag is a 32-bit unsigned integer:

- `0`: character-creation request.
- `1`: character-creation cancellation.

Other values are rejected as malformed.

The native client clears the name, body, and profession selections when submitting cancellation. The
client-reported session UID remains present.

The parser preserves the raw body, profession, and session UID values. It does not treat them as
authenticated or authorized values.

The 16 reserved bytes at offset 8 and the byte at offset 120 are checked against the documented
native zero values.

Bytes 121–123 have no independently verified semantics and are not interpreted or required to
contain zero.

## Character Name Encoding

The native client stores the name in a 48-byte packet field.

Its creation interface restricts the submitted name to 4–15 bytes and applies additional font/pixel
validation before sending the request.

The parser requires a null terminator within the first 16 bytes of name storage, matching the native
client's maximum input capacity.

The remaining storage bytes are not interpreted as additional name data.

Names are decoded using the existing strict Windows-1252 encoding.

The following undefined Windows-1252 byte values are rejected before decoding:

- `0x81`
- `0x8D`
- `0x8F`
- `0x90`
- `0x9D`

Explicit rejection is necessary because .NET's Windows-1252 implementation can map these values to
control characters without invoking the configured decoder exception fallback.

Bytes following the first null terminator do not contribute to the decoded name and are not
independently interpreted as protocol fields.

The parser performs structural and encoding validation only.

`CharacterNamePolicy` remains responsible for authoritative server-side character-name validation,
including length, forbidden characters, and other domain restrictions.

A successfully decoded name is not necessarily a valid character name.

## Client-Reported MAC Address

The native packet reserves 40 bytes beginning at offset 80 for MAC-address text.

The previous OpenConquerPublic implementation accepted an empty value or 12 uppercase hexadecimal
characters.

The native account-login implementation also has evidence of uppercase hexadecimal MAC-address
formatting.

However, those facts do not independently establish an exclusive character- creation MAC validation
contract for every supported native-client condition.

The current parser therefore:

- Reads the complete 40-byte storage field.
- Stops text decoding at the first null terminator.
- Requires ASCII bytes in the decoded portion.
- Preserves the resulting string without interpreting it as identity.

The parser does not duplicate OpenConquerPublic's MAC-address format policy.

Client-reported hardware identifiers are untrusted and must not establish account ownership,
authorization, or authenticated identity.

## Native Character Selections

The native creation interface maps vocation indices to profession codes:

| Vocation indices | Profession code |
| ---------------- | --------------: |
| 0, 1             |             100 |
| 2, 3             |              10 |
| 4, 5             |              40 |
| 6, 7             |              20 |
| 8, 9             |              50 |
| 10, 11           |              60 |

Native body selection:

| Vocation index parity | Body-size selection | Body identifier |
| --------------------- | ------------------- | --------------: |
| Even                  | 1                   |            2001 |
| Even                  | Other               |            2002 |
| Odd                   | 1                   |            1003 |
| Odd                   | Other               |            1004 |

These mappings establish the native client's submitted values.

The request parser preserves raw integer selections without performing profession normalization,
class validation, or character initialization.

Server-side selection validation belongs in the authenticated character- creation application flow.

## Security Boundaries

Packet `1001` is received on an authenticated GameServer connection.

The request packet does not establish account identity.

Authoritative character ownership must come from the authenticated GameServer session, not from
fields supplied by the client.

The client-reported session UID must not independently establish authentication or ownership.

The client-reported MAC address is not an authentication credential.

The existing character-name policy, account ownership rules, and database uniqueness constraints
must remain authoritative.

Character creation must be transactional and concurrency-safe.

The existing unique database constraints on character names and account ownership must remain the
final concurrency boundary.

The creation persistence design must also preserve the repository's least-privilege database
credential model.

## Verified Native Session Continuation

The native client supports character creation on the existing encrypted GameServer connection.

The verified successful sequence is:

1. GameServer authenticates the connection.
2. GameServer sends packet `1004` with `NEW_ROLE`.
3. The client enters character-creation mode.
4. The client sends packet `1001`.
5. On successful creation, GameServer sends packet `1004` with `ANSWER_OK`.
6. The client invokes `CConnectionFlow__SendGameSessionBootstrap`.
7. The client sends another post-DH `1052` game-login proof.
8. World-entry bootstrap continues on the same connection.

The client does not require a new account-server login or a new GameServer connection after
successful character creation.

The native create-role control-message handler also exposes non-success messages through the
creation interface's `ERROR` variable.

The successful control path is verified. Complete retail server-side failure messages and
persistence behavior remain separate evidence requirements.

## Remaining Implementation Work

The native packet layout, client-side selections, and successful same-session continuation are
established.

Before enabling complete character creation, the implementation must establish:

1. Authoritative server-side profession and body acceptance rules.
2. Complete character-creation failure-response semantics.
3. Authoritative initial appearance, attributes, and character state.
4. Initial map and position policy.
5. Transactional character persistence and database permissions.
6. Duplicate-submission and concurrent-creation behavior.
7. Authenticated session ownership throughout creation.
8. Successful continuation through repeated login proof and world bootstrap.
9. Cancellation, failure, timeout, and disconnect cleanup.
10. Integration and database concurrency tests.

OpenConquerPublic provides an existing implementation reference for several of these
responsibilities.

Its historical starting-state defaults and server policies are not automatically authoritative
native 5517 behavior.

Missing retail server-side policies must not be presented as client-disassembly facts.

## Runtime Status

The packet `1001` request parser is independently testable.

It validates the native request structure and exposes untrusted decoded fields.

The hosted GameServer character-creation route remains intentionally fail-closed.

This slice does not create characters, change database permissions, send speculative creation
responses, or transfer connection ownership.

Complete character creation remains a separate implementation responsibility.
