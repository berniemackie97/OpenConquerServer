# TQ Text Encoding

This document records the text-encoding and string-field behavior currently implemented by
`OpenConquer.Protocol`.

For the protocol documentation index, see [README](README.md).

## Scope

The shared protocol foundation currently supports three explicit text modes:

```text
TqTextEncoding.Ansi
TqTextEncoding.StrictAnsi
TqTextEncoding.Ascii
```

They are mapped internally to the corresponding .NET encodings.

Packet serialization does not accept arbitrary .NET `Encoding` instances, and the concrete runtime
encoding objects are not part of the public protocol API.

This keeps the shared wire API limited to encodings established by current Conquer Online 5517
protocol evidence.

## TqTextEncoding

`TqTextEncoding` identifies the text encodings supported by the shared protocol serializer.

```text
Ansi
StrictAnsi
Ascii
```

Unknown enum values are rejected with `ArgumentOutOfRangeException`.

The selector is intentionally small. New values should only be added when verified protocol evidence
requires another shared wire encoding.

## Runtime Encoding Mapping

The serializer internally maps each `TqTextEncoding` value to its corresponding runtime encoding.

The current mappings are:

```text
TqTextEncoding.Ansi
    -> Windows-1252 with the runtime's default code-page fallback

TqTextEncoding.StrictAnsi
    -> Windows-1252 with exception fallbacks

TqTextEncoding.Ascii
    -> seven-bit ASCII
```

The concrete encoding objects and resolver are internal implementation details of
`OpenConquer.Protocol`.

Callers select only `TqTextEncoding`.

## Windows-1252

The normal TQ text encoding is Windows-1252:

```text
Code page: 1252
```

For example:

```text
€ -> 80
```

`TqTextEncoding.Ansi` uses the runtime's default .NET Windows-1252 code-page fallback behavior.

An unsupported outbound character is therefore handled according to the runtime's default
Windows-1252 fallback policy. The protocol layer does not strengthen that behavior into a
replacement-only contract.

For example, an unrepresentable character such as:

```text
漢
```

is encoded as:

```text
?
```

under the current runtime fallback behavior.

That example records observable behavior for this input. It is not a guarantee that every
unrepresentable character is always handled by simple replacement.

## Strict Windows-1252

`TqTextEncoding.StrictAnsi` uses Windows-1252 with exception fallbacks.

Supported characters encode normally:

```text
€ -> 80
```

Unsupported characters fail rather than being silently handled by a fallback.

For example, a character outside Windows-1252 causes an `EncoderFallbackException`.

Strict encoding is appropriate for fields whose wire contract must reject unrepresentable text.

## ASCII

`TqTextEncoding.Ascii` selects seven-bit ASCII using the runtime's standard ASCII fallback behavior.

ASCII is used only where protocol evidence establishes ASCII semantics.

It is not treated as the default merely because a particular value happens to contain ordinary Latin
characters.

## Default Encoding

The shared string helpers default to:

```text
TqTextEncoding.Ansi
```

Therefore these are equivalent:

```text
writer.WriteFixedString(value, width);

writer.WriteFixedString(
    value,
    width,
    TqTextEncoding.Ansi
);
```

and:

```text
reader.ReadByteString();

reader.ReadByteString(
    TqTextEncoding.Ansi
);
```

Call sites should specify another selector only when the field contract requires it.

## Encoded Length

Protocol string widths and prefixes are measured in encoded bytes.

They are not measured in:

```text
string.Length
Unicode scalar values
grapheme clusters
```

The selected protocol encoding determines the wire byte count.

For the currently supported single-byte encodings, one representable character normally occupies one
byte, but code still derives wire lengths from the encoding rather than assuming character count.

## Fixed-Width Strings

A fixed-width TQ string occupies exactly its declared number of bytes.

Writing:

```text
ABC
```

into an eight-byte field produces:

```text
41 42 43 00 00 00 00 00
```

Unused bytes are explicitly zero-filled.

Existing caller-owned memory is never allowed to become implicit padding.

## Fixed-Width Reading

The reader requires the complete fixed-width field before consuming it.

Within that field, the first `0x00` terminates the decoded value.

For example:

```text
41 42 00 43 00
```

returns:

```text
AB
```

while consuming all five bytes.

If no zero byte exists, the entire field is decoded.

Conceptually:

```text
fixed field
    ↓
locate first 0x00
    ↓
decode preceding bytes
    ↓
consume complete field width
```

## Fixed-Width Null Rule

`0x00` is structural in a fixed-width TQ string.

A source value containing an embedded null character is therefore rejected.

For example:

```text
"A\0B"
```

is invalid for `WriteFixedString`.

Without this rule, the writer could emit bytes that the matching reader would interpret as a shorter
value.

The supported Windows-1252 and ASCII encodings preserve a literal source null as `0x00`, so
validating the source value is sufficient for the currently exposed encoding set.

## Fixed-Width Capacity

The complete encoded value must fit within the declared field width.

If:

```text
EncodedByteCount > Width
```

