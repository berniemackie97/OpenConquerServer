# Protocol

This section describes what goes over the wire between the client and the two servers. The
supported client is Conquer Online version 5517. Server internals are covered in the
[architecture documentation](../architecture/README.md).

- [Framing](framing.md): frame header, size limits, and encoder and decoder behavior.
- [Text encoding](text-encoding.md): string fields and supported encodings.
- [Character creation](character-creation.md): the 5517 character-creation request, its sources,
  what the parser checks, and what is still unverified.

## Frames

Every packet starts with a four-byte little-endian header: a `UInt16` total length (including the
header) and a nonzero `UInt16` packet ID.

| Path               | Maximum frame length                                     |
| ------------------ | -------------------------------------------------------- |
| Framing primitives | 65,535 bytes                                             |
| AccountServer      | 524 bytes                                                |
| GameServer         | 1,024 bytes, plus an 8-byte signature after each frame   |

Each server passes its own limit to the framing layer. The framing primitives hard-code neither
limit.

## AccountServer login

All AccountServer traffic is encrypted with a stream cipher. Each direction has its own cipher
state.

```text
1059 seed → 1060 credentials → 1055 response → 1100 MAC address report → 1052 resource version report
```

| ID     | Direction | Size | Contents                                                      |
| -----: | --------- | ---: | ------------------------------------------------------------- |
| `1059` | S → C     |    8 | Login seed (`UInt32`)                                         |
| `1060` | C → S     |  276 | Account name, credential field, server name                   |
| `1055` | S → C     |   36 | Session ID, key or failure code, GameServer port and address  |
| `1100` | C → S     |   52 | Session ID, MAC address text, four unused bytes               |
| `1052` | C → S     |   28 | Session ID, resource version, resource name (`res.dat`)       |

The success response in `1055` is sent only after the GameServer ticket has been committed. The two
reports that follow are telemetry and never affect the ticket.

### Credential request (1060)

| Offset  | Size | Field            |
| ------: | ---: | ---------------- |
|  `0x00` |  128 | Account name     |
|  `0x80` |  128 | Credential field |
| `0x100` |   16 | Server name      |

The client encrypts only the first 32 bytes of the credential field. To recover the password, the
server derives an RC5-32/12/16 key from the login seed, decrypts those 32 bytes, and reverses the
client's keypad permutation, which is keyed on the raw account name bytes. The remaining 96 bytes
are ignored. Username and password rules are applied only after decoding.

### Account registration (0xF001 / 0xF002, OpenConquer extension)

Retail 5517 created accounts on a website; these two packets are project-owned. A client that wants
to register sends `0xF001` instead of `1060` on a fresh login connection, after receiving `1059`.

| ID       | Direction | Size | Contents                                    |
| -------: | --------- | ---: | ------------------------------------------- |
| `0xF001` | C → S     |  260 | Account name, credential field (as `1060`)  |
| `0xF002` | S → C     |    8 | Result code (`UInt32`)                      |

The request body is the first 256 bytes of `1060`: the same account-name field and the same
seed-keyed RC5/keypad password envelope. It has no server name because accounts are not bound to
a realm. The server sends one `0xF002` and closes the connection; it never sends `1055` or issues a
ticket for a registration.

| Code | Meaning |
| ---: | ------- |
| `0` | Account created |
| `1` | Malformed request, or the name or password breaks registration policy |
| `2` | Name already taken |
| `3` | The server could not confirm the account was created |
| `4` | Registration disabled, or this source has made too many requests |

Packets `1084` (protected login) and `1098` (mobile login) are recognized but not supported and
receive failure code `1`. The 524-byte frame limit exists because `1084` is the largest login
packet.

### Response (1055)

| Offset | Size | Field                                  |
| -----: | ---: | -------------------------------------- |
| `0x00` |    4 | Session ID                             |
| `0x04` |    4 | Authentication key, or failure code    |
| `0x08` |    4 | GameServer port                        |
| `0x0C` |    4 | Additional session field               |
| `0x10` |   16 | GameServer IPv4 address (C string)     |

A nonzero session ID means success. With a zero session ID, `0x04` holds a failure code (`1`
invalid credentials, `10` server down, `11` try again later, `12` banned, `20` server busy, `21`
server full, `57` invalid request, `70` server locked). The client's failure branch reads only that
code and shows its `STR_CONNECT_ERROR_MSG_TYPE_%03u` text, so a failure leaves the port, `0x0C` and
address zeroed. On success the server writes the authentication key into both `0x04` and `0x0C`.
The meaning of `0x0C` is not fully known, so the packet model keeps it as a separate field.

## GameServer connection

The GameServer performs a Diffie-Hellman key exchange, then switches the same TCP stream to
CAST5-CFB64 encryption. After the switch, every frame is followed by an eight-byte signature,
`TQServer` from the server and `TQClient` from the client. This encryption matches what the client
supports. It does not authenticate the server or protect message integrity.

