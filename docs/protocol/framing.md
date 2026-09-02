# TQ Framing

This document records the framing behavior currently implemented by `OpenConquer.Protocol`.

It describes the common TQ frame header, outbound frame encoding, frame-size boundaries, packet
payload contracts, and failure behavior.

For the protocol documentation index, see [README](README.md).

## Frame Format

A common TQ packet begins with a four-byte header:

| Offset | Size | Field               |
| -----: | ---: | ------------------- |
|      0 |    2 | Total packet length |
|      2 |    2 | Packet identifier   |

Both fields are unsigned 16-bit little-endian integers.

The declared packet length includes the header:

```
PacketLength = 4 + PayloadLength
```

Example:

```
06 00 78 56 AA BB
```

represents:

```
Length:     6
Packet ID:  0x5678
Payload:    AA BB
```

The four-byte header and payload form the TQ packet represented by `WireFrameEncoder`.

Transport-level encryption or signature bytes that surround that packet are separate concerns.

## WireFrameHeader

`WireFrameHeader` is the raw representation of the four-byte TQ header.

It owns only binary interpretation and writing of:

```
UInt16 Length
UInt16 PacketId
```

It deliberately does not apply complete-packet semantic validation.

For example, the raw structure can represent:

```
Length   = 0
PacketId = 0
```

even though those values cannot describe a valid complete TQ packet.

This keeps the responsibilities separate:

```
WireFrameHeader
    raw binary representation

WireFrameEncoder
    complete-packet validity

login/game protocol boundary
    path-specific compatibility rules
```

`WireFrameHeader` should remain usable by future inbound framing without embedding packet-family
policy into the binary header type.

## Byte Order

Header integers use little-endian byte order.

For example:

```
UInt16 0x1234
    -> 34 12
```

The current primitive protocol serializers follow the same little-endian rule.

## Packet Identifier

Native 5517 packet validation requires the packet identifier to be nonzero.

Therefore a complete packet encoded by `WireFrameEncoder` requires:

```
PacketId != 0
```

Packet identifier `0` is rejected before destination memory is modified.

This is a complete-packet rule, not a raw-header rule.

The distinction is intentional:

```
WireFrameHeader
    may represent PacketId = 0

WireFrameEncoder
    rejects PacketId = 0
```

Individual packet families may impose additional identifier expectations when they are implemented.

## Generic Packet Length Limit

The common TQ packet length is represented by a `UInt16`.

The maximum representable declared packet size is therefore:

```
65535 bytes
```

The largest corresponding payload is:

```
65535 - 4 = 65531 bytes
```

This is the generic framing limit supported by `WireFrameEncoder`.

It is not automatically the valid maximum for every protocol path.

## Caller-Supplied Limits

`WireFrameEncoder` supports a caller-supplied maximum packet length.

This allows a future protocol boundary to impose a stricter compatibility limit while leaving the
generic four-byte TQ framing primitive reusable.

Conceptually:

```
generic TQ framing
    maximum 65535

protocol-specific caller
    may provide a lower maximum
```

The current foundation implements the ability to enforce such a supplied maximum.

It does not yet contain the GameServer boundary that selects the game client's `0x400` limit.

## 5517 Game Packet Limit

Native 5517 client receive behavior establishes a stricter game-packet limit:

```
0x400 bytes
1024 bytes
```

Critically, this limit applies to the **header-declared TQ packet length**.

It includes:

```
4-byte TQ header
packet payload
```

It does not include the post-handshake eight-byte protocol signature trailer.

Therefore, when the trailer-bearing game transport mode is active:

```
maximum declared TQ packet
    0x400 bytes

encrypted signature trailer
    0x008 bytes

maximum corresponding stream unit
    0x408 bytes
```

The client's receive path validates the header-declared packet size against `1024`, then separately
accounts for the eight-byte trailer.

This distinction matters because treating `0x400` as the entire encrypted transport unit would
incorrectly reduce the usable TQ packet size by eight bytes.

The generic framing implementation does not hard-code `0x400`.

The future game protocol/session boundary should provide that value when the relevant transport
state is implemented.

## Signature Trailer Boundary

Post-handshake game traffic may carry an eight-byte protocol signature trailer associated with the
packet in the encrypted stream.

Verified client behavior uses:

```
TQServer
    expected on server-to-client traffic

TQClient
    produced on client-to-server traffic
```

The signature is part of the encrypted transport/session envelope, not part of the four-byte TQ
header's declared packet length.

`WireFrameEncoder` therefore does not append, reserve, encrypt, or validate that trailer.

That work belongs to the future game session/security boundary.

Conceptually:

```
TQ packet
├── 4-byte header
└── payload
        ↓
session/security processing
        ↓
encrypted packet + 8-byte signature material
```

The exact encryption and session-state behavior will be implemented only when that protocol slice is
audited.

## Login and Handshake Separation

Login and handshake traffic must not inherit the game packet limit merely because all of these flows
use network sockets.

Native evidence shows separate handshake framing and security behavior.

The generic framing layer therefore exposes mechanism:

```
four-byte TQ framing
UInt16 representable length
optional caller-supplied maximum
```

while the future login/game boundaries supply policy based on their own verified contracts.

## Outbound Packet Contract

Outbound packets implement `IPacket`.

The contract consists of:

```
PacketId
PayloadLength
WritePayload
```

A packet writes only its payload.

The common TQ header is owned by `WireFrameEncoder`.

```
packet-specific payload
        ↓
WireFrameEncoder
        ↓
common header + payload
```

This prevents individual packet implementations from independently reproducing framing rules.

## Metadata Snapshot

`WireFrameEncoder` reads `PacketId` and `PayloadLength` once before payload serialization begins.

