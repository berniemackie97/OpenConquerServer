# Text encoding

Packet strings are read and written by `PacketReader` and `PacketWriter`. Callers choose an encoding
with the `WireTextEncoding` enum; arbitrary `System.Text.Encoding` instances are not accepted, and
the mapping to runtime encodings is internal.

| `WireTextEncoding` | Encoding                                                    |
| ------------------ | ----------------------------------------------------------- |
| `Ansi` (default)   | Windows-1252 with .NET's default replacement fallback       |
| `StrictAnsi`       | Windows-1252; unrepresentable characters throw              |
| `Ascii`            | 7-bit ASCII                                                 |

Use `StrictAnsi` for fields that must reject characters the client cannot display, such as
friend and syndicate names. Use `Ascii` only where a field is defined as ASCII, such as IP
addresses and MAC address text. Field widths and length prefixes are always counted in encoded
bytes, not characters.

## Fixed-width strings

A fixed-width field occupies exactly its declared number of bytes.

- **Writing** pads unused bytes with zeros. A value containing a null character is rejected,
  because the reader would treat it as the end of the string. A value that does not fit is
  rejected; truncation is never automatic.
- **Reading** consumes the whole field and stops the string at the first zero byte. For example,
  `41 42 00 43 00` reads as `AB`.

## Length-prefixed strings

A one-byte length followed by that many encoded bytes, so values are limited to 255 bytes.
`03 41 42 43` is `ABC`; an empty string is `00`. Embedded zero bytes are allowed because the prefix
defines the end.

## Failure behavior

Both the reader and writer check everything they can before committing, so a failed call leaves
the position unchanged:

- The reader rejects an unknown encoding, a truncated field, or a missing prefix without advancing.
- The writer rejects null values, negative widths, embedded nulls, unknown encodings, values that
  are too long, and insufficient space without changing its position. The encoded byte count is
  computed before any destination memory is claimed, so a strict-encoding failure leaves the
  buffer untouched.

`PacketWriter.Reserve` zeroes the bytes it skips rather than leaving them as they were, for the same
reason padding is zeroed: reused buffers must not leak old data onto the wire.

## Adding an encoding

Add a `WireTextEncoding` value only when a real field requires it. The change needs the enum value,
its internal mapping, and reader and writer tests for the observable bytes.
