# Character Creation — 5517 Registration Request

## Evidence

The version-matched FoxConquer 5517 reference identifies `MsgRegister` as packet `1001`.

Reference implementations:

- `ServerCore/Networking/Packets/1001-CharacterCreation.cs`
- `MsgServer/Network/GameServer/Handlers/1001-MsgRegister.cs`

These sources establish a candidate request layout. They do not independently establish native
retail-client behavior or the complete creation exchange.

## Request Layout

| Offset | Width | Field                          |
| -----: | ----: | ------------------------------ |
|      0 |     2 | Frame length                   |
|      2 |     2 | Packet identifier: `1001`      |
|      4 |     1 | Nonzero indicates cancellation |
|     24 |    15 | Character name                 |
|     72 |     2 | Body selection                 |
|     74 |     2 | Profession selection           |
|     80 |    12 | Client-reported MAC address    |

All multibyte integer values are little-endian.

The decoder requires at least 92 packet bytes and enforces the existing GameServer maximum packet
length of 1,024 bytes. It does not assign meaning to unverified intermediate or trailing fields.

Fixed-width text uses the existing strict Windows-1252 protocol decoding policy. Names and
client-reported MAC addresses are decoded but are not trusted.

The parser preserves raw client-selected body and profession values. It does not normalize
unsupported professions or manufacture character appearance values.

## Security Boundaries

The request packet is not authentication.

Character creation must use the account identity established through the existing authenticated
GameServer connection. A client-supplied MAC address cannot establish account ownership or authorize
character creation.

The existing `CharacterNamePolicy` is the authoritative character-name validation boundary. A valid
wire string is not automatically an acceptable character name.

Persisted character ownership and name uniqueness must ultimately be enforced atomically by the
database, not through a preliminary availability query.

## Remaining Native Evidence

Before enabling the character-creation route, verify against the actual 5517 client:

1. Complete packet `1001` request length and offsets.
2. Body and starting-profession selections accepted by the client.
3. Character-creation response packet, payload, and error semantics.
4. Whether a successful creation continues on the same GameServer connection.
5. The required transition into existing-character bootstrap.
6. Native starting appearance, attributes, position, and initial character state.

The version-matched reference is supporting evidence, not a replacement for native client
verification.

## Runtime Status

The decoder is independently testable and does not change the currently hosted login flow.

Accounts without persisted characters continue to follow the existing fail-closed character-creation
route. No character records are written, and no speculative creation responses are sent.

Full character creation remains unimplemented until the wire protocol, authoritative initial state,
and transactional persistence boundary are verified.
