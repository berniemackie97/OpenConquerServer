# Conquer Online 5517 Protocol

This directory documents the wire behavior OpenConquer must preserve for compatibility with the
Conquer Online 5517 client.

Protocol documentation covers externally observable behavior such as framing, byte order, field
layouts, text encoding, limits, handshakes, cryptography, and compatibility quirks.

Internal server architecture is documented separately under
[`docs/architecture`](../architecture/README.md).

## Current Implementation

| Area | Implemented coverage |
| --- | --- |
| Framing | Four-byte header, segmented decoder, bounded encoder, complete-frame validation. |
| Serialization/text | Caller-owned readers/writers; ANSI, strict ANSI, ASCII; fixed and byte-prefixed strings. |
| Login cryptography | Independent inbound/outbound legacy-TQ stream state; seed-derived RC5-32/12/16 key and credential decryption; signed-account-byte keypad permutation. |
| Login packets | Server seed 1059 (8 bytes), standard client credentials 1060 (276 bytes), server authentication response 1055 (36 bytes), client MAC report 1100 (52 bytes), AccountServer resource report 1052 (28 bytes). |
| Login limits | 524-byte complete-frame ceiling for the audited login packet set; packet-specific exact lengths are checked separately. |

`LoginAccountRequest` owns disposable password memory. Username normalization and credential
length policy are enforced after wire decoding by Application using Domain rules. Trimming the
account before the keypad step would change its seed and break compatibility.

The concrete runtime encoding resolver and CRT random generator remain internal to Protocol.

The 524-byte ceiling accommodates the verified 1084 frame size; it does **not** mean 1084 is
implemented. Only standard 1060 credential decoding is supported. OEM, protected/mobile, Facebook,
and custom registration variants are not implemented. Nor are GameServer DH/CAST5, signatures,
login proofs, or gameplay packets. The two context-dependent 1052 forms must not be interchanged.

AccountServer already integrates the login cipher and framed I/O with Transport pipelines, sends
the seed, decodes requests, and validates the ordered post-authentication report pair. These are
components, not a composed runnable authentication host. See
[Networking Architecture](../architecture/networking.md) and the
[baseline audit](../audits/main-rebaseline.md) for evidence and scope.

## Reference

### [TQ Framing](framing.md)

The framing reference owns the detailed contract for:

- the 4-byte common TQ header
- little-endian header fields
- raw-header versus complete-packet validation
- nonzero complete-packet identifiers
- the generic `UInt16` packet-length limit
- caller-supplied packet-length limits
- the 5517 game-client `0x400` packet boundary
- the separate 8-byte game signature trailer
- `IPacket`
- payload-length enforcement
- caller-owned frame memory
- frame commit ordering
- serialization failure cleanup
- Protocol/Transport framing ownership

### [TQ Text Encoding](encoding.md)

The encoding reference owns the detailed contract for:

- `TqTextEncoding`
- Windows-1252
- strict Windows-1252
- ASCII
- fixed-width strings
- byte-length-prefixed strings
- zero padding
- embedded-null semantics
- reader and writer failure behavior
- field-level truncation policy

Additional protocol documents should be introduced only when an implemented or verified
compatibility boundary becomes large enough to justify one.

## Core Wire Rules

### Byte Order

Protocol integers currently supported by the shared serializer use little-endian byte order.

```text
UInt16 0x1234
-> 34 12

UInt32 0x89ABCDEF
-> EF CD AB 89
```

Primitive APIs are added from actual protocol evidence rather than made artificially symmetrical.

### Common TQ Header

A common TQ packet begins with:

| Offset | Size | Field               |
| -----: | ---: | ------------------- |
|      0 |    2 | Total packet length |
|      2 |    2 | Packet identifier   |

The declared length includes the four-byte header:

```text
PacketLength = 4 + PayloadLength
```

The raw header representation does not itself decide whether the values describe a valid complete
packet.

Complete-packet validation belongs to the framing boundary.

See [TQ Framing](framing.md).

