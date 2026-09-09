# Conquer Online 5517 Protocol

This directory documents client-visible **Conquer Online 5517** wire behavior implemented or
verified by OpenConquer.

Internal server architecture belongs under [`docs/architecture`](../architecture/README.md).

## Current Coverage

| Area                                      | Status                  |
| ----------------------------------------- | ----------------------- |
| TQ framing                                | Implemented             |
| Binary serialization                      | Implemented             |
| Windows-1252 / ASCII text encoding        | Implemented             |
| AccountServer login stream cipher         | Implemented             |
| Standard 5517 credential decoding         | Implemented             |
| AccountServer authentication response     | Implemented             |
| AccountServer post-authentication reports | Implemented             |
| Standard AccountServer login transaction  | Implemented             |
| Protected/mobile login variants           | Recognized, unsupported |
| GameServer handshake                      | Not implemented         |
| GameServer login proof                    | Not implemented         |
| Game packet signatures                    | Not implemented         |
| Gameplay packets                          | Not implemented         |

Detailed shared contracts:

- [TQ Framing](framing.md)
- [TQ Text Encoding](encoding.md)

## Common TQ Frame

All currently implemented TQ packets use a four-byte little-endian header:

| Offset | Size | Field              |
| -----: | ---: | ------------------ |
| `0x00` |    2 | Total frame length |
| `0x02` |    2 | Packet identifier  |

The declared length includes the header:

```text
FrameLength = 4 + PayloadLength
```

Packet identifier `0` is invalid for a complete TQ packet.

The generic length field is `UInt16`, giving a maximum representable complete frame of:

```text
65535 bytes
```

Protocol-specific paths may impose lower limits.

## Frame Limits

| Path                    | Complete TQ frame limit |
| ----------------------- | ----------------------: |
| Generic framing         |             65535 bytes |
| AccountServer login     |               524 bytes |
| 5517 GameServer traffic |    `0x400` / 1024 bytes |

The `0x400` GameServer limit is verified native behavior but is **not yet active**, because the
GameServer session boundary is not implemented.

The later game protocol also uses a separate eight-byte signature trailer. That trailer is outside
the header-declared `0x400` TQ frame:

```text
0x400 TQ frame
+ 0x008 signature
= 0x408 stream bytes
```

Generic framing does not hard-code either AccountServer or GameServer limits. The owning protocol
path supplies its limit.

## AccountServer Login Packets

| Packet | Direction       | Complete size | Purpose                                    |
| -----: | --------------- | ------------: | ------------------------------------------ |
| `1059` | Server → Client |       8 bytes | Login seed                                 |
| `1060` | Client → Server |     276 bytes | Standard account credentials               |
| `1055` | Server → Client |      36 bytes | Authentication result / GameServer handoff |
| `1100` | Client → Server |      52 bytes | MAC-address report                         |
| `1052` | Client → Server |      28 bytes | `res.dat` version report                   |

Packet `1052` is context-sensitive. The 28-byte AccountServer report is distinct from the later
GameServer login-proof packet using the same identifier.

## Standard AccountServer Transaction

The implemented standard 5517 flow is:

```text
connection established
    ↓
1059 login seed
    ↓
1060 credential request
    ↓
credential authentication
    ↓
durable GameServer login-ticket grant
    ↓
1055 authentication response
    ↓
1100 MAC report
    ↓
1052 res.dat version report
    ↓
AccountServer transaction complete
```

A successful `1055` is never sent before the GameServer login ticket has been durably granted.

The AccountServer transaction is implemented, but the executable listener, admission queue, worker
pool, and production composition root are not yet wired.

## Login Seed and Stream Cipher

The AccountServer sends packet `1059` immediately after the login connection is opened.

Its payload is one `UInt32` login seed.

AccountServer traffic uses the legacy TQ stream transform with independent state for:

```text
client → server
server → client
```

Inbound and outbound byte positions must not share state.

The seed is also used to derive the standard `1060` credential decryption key.