Those captured values define the packet being encoded.

A packet changing the values returned by those properties later in the operation does not change the
metadata already selected by the encoder.

Packet ID validation is performed against the captured identifier.

Payload-length validation is performed against the captured payload length.

## Payload Length

`PayloadLength` is part of the `IPacket` contract and is enforced by the encoder.

The encoder gives `PacketWriter` exactly the declared payload capacity.

The outcomes are:

```
actual < declared
    -> rejected after serialization

actual > declared
    -> PacketWriter capacity failure

actual = declared
    -> accepted
```

A packet cannot silently extend the encoded packet beyond its declared payload size.

Negative declared payload lengths are rejected before destination memory is modified.

## Frame-Length Calculation

`GetFrameLength` applies the same complete-packet metadata rules needed for actual encoding.

It validates:

```
packet exists
maximum packet length is valid
packet identifier is nonzero
payload length is nonnegative
resulting packet length is within the selected maximum
```

The returned value is therefore suitable for sizing memory for a packet that the encoder itself
would accept based on metadata.

Payload serialization correctness is naturally validated only by `WriteFrame`.

## Destination Ownership

`WireFrameEncoder` writes into caller-owned memory.

Conceptually:

```
caller-owned destination
├── packet header
└── payload
```

The encoder does not allocate final packet memory.

`PacketWriter` receives only the payload region and does not own that memory.

This lets the future transport layer choose its concrete memory strategy without changing packet
serialization.

## Destination Capacity

The destination may be larger than the packet being produced.

Only the returned number of bytes belongs to the encoded packet.

Bytes after that region remain untouched.

If the destination is smaller than the required packet, encoding fails before destination memory is
modified.

Metadata validation also occurs before destination memory is modified.

Examples include:

```
PacketId == 0
PayloadLength < 0
declared packet exceeds selected maximum
```

## Encoding Order

The packet header is committed only after payload serialization succeeds.

The operation is:

```
read packet metadata
        ↓
validate packet identifier
        ↓
validate packet length
        ↓
validate destination
        ↓
serialize payload
        ↓
verify exact payload length
        ↓
write header
        ↓
success
```

This ordering prevents a valid-looking packet header from being exposed before its payload has been
successfully produced.

## Failure Cleanup

Once an attempted packet region has been selected, payload serialization is transactional over that
region.

If serialization fails:

```
attempted packet region cleared
exception propagated
```

Bytes beyond the attempted packet remain untouched.

Examples include:

- packet serializer throws;
- packet writes too few bytes;
- packet attempts to exceed its declared payload capacity.

This matters when caller-owned memory is reusable.

A failed packet must not leave stale or partially encoded protocol data behind.

Failures that occur before an attempted packet region is selected do not modify destination memory.

## Raw Header vs Complete-Packet Validation

Raw binary representation and complete-packet validity intentionally live at different levels.

For example:

```
WireFrameHeader
    can represent Length = 0
    can represent PacketId = 0

WireFrameEncoder
    requires total packet length >= 4
    requires PacketId != 0
```

This prevents low-level binary structures from accumulating higher-level protocol policy.

Future inbound framing should preserve the same separation.

A stream decoder may first parse a raw header, then apply complete-packet validation before exposing
the packet to packet-specific decoding.

## Protocol and Transport Boundary

Framing belongs to `OpenConquer.Protocol`, not `OpenConquer.Transport`.

Transport should provide ordered bytes and manage memory lifetime, buffering, backpressure, and
socket behavior.

Protocol determines what those bytes mean and which packet-level rules apply.

Transport should not contain rules such as:

```
packet 1004 means chat
the game client accepts a maximum declared packet size of 0x400
packet identifier zero is invalid
this field is a character identifier
```

Those are protocol concerns.

At the same time, session-security concerns such as encryption state and the encrypted eight-byte
game signature trailer should not be pushed into the generic four-byte frame encoder.

The networking boundary is documented in [Networking Architecture](../architecture/networking.md).

## Current Implementation Boundary

The framing foundation currently consists of:

```
Framing/
├── WireFrameHeader
└── WireFrameEncoder

Packets/
└── IPacket
```

Implemented now:

- raw four-byte TQ header representation;
- little-endian header read/write;
- outbound complete-packet encoding;
- nonzero packet-identifier enforcement;
- generic `UInt16` packet-length enforcement;
- caller-supplied maximum packet lengths;
- exact payload-length enforcement;
- deterministic failure cleanup.

Not yet implemented:

- inbound stream packet extraction;
- game-session selection of the `0x400` compatibility limit;
- encrypted `TQServer` / `TQClient` signature handling;
- login/game handshake framing;
- protocol cryptography;
- packet-family dispatch.

Those future slices should reuse this foundation rather than collapsing their policy back into
generic framing.

## Tested Invariants

The current protocol tests verify:

- exact four-byte header representation;
- little-endian length and packet identifier;
- raw-header independence from complete-packet policy;
- rejection of packet identifier `0` by complete-frame encoding;
- zero-length payload packets;
- maximum `UInt16` packet;
- rejection above the generic maximum;
- caller-provided maximum packet lengths;
- exact `0x400` caller-supplied boundary behavior;
- insufficient destination behavior;
- payload underwrite detection;
- payload overwrite prevention;
- packet-region clearing after serialization failure;
- header commit only after successful payload serialization;
- packet metadata read once per encoding operation;
- pre-write metadata failures leaving destination memory unchanged.

The `0x400` tests establish that the generic encoder can enforce that boundary when supplied by a
caller.

They do not imply that a GameServer/session boundary selecting that limit has already been
implemented.

Tests should remain focused on externally meaningful framing behavior, compatibility invariants, and
ownership guarantees rather than implementation trivia.