### Packet Identifier

Native 5517 validation establishes packet identifier `0` as invalid for a complete TQ packet.

The rule is intentionally not embedded in `WireFrameHeader`.

`WireFrameEncoder` rejects packet identifier `0` before modifying destination memory.

`WireFrameDecoder` returns `IncompleteFrame` until the complete declared packet is buffered, then
returns `InvalidPacketId` when that complete packet has identifier `0`.

### Generic Packet Length

The common packet-length field is a `UInt16`.

The maximum representable header-declared TQ packet size is therefore:

```text
65535 bytes
```

That corresponds to a maximum payload of:

```text
65531 bytes
```

This is the generic framing limit.

It is not automatically the valid maximum for every protocol path.

### 5517 Game Packet Limit

Native 5517 client behavior establishes a stricter game-packet boundary:

```text
0x400 bytes
1024 bytes
```

This value applies to the **header-declared TQ packet**:

```text
4-byte header
+
payload
```

It does not include the separate eight-byte signature trailer used by the post-handshake encrypted
game protocol.

When that trailer is present, the corresponding stream unit may therefore occupy:

```text
0x400-byte TQ packet
+
0x008-byte signature material
=
0x408 bytes
```

The generic framing layer does not hard-code the `0x400` limit.

Instead, the generic framing APIs accept a caller-supplied maximum so the future game-session
boundary can select `0x400` when that protocol slice is implemented.

The existence of caller-supplied limits today does **not** mean the GameServer session boundary is
already implemented.

The implemented account-login path selects its separate 524-byte complete-frame ceiling.

## Protocol Boundary

`OpenConquer.Protocol` owns the interpretation and production of Conquer wire data.

It owns concepts such as:

```text
packet identifiers
packet layouts
framing
serialization
text encoding
handshakes
protocol cryptography
wire compatibility
```

It does not own:

```text
TCP sockets
connection lifetime
transport buffering
backpressure
database access
gameplay state
```

The transport side of this boundary is documented in
[Networking Architecture](../architecture/networking.md).

The distinction is:

```text
Protocol
"What do these bytes mean?"

Transport
"How do these bytes move?"
```

## Memory Ownership

Protocol serializers operate on caller-owned memory.

`PacketWriter` does not:

- allocate frame memory
- rent arrays
- grow its destination
- own the backing buffer
- dispose caller memory

Transport and AccountServer select their buffering strategy independently; the current login
session integrates these APIs with `System.IO.Pipelines`.

Borrowed protocol memory must not outlive the lifetime guaranteed by its owner.

`WireFrameDecoder` likewise does not own inbound memory.

It accepts a caller-owned `ReadOnlySequence<byte>` and, on success, returns a borrowed slice
covering exactly the first complete TQ frame.

The decoder does not:

- allocate frame memory
- coalesce segmented payloads
- advance a transport buffer
- retain the supplied sequence
- perform network I/O

The returned frame must not outlive the source memory supplied by the caller.

## PacketReader

`PacketReader` is a non-owning cursor over caller-provided protocol bytes.

The current primitive read surface is intentionally limited to proven protocol requirements:

```text
Byte
UInt16
UInt32
Raw bytes
```

String reads are:

```text
ReadFixedString
ReadByteString
```

Both accept `TqTextEncoding`.

Successful field reads consume exactly the bytes belonging to that field.

Failures that occur before a field is successfully consumed leave the cursor at its previous
position.

Current meaningful examples include:

```text
primitive underflow
fixed-width underflow
truncated byte-length string
unknown TqTextEncoding
```

The current closed decoder set does not rely on a synthetic invalid Windows-1252 byte case to define
reader behavior.

## PacketWriter

`PacketWriter` is a non-owning fixed-capacity writer over caller-owned memory.

The current primitive write surface is:

```text
Byte
UInt16
UInt32
UInt64
Raw bytes
Reserved bytes
```

String writes are:

```text
WriteFixedString
WriteByteString
```

Both accept `TqTextEncoding`.