## Standard Credential Request — 1060

The standard retail 5517 credential payload is 272 bytes:

|  Offset | Size | Field            |
| ------: | ---: | ---------------- |
|  `0x00` |  128 | Account name     |
|  `0x80` |  128 | Credential field |
| `0x100` |   16 | Server name      |

Complete frame size:

```text
4-byte header
+ 272-byte payload
= 276 bytes
```

Only the first 32 bytes of the 128-byte credential field are transformed by the standard 5517 path.

Credential decoding uses:

```text
login seed
    ↓
seed-derived RC5-32/12/16 key
    ↓
decrypt first 32 credential bytes
    ↓
account-dependent keypad permutation
    ↓
password characters
```

The keypad permutation uses the original account bytes from the packet. The account name must not be
trimmed or normalized before that step.

The remaining 96 credential bytes are not treated as a validity condition by the standard path.

Decoded password memory is mutable and explicitly disposable.

Application-level username and password policy is applied only after wire decoding.

## Unsupported Credential Variants

Only packet `1060` is implemented as an authentication request.

Known alternate login packet identifiers include:

| Packet | Current handling                                |
| -----: | ----------------------------------------------- |
| `1084` | Recognized protected-login variant; unsupported |
| `1098` | Recognized mobile-login variant; unsupported    |

These variants fail closed and do not enter standard credential decoding.

Unknown login request packet identifiers are also rejected.

The 524-byte AccountServer frame ceiling accommodates verified login traffic including packet
`1084`; it does not imply support for that packet.

OEM, Facebook, custom registration, and other non-standard login paths are not implemented.

## Authentication Response — 1055

Packet `1055` has a 32-byte payload:

| Offset | Size | Field                              |
| -----: | ---: | ---------------------------------- |
| `0x00` |    4 | Session UID                        |
| `0x04` |    4 | Authentication key or failure code |
| `0x08` |    4 | GameServer port                    |
| `0x0C` |    4 | Additional session field           |
| `0x10` |   16 | GameServer IPv4 address            |

### Success

A nonzero session UID selects the native success path.

The implemented standard handoff sends:

```text
SessionUid               = durable ticket SessionUid
AuthenticationKey        = durable ticket AuthenticationKey
GameServerPort           = configured GameServer port
AdditionalSessionField   = AuthenticationKey
GameServerIp             = configured IPv4 address
```

The authentication key is intentionally written into both `0x04` and `0x0C` for the standard
AccountServer handoff, matching preserved server behavior.

The protocol model keeps the fields separate because the complete native meaning of `0x0C` remains
unresolved.

### Failure

A zero session UID selects the failure path. The field at `0x04` becomes the native failure code.

Verified codes:

| Code | Meaning                 |
| ---: | ----------------------- |
|  `1` | Invalid credentials     |
| `12` | Banned account          |
| `57` | Invalid account/request |

Current AccountServer mappings include:

```text
wrong credentials                -> 1
unsupported 1084 / 1098 request -> 1
stale authentication snapshot    -> 1
banned account                   -> 12
malformed standard 1060          -> 57
unknown login packet             -> 57
```

If account state changes between successful credential authentication and durable ticket grant, the
grant fails closed and the client receives generic failure `1`.

## Durable GameServer Handoff

`1055` success represents more than successful password verification.

Before success is sent:

1. credentials must authenticate;
2. current account/password revisions must still match;
3. a nonzero session UID and authentication key must be allocated;
4. the GameServer login ticket must be durably committed.

This prevents a stale authentication result from producing a usable GameServer bearer credential
after an authentication-invalidating account mutation.

Ticket persistence and account security semantics are documented in
[Authentication](../architecture/authentication.md).

## MAC Report — 1100

Packet `1100` has a 48-byte payload:

| Offset | Size | Field                                  |
| -----: | ---: | -------------------------------------- |
| `0x00` |    4 | Session UID                            |
| `0x04` |   40 | MAC-address field                      |
| `0x2C` |    4 | Semantically unresolved trailing bytes |