The first encrypted packet is the login proof, `1052` (28 bytes): session ID, authentication key,
mode, locale, hardware address, and resource version. It reuses the ID of the AccountServer
resource report but has a different layout. The server redeems the ticket before doing anything
else with the connection.

## GameServer login

If the GameServer cannot take a login because the realm is full or busy, or because the account is
still playing in another realm, it sends one `1004` talk on channel `0x835` (2101) and closes the
connection. The client shows any login-channel text other than `ANSWER_OK` and `NEW_ROLE` as a
login error.

When a session is closed because its account logged in again, here or in another realm, it first
receives the kick notice `1115`:

| Offset | Size | Field                                              |
| -----: | ---: | -------------------------------------------------- |
| `0x00` |    4 | Reason: `0` logged in elsewhere, `1` password changed |
| `0x04` |    8 | Unread by the client                               |
| `0x0C` |    n | Text, NUL-terminated; the client copies at most 255 bytes |

After authentication, an existing character is loaded in a fixed order driven by the client.
Requests other than `1052` are action packets (`10010`), a 38-byte general-purpose message
identified by its action code. Every request is checked against the authenticated character
before any data is read.

| Step                     | Client request          | Server response                                                       |
| ------------------------ | ----------------------- | --------------------------------------------------------------------- |
| Initial                  | (after `1052`)          | `1004` entry accepted, `2078` login history, `2079` server state, `1006` character info |
| Enter map                | `10010` / `0x4A`        | `1110` map info, `1016` weather, action acknowledgement               |
| Map state applied        | `10010` / `0x198`       | None                                                                  |
| Items                    | `10010` / `0x4B`        | `1008` per item, `1009` subtype 46 if main-set equipment is worn, acknowledgement |
| Friends                  | `10010` / `0x4C`        | `1019` per relation, acknowledgement                                  |
| Weapon skills            | `10010` / `0x4D`        | `1025` per skill, acknowledgement                                     |
| Magic                    | `10010` / `0x4E`        | `1103` per spell, `1104` for spells with experience, acknowledgement  |
| Syndicate                | `10010` / `0x61`        | `1106` if the character is a member, acknowledgement                  |
| Client report            | `10010` / `0xFB`        | Acknowledgement carrying the character ID                             |
| Statistics               | `10010` / `0x84`        | None                                                                  |

Notes on individual steps:

- **Items.** Items are written in ascending item ID order. Item lifetimes are converted to the
  client's signed 32-bit range using one UTC instant per request.
- **Syndicate.** `1106` is a fixed 92-byte layout. Dates are sent as UTC `yyyyMMdd`, and an expired
  position date is sent as zero. Level, mantle, and the unused field at offset `+71` are zero.
- **Client report.** The client sends entity ID zero with a checksum at `+8` and a data version at
  `+16`. Both are untrusted. The acknowledgement uses the authenticated character ID because the
  client needs it to continue; the reported values are echoed back.
- **Statistics.** The request carries the character ID and a client timestamp. It has no response.

After the last step the connection becomes a world session. Gameplay packets are not handled yet.

## Packet types

| ID      | Type                                | Direction |
| ------: | ----------------------------------- | --------- |
|  `1004` | `GameChatPacket`                    | S → C     |
|  `1006` | `GameCharacterInfoPacket`           | S → C     |
|  `1008` | `GameLocalItemSnapshotPacket`       | S → C     |
|  `1009` | `GameActiveEquipmentSnapshotPacket` | S → C     |
|  `1016` | `GameWeatherUpdatePacket`           | S → C     |
|  `1019` | `GameSocialRelation`, `GameSocialRelationPacket` | Both |
|  `1025` | `GameWeaponSkillPacket`             | S → C     |
|  `1052` | `GameLoginProof`                    | C → S     |
|  `1103` | `GameMagicPacket`                   | S → C     |
|  `1104` | `GameSkillExperiencePacket`         | S → C     |
|  `1106` | `GameSyndicateAttributesPacket`     | S → C     |
|  `1110` | `GameMapInfoPacket`                 | S → C     |
|  `2078` | `GameLoginHistoryPacket`            | S → C     |
|  `2079` | `GameServerStatePacket`             | S → C     |
| `10010` | `GameAction`, `GameActionPacket`    | Both      |

Login packet types live in `OpenConquer.Protocol.Login.Packets` and are named after their purpose
(`LoginSeedPacket`, `LoginAccountRequestPacket`, and so on). Each type exposes its packet ID as a
constant, and type names describe the message rather than its number.

## Adding protocol support

A packet type is added when a server feature needs it, together with tests that pin its byte
layout. Unknown fields are kept as raw values or written as zero. They are never given a guessed
meaning.