The writer never expands its backing storage.

Validation occurs before a string field is committed.

Examples include:

- null source rejection
- invalid fixed-field width
- embedded null rejection for fixed-width strings
- encoded value wider than a fixed-width field
- encoded byte-string value longer than 255 bytes
- strict encoding rejection of an unrepresentable source value
- unknown `TqTextEncoding`

If a write exceeds remaining capacity, the operation fails without advancing its committed position.

Previously committed bytes are never rolled back by a later failed write.

## Failure Semantics

The binary foundation avoids exposing misleading partially committed protocol state.

Current guarantees include:

```text
PacketReader field failure
    -> reader position unchanged

PacketWriter validation failure
    -> writer position unchanged

PacketWriter capacity failure
    -> writer position unchanged

PacketWriter string encoding failure during pre-write validation
    -> destination unchanged
    -> writer position unchanged

WireFrameEncoder serialization failure
    -> attempted frame region cleared
```

`PacketWriter` does not claim a destination field and then promise generic rollback for arbitrary
encoding failures.

The supported encoding set is closed, string byte counts are established before destination memory
is selected, and the shared writer exposes only the protocol text modes justified by current
evidence.

Detailed guarantees belong in [TQ Framing](framing.md) and [TQ Text Encoding](encoding.md).

## Text Encoding

The public string API exposes only protocol text modes currently justified by 5517 evidence:

```text
TqTextEncoding.Ansi
TqTextEncoding.StrictAnsi
TqTextEncoding.Ascii
```

The protocol assembly maps those selectors internally to concrete runtime encodings.

Packet serialization does not accept arbitrary `System.Text.Encoding` instances.

This keeps the shared protocol surface closed over established wire behavior instead of exposing an
unjustified encoding extension point.

### ANSI

The default text mode is:

```text
TqTextEncoding.Ansi
```

It maps internally to Windows-1252 using the runtime's default code-page fallback behavior.

The API intentionally does not promise that the runtime fallback is a replacement-only strategy.

### Strict ANSI

```text
TqTextEncoding.StrictAnsi
```

maps to Windows-1252 using exception fallbacks.

Its important current distinction is outbound encoding: source characters that cannot be represented
in Windows-1252 are rejected rather than silently transformed through the default fallback behavior.

### ASCII

```text
TqTextEncoding.Ascii
```

maps to seven-bit ASCII using the runtime's standard ASCII fallback behavior.

It is used only where protocol evidence establishes an ASCII field.

See [TQ Text Encoding](encoding.md) for the detailed string contract.

## Fixed-Width Strings

A fixed-width TQ string occupies exactly its declared field width.

Unused bytes are zero-filled.

When reading, the first `0x00` terminates the logical value while the reader still consumes the
entire field width.

An outbound fixed-width source may not contain an embedded null character:

```text
"A\0B"
    -> rejected
```

The generic writer does not truncate a value that exceeds the field width.

Field-specific truncation belongs to the owner of that packet field and must be justified by packet
evidence.

## Byte-Length-Prefixed Strings

Byte-length-prefixed TQ strings use one unsigned byte for the encoded value length.

The maximum encoded value length is therefore:

```text
255 bytes
```

The prefix counts encoded bytes, not .NET characters.

Unlike fixed-width strings, embedded null bytes are valid because the explicit prefix determines the
field boundary.

## Reserved Bytes

Protocol-reserved bytes are deterministic.

`PacketWriter.Reserve` clears reserved memory rather than merely advancing across caller-owned
storage.

Unused bytes in fixed-width string fields are likewise cleared.

This prevents stale contents from reusable buffers from appearing on the wire.

## Truncation and Transformation

Generic protocol utilities do not silently:

```text
truncate
normalize
sanitize
transform
```

string values unless that behavior is intrinsic to the wire format itself.

Those decisions belong to the packet, application, or gameplay rule that owns the field.

Legacy helpers are evidence only for the specific call sites that used them.

## Outbound Packet Contract

Outbound packets implement `IPacket`.