the operation fails with `ArgumentOutOfRangeException`.

The generic writer does not truncate automatically.

Truncation is a field-specific protocol or gameplay policy and must be decided by the owner of that
field.

## Fixed-Width Empty Values

An empty value fills the field with zero padding.

For example, an empty four-byte field becomes:

```text
00 00 00 00
```

An empty value is also valid for a zero-width field.

## Byte-Length-Prefixed Strings

Another TQ string format stores the encoded length in one unsigned byte.

Example:

```text
03 41 42 43
```

represents:

```text
ABC
```

The first byte is the encoded value length.

## Byte-String Length Limit

Because the prefix is one byte, the maximum encoded value length is:

```text
255 bytes
```

An empty value is represented as:

```text
00
```

A value requiring more than 255 encoded bytes is rejected.

## Embedded Nulls in Byte Strings

Byte-length-prefixed strings may contain embedded `0x00` bytes.

For example:

```text
03 41 00 42
```

is structurally valid.

The explicit length prefix already defines the field boundary, so a zero byte inside the value does
not act as a terminator.

This differs intentionally from fixed-width strings:

```text
fixed-width string
embedded source null
    -> rejected

byte-length-prefixed string
embedded source null
    -> valid
```

## PacketReader Semantics

`PacketReader` exposes:

```text
ReadFixedString
ReadByteString
```

Both accept a `TqTextEncoding` selector.

The selector is resolved before field bytes are consumed.

Therefore an unknown selector fails without advancing the reader.

The reader also preserves its cursor when a field cannot be read completely.

Conceptually:

```text
position = N

attempt field
    ↓
failure

position = N
```

This applies to:

- invalid encoding selector
- fixed-width underflow
- missing byte-string length prefix
- truncated byte-string payload

Successful operations consume the complete field.

The current closed decoder set does not rely on synthetic invalid Windows-1252 or ASCII byte cases
to define reader failure behavior.

## PacketWriter Semantics

`PacketWriter` exposes:

```text
WriteFixedString
WriteByteString
```

Both accept a `TqTextEncoding` selector and default to `Ansi`.

Validation that can reject the operation is performed before the writer commits the field.

Examples include:

- null value
- negative fixed width
- embedded null in a fixed-width source value
- unknown encoding selector
- encoded fixed value wider than the field
- byte-string value longer than 255 bytes
- insufficient remaining capacity

These failures leave the committed writer position unchanged.

Failures detected before destination memory is claimed also leave caller memory unchanged.

## Encoding Failure

Encoding byte count is determined before destination memory is claimed.

This means strict encoding failures for unsupported characters occur before the writer modifies the
target field.

The supported encoding set is closed and internally controlled. Packet callers cannot inject custom
`Encoding` implementations.

Once an encoded byte count has been successfully determined and sufficient destination capacity has
been established, the serializer writes the value using that same encoding.

Previously committed bytes remain intact when a later write fails validation.

## Zero Padding and Reserved Bytes

Fixed-width string padding is always explicit zero padding.

The same deterministic-memory principle is used by:

```text
PacketWriter.Reserve
```

Reserved bytes are cleared rather than skipped.

This prevents stale caller-owned or reusable memory from becoming observable protocol data.

## Truncation Policy

The shared protocol serialization layer does not automatically truncate strings.

Whether a particular field should:

```text
reject
truncate
normalize
transform
```

depends on the verified contract for that field.

Legacy truncation helpers are evidence only for the specific call sites that used them.

Generic wire serialization does not make that policy decision.

## Adding Another Encoding

A new shared encoding should not be introduced merely because .NET supports it.

Adding another protocol encoding requires evidence that a 5517 wire field actually uses it.

The required change would include:

1. adding an explicit `TqTextEncoding` value;
2. adding its canonical internal runtime mapping;
3. establishing the field semantics from protocol evidence;
4. adding reader/writer tests for its observable wire behavior.

This keeps the wire API closed over known protocol behavior rather than exposing arbitrary encoding
extensibility.

## Current Types

The public text selector is:

```text
TqTextEncoding
```

`PacketReader` and `PacketWriter` consume that selector at the serialization boundary.

The concrete .NET encoding objects and their resolver remain internal implementation details.

The public string operations are:

```text
PacketReader
├── ReadFixedString
└── ReadByteString

PacketWriter
├── WriteFixedString
└── WriteByteString
```

## Tested Invariants

The current tests verify:

- observable Windows-1252 wire behavior
- Windows-1252 euro encoding
- observable default ANSI fallback behavior
- strict Windows-1252 behavior
- ASCII behavior
- observable mapping of each supported `TqTextEncoding`
- rejection of unknown `TqTextEncoding` values
- default ANSI behavior
- fixed-width zero padding
- complete fixed-width field consumption
- embedded-null rejection for fixed-width strings
- byte-length prefixes
- maximum 255-byte string values
- embedded nulls in byte-length strings
- strict encoding failure
- reader cursor atomicity
- writer capacity atomicity
- caller memory preservation on pre-write failure

These tests define the supported shared text serialization contract.