Complete frame size:

```text
52 bytes
```

AccountServer validation requires:

- session UID matches the issued ticket;
- MAC address is empty or exactly 12 uppercase hexadecimal characters.

The four trailing bytes are structurally consumed but have no assigned semantic meaning in current
evidence.

## Resource-Version Report — 1052

The AccountServer form of packet `1052` has a 24-byte payload:

| Offset | Size | Field            |
| -----: | ---: | ---------------- |
| `0x00` |    4 | Session UID      |
| `0x04` |    4 | Resource version |
| `0x08` |   16 | Resource name    |

Complete frame size:

```text
28 bytes
```

AccountServer validation requires:

- session UID matches the issued ticket;
- resource name is exactly `res.dat`.

The resource-version value is accepted as client telemetry.

## Post-Authentication Reports

The standard client sends:

```text
1100 MAC report
    ↓
1052 res.dat version report
```

The order is enforced.

Each report read receives an independent bounded phase timeout.

Report failures are non-authoritative telemetry failures. They do **not** revoke an already-issued
GameServer login ticket.

This includes:

- missing report;
- timeout;
- unexpected packet;
- malformed report;
- session mismatch;
- invalid MAC format;
- unexpected resource name.

Authentication authorization was already established by the durable ticket grant before these
reports are consumed.

## Text Encoding

Implemented protocol text modes are:

| `TqTextEncoding` | Runtime encoding                     |
| ---------------- | ------------------------------------ |
| `Ansi`           | Windows-1252                         |
| `StrictAnsi`     | Windows-1252 with exception fallback |
| `Ascii`          | ASCII                                |

Shared packet APIs do not accept arbitrary runtime `Encoding` instances.

Fixed-width strings:

- consume their full field width;
- terminate logically at the first null byte;
- zero-fill unused outbound bytes;
- reject outbound embedded null characters;
- are not silently truncated by generic serializers.

Byte-length-prefixed strings use one byte for encoded length and therefore support at most 255
encoded bytes.

See [TQ Text Encoding](encoding.md) for the complete contract.

## Serialization and Memory Ownership

Protocol serializers operate on caller-owned memory.

`PacketReader` is a non-owning cursor over inbound bytes.

`PacketWriter` is a non-owning fixed-capacity writer over outbound memory.

`WireFrameDecoder` accepts caller-owned `ReadOnlySequence<byte>` input and does not advance the
transport buffer itself.

`WireFrameEncoder` serializes payload before committing the common frame header.

Important failure guarantees are documented in:

- [TQ Framing](framing.md)
- [TQ Text Encoding](encoding.md)

## Protocol / Transport Boundary

`OpenConquer.Protocol` owns:

```text
packet identifiers
packet layouts
framing
serialization
text encoding
protocol cryptography
wire compatibility
```

`OpenConquer.Transport` owns:

```text
TCP
connection lifetime
buffer movement
I/O
backpressure
admission
```

Protocol does not own sockets or transport queues.

Transport does not interpret Conquer packet semantics.

See [Networking Architecture](../architecture/networking.md).

## Evidence Policy

Client-visible behavior is derived from:

- reconstructed/native 5517 client behavior;
- preserved OpenConquerPublic server behavior;
- packet captures when available;
- verified protocol vectors and tests.

Native 5517 client behavior has final authority for compatibility.

Legacy server code is behavioral evidence, not an architecture template. Its class layout,
threading, socket abstractions, and ownership patterns are not automatically carried into the
rewrite.

Protocol APIs are added only when supported by concrete wire requirements.

## Not Yet Implemented

The current protocol surface does not include:

- AccountServer registration variants;
- protected/mobile credential decoding;
- GameServer Diffie-Hellman handshake;
- CAST5 game-channel encryption;
- game packet signatures;
- GameServer `1052` login proof;
- character/session bootstrap;
- gameplay packets.

These boundaries should be documented when their implementation or native evidence is sufficiently
complete.