The contract consists of:

```text
PacketId
PayloadLength
WritePayload
```

Packets serialize only their payload.

They do not reproduce the common TQ header themselves.

```text
packet payload
      ↓
WireFrameEncoder
      ↓
common TQ header + payload
```

`WireFrameEncoder` snapshots `PacketId` and `PayloadLength` before payload serialization begins.

It validates the captured packet identifier and declared payload length before frame construction.

The packet then receives exactly its declared payload capacity.

Therefore:

```text
actual payload < declared payload
    -> rejected

actual payload > declared payload
    -> bounded PacketWriter rejects the write

actual payload = declared payload
    -> accepted
```

A complete packet with:

```text
PacketId == 0
```

is rejected before destination memory is modified.

See [TQ Framing](framing.md).

## Frame Limits

`WireFrameEncoder` and `WireFrameDecoder` support two framing modes:

```text
generic framing
    -> maximum UInt16 packet length

caller-constrained framing
    -> lower maximum supplied by the protocol path
```

This distinction is important because the common header representation and a path-specific client
compatibility limit are not the same thing.

The current foundation can enforce `0x400` when explicitly supplied by a caller.

The future game-session boundary is responsible for actually selecting that value for 5517 game
traffic.

Generic framing must not silently impose the game client's `0x400` limit on unrelated protocol
paths.

## Frame Commit Ordering

The outbound encoder serializes the payload before committing the common frame header.

Conceptually:

```text
capture packet metadata
    ↓
validate packet identifier and length
    ↓
validate destination
    ↓
serialize payload
    ↓
verify exact payload length
    ↓
write common frame header
```

A payload serialization failure therefore cannot leave a valid-looking header in front of an
incomplete payload.

If frame construction fails after the attempted frame region has been selected, that region is
cleared.

Bytes beyond the attempted frame remain untouched.

## Compatibility Evidence

Protocol behavior is established from evidence rather than assumptions.

Useful evidence includes:

- legacy OpenConquer packet implementations
- legacy framing and parsing code
- observed 5517 client behavior
- packet captures
- static client analysis
- original client data where relevant

Evidence may establish:

```text
packet identifier
field offset
field width
byte order
encoding
packet limit
cipher behavior
signature behavior
```

It does not automatically justify retaining legacy:

```text
class structure
buffer ownership
threading model
socket abstractions
dependency layout
```

OpenConquerPublic is the working server being ported. Preserve its proven behavior unless a
verified native requirement or a documented production improvement justifies a deviation.
Inventory the complete relevant legacy subsystem before changing its design. Native 5517 evidence
has final authority for client-visible behavior.

## Adding Protocol APIs

Shared protocol APIs should be introduced only when evidence demonstrates a real wire requirement.

Do not add operations merely for symmetry or convenience.

For example:

```text
PacketWriter
    Byte
    UInt16
    UInt32
    UInt64

PacketReader
    Byte
    UInt16
    UInt32
```

The writer currently needs `UInt64`; the reader does not.

That asymmetry is intentional until inbound protocol evidence requires otherwise.

The same rule applies to text encodings.

A new text mode requires:

1. verified protocol evidence;
2. a new `TqTextEncoding` value;
3. a canonical internal runtime mapping;
4. tests for its observable wire behavior.

## Protocol and Transport

Outbound framing operates on caller-owned contiguous destination memory.

Inbound framing accepts caller-owned `ReadOnlySequence<byte>` input. This matches segmented
buffering such as `System.IO.Pipelines` without requiring Protocol to own a `PipeReader`, socket,
pool, or other transport resource.

Transport remains free to use:

```text
System.IO.Pipelines
pooled memory
socket buffers
other bounded buffering strategies
```

provided ownership and lifetime rules are respected.

The decoder interprets the first frame represented by the supplied bytes but does not advance or
consume the transport buffer itself. On success, the returned frame boundary gives the caller the
position through which it may advance its own buffer.

Post-handshake game signature handling remains outside the generic four-byte framing layer.
