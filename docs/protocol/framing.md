# TQ Framing

This document records the framing behavior currently implemented by `OpenConquer.Protocol`.

It describes the common TQ frame header, inbound frame extraction, outbound frame encoding,
frame-size boundaries, packet payload contracts, memory ownership, and failure behavior.

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

The four-byte header and payload form the TQ packet handled by the generic framing layer.
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

WireFrameDecoder / WireFrameEncoder
    generic complete-packet validity

login/game protocol boundary
    path-specific compatibility rules
```

## Byte Order

Header integers use little-endian byte order.

For example:

```
UInt16 0x1234
    -> 34 12
```

The current primitive protocol serializers follow the same little-endian rule.

## Packet Identifier

Native 5517 packet validation requires the packet identifier to be nonzero for a complete TQ packet.

Therefore:

```text
PacketId != 0
```

is a complete-packet framing rule.

`WireFrameEncoder` rejects packet identifier `0` before destination memory is modified.

`WireFrameDecoder` does not reject the raw identifier until the complete header-declared packet is
available. For example:

```text
valid declared length
PacketId = 0
not all declared bytes buffered
    -> IncompleteFrame

valid declared length
PacketId = 0
all declared bytes buffered
    -> InvalidPacketId
```

This preserves the native distinction between inspecting a raw header and validating a complete
packet.

The rule remains outside `WireFrameHeader`, which may faithfully represent `PacketId = 0`.

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

This is the generic framing limit supported by the framing layer.
It is not automatically the valid maximum for every protocol path.

## Caller-Supplied Limits

`WireFrameEncoder` and `WireFrameDecoder` support a caller-supplied maximum packet length.

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

## Inbound Frame Extraction

`WireFrameDecoder` interprets the first TQ frame in caller-owned buffered input.

Its input is:

```text
ReadOnlySequence<byte>
```

rather than a socket, stream, `PipeReader`, pooled array, or transport connection.

This allows the framing layer to operate directly over segmented transport buffers without taking
ownership of transport mechanics.

### Decode Results

Inbound decoding produces one of five results:

```text
IncompleteHeader
    fewer than 4 bytes are available

InvalidFrameLength
    the complete header is available
    declared length is below 4
    or declared length exceeds the caller-selected maximum

IncompleteFrame
    the declared length is valid
    but fewer than the declared number of bytes are available

InvalidPacketId
    the complete declared frame is available
    but PacketId is 0

Success
    the first complete valid frame is available
```

Invalid declared lengths are rejected as soon as the complete four-byte header is available.

This is deliberate: a peer cannot force the server to wait for a payload whose declared size is
already impossible or exceeds the caller-selected compatibility limit.

Packet-identifier validation occurs only after all header-declared frame bytes are available because
the nonzero identifier rule belongs to complete-packet validation.

### Buffer Consumption

`WireFrameDecoder` does not consume or advance the supplied sequence.

On incomplete or invalid results:

```text
frame = empty
```

The parsed header is still returned whenever a complete four-byte header was available.

On success, the returned frame:

- begins at the start of the supplied sequence;
- contains exactly the header-declared number of bytes;
- excludes any bytes belonging to later coalesced frames;
- may span multiple sequence segments;
- borrows its memory from the supplied sequence.

A transport caller can therefore advance its own buffer through the returned frame's end only after
it has completed whatever protocol/session processing requires those bytes.

### Segmented Input

Neither the four-byte header nor the payload is required to occupy one contiguous memory segment.

A header may arrive conceptually as:

```text
segment 1    06
segment 2    00 78
segment 3    56 AA BB
```

and still represent:

```text
Length:     6
Packet ID:  0x5678
Payload:    AA BB
```

Only the four header bytes are copied to temporary stack memory when the header itself crosses a
segment boundary.

The complete returned frame is not coalesced or copied.

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

`WireFrameDecoder` preserves the same separation.

It first interprets the raw four-byte header, validates declared frame length, waits until the
complete declared frame exists, and only then applies the nonzero complete-packet identifier rule.

## Protocol and Transport Boundary

Framing belongs to `OpenConquer.Protocol`, not `OpenConquer.Transport`.

Transport should provide ordered bytes and manage memory lifetime, buffering, backpressure, and
socket behavior.

Protocol determines what those bytes mean and which packet-level rules apply.

`WireFrameDecoder` makes this boundary concrete by accepting a borrowed `ReadOnlySequence<byte>`
rather than depending on Transport or `System.IO.Pipelines` directly.

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
