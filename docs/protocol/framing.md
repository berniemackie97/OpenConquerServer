# Framing

`OpenConquer.Protocol.Framing` reads and writes the four-byte frame header shared by every packet.
Encryption and the GameServer signature trailer wrap frames from the outside. The login and game
session layers handle them.

## Header

| Offset | Size | Field                                 |
| -----: | ---: | ------------------------------------- |
|      0 |    2 | Total frame length, including header  |
|      2 |    2 | Packet ID                             |

Both fields are little-endian `UInt16`. For example, `06 00 78 56 AA BB` is a six-byte frame with
packet ID `0x5678` and payload `AA BB`.

`WireFrameHeader` represents the raw four bytes and can hold any value, including zero length or
packet ID zero. Validity rules belong to the decoder and encoder, which require a length of at
least 4 and a nonzero packet ID.

## Limits

The header allows frames up to 65,535 bytes (a 65,531-byte payload). Both the encoder and decoder
accept a lower maximum from the caller. The AccountServer passes 524; the GameServer passes 1,024
through `GameWireFrameEncoder` and `GameWireProtocol`.

On the GameServer the 1,024-byte limit applies to the frame itself. The eight-byte signature that
follows each encrypted frame is not counted, so a full frame occupies 1,032 bytes on the wire.
Counting the signature inside the limit would wrongly shrink the largest payload by eight bytes.

## Decoding

`WireFrameDecoder` inspects the start of a caller-owned `ReadOnlySequence<byte>` and returns one of:

| Result               | Meaning                                                        |
| -------------------- | -------------------------------------------------------------- |
| `IncompleteHeader`   | Fewer than four bytes are available                            |
| `InvalidFrameLength` | Declared length is below 4 or above the caller's maximum       |
| `IncompleteFrame`    | The declared length is valid but not all bytes have arrived    |
| `InvalidPacketId`    | The whole frame is available and its packet ID is zero         |
| `Success`            | The whole frame is available and valid                         |

An impossible length is rejected as soon as the header arrives, so a peer cannot make the server
wait for a payload it would refuse anyway. The packet ID is checked only once the whole frame is
present.

The decoder never consumes input. On success it returns a slice that covers the declared frame and
nothing more. The slice may span several buffer segments and borrows the caller's memory. The
caller advances its buffer after it has finished with the frame. Only a header that straddles
segments is copied (to the stack). The frame body is never copied.

## Encoding

Outbound packets implement `IPacket`:

```csharp
public interface IPacket
{
    ushort PacketId { get; }
    int PayloadLength { get; }
    void WritePayload(ref PacketWriter writer);
}
```

A packet writes only its payload, and `WireFrameEncoder` owns the header. The encoder reads
`PacketId` and `PayloadLength` once and validates them and the destination size. It then gives the
packet a `PacketWriter` limited to the declared payload length, and writes the header only after
the payload succeeds.

| Situation                                     | Result                                           |
| --------------------------------------------- | ------------------------------------------------ |
| Packet ID zero, negative length, or too large | Rejected before the destination is touched       |
| Destination too small                         | Rejected before the destination is touched       |
| Payload writes fewer bytes than declared      | Rejected; the attempted frame region is zeroed   |
| Payload writes more bytes than declared       | Writer capacity error; the region is zeroed      |
| Payload serializer throws                     | Exception propagates; the region is zeroed       |

Zeroing the attempted region keeps reused buffers from exposing a stale or partial frame. Bytes
after the frame are never touched. `GetFrameLength` applies the same metadata checks, so callers can
size buffers for the frames the encoder will accept.
