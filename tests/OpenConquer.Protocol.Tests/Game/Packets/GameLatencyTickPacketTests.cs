using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameLatencyTickPacketTests
{
    [Fact]
    public void TryParse_AcceptsNativeHeartbeat_AndEchoesItByteForByte()
    {
        byte[] wire = CreateHeartbeat();
        using GameInboundFrame frame = CreateFrame(wire);

        Assert.True(GameLatencyTickPacket.TryParse(frame, out GameLatencyTickPacket? packet));
        Assert.Equal(GameLatencyTickPacket.PacketIdentifier, packet.PacketId);
        Assert.Equal(GameLatencyTickPacket.FixedPacketLength - WireFrameHeader.Size, packet.PayloadLength);

        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];
        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(GameLatencyTickPacket.FixedPacketLength, written);
        Assert.Equal(wire, destination);
    }

    [Fact]
    public void TryParse_EchoesCapturedClientHeartbeatUnchanged()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "WireGolden", "1009-27-heartbeat.observed.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement source = document.RootElement;

        Assert.Equal(1, source.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("observed", source.GetProperty("classification").GetString());
        Assert.True(source.GetProperty("runtimeCaptured").GetBoolean());
        Assert.Equal("game", source.GetProperty("protocol").GetString());
        Assert.Equal("client-to-server", source.GetProperty("direction").GetString());
        Assert.Equal(GameLatencyTickPacket.PacketIdentifier, source.GetProperty("packetId").GetUInt16());
        Assert.Equal(GameLatencyTickPacket.LatencyTickSubtype, source.GetProperty("subtype").GetUInt16());
        Assert.Equal("b6ba5ab8573ec073ea271f57b16fa14de1c67bfb44efd5aa7cfbfc0d3b488a62",
            source.GetProperty("referenceImageSha256").GetString());
        Assert.Equal("20_notes/Network/evidence/cp32-world-entry-2026-11-08/capture.json#capture-1-record-40",
            source.GetProperty("sourceEvidence").GetString());
        Assert.Equal("peer after CAST5-CFB decrypt and client trailer validation",
            source.GetProperty("capturePoint").GetString());

        byte[] observed = Convert.FromHexString(source.GetProperty("frameHex").GetString()!);
        const string digest = "aa2d5b328c0f90b9b80eea69f1e648ee33653fc9092ff633d72e4c9e05d4c922";
        Assert.Equal(digest, source.GetProperty("frameSha256").GetString());
        Assert.Equal(digest, Convert.ToHexString(SHA256.HashData(observed)).ToLowerInvariant());
        Assert.Equal(GameLatencyTickPacket.FixedPacketLength, observed.Length);
        Assert.Equal(observed.Length, BinaryPrimitives.ReadUInt16LittleEndian(observed));
        Assert.Equal(1_000_000u, BinaryPrimitives.ReadUInt32LittleEndian(observed.AsSpan(4)));
        Assert.Equal(68_952_244u, BinaryPrimitives.ReadUInt32LittleEndian(observed.AsSpan(16)));

        using GameInboundFrame frame = CreateFrame(observed);
        Assert.True(GameLatencyTickPacket.TryParse(frame, out GameLatencyTickPacket? tick));

        byte[] echo = new byte[GameWireFrameEncoder.GetFrameLength(tick)];
        Assert.Equal(observed.Length, GameWireFrameEncoder.WriteFrame(tick, echo));
        Assert.Equal(observed, echo);
    }

    [Fact]
    public void TryParse_CopiesPayload_SoTheEchoOutlivesTheReceiveBuffer()
    {
        byte[] wire = CreateHeartbeat();
        GameLatencyTickPacket? packet;
        using (GameInboundFrame frame = CreateFrame(wire))
        {
            Assert.True(GameLatencyTickPacket.TryParse(frame, out packet));
        }

        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];
        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(wire, destination);
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)26)]
    [InlineData((ushort)28)]
    [InlineData((ushort)46)]
    public void TryParse_RejectsOtherItemActionSubtypes(ushort subtype)
    {
        byte[] wire = CreateHeartbeat();
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(12), subtype);
        using GameInboundFrame frame = CreateFrame(wire);

        Assert.False(GameLatencyTickPacket.TryParse(frame, out GameLatencyTickPacket? packet));
        Assert.Null(packet);
    }

    [Fact]
    public void TryParse_RejectsOtherPacketTypes()
    {
        byte[] wire = CreateHeartbeat();
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(2), 1010);
        using GameInboundFrame frame = CreateFrame(wire);

        Assert.False(GameLatencyTickPacket.TryParse(frame, out _));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(84)]
    [InlineData(92)]
    public void TryParse_RejectsLengthsOtherThanTheNativeFrame(int length)
    {
        byte[] wire = new byte[length];
        BinaryPrimitives.WriteUInt16LittleEndian(wire, (ushort)length);
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(2), GameLatencyTickPacket.PacketIdentifier);
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(12), GameLatencyTickPacket.LatencyTickSubtype);
        using GameInboundFrame frame = CreateFrame(wire);

        Assert.False(GameLatencyTickPacket.TryParse(frame, out _));
    }

    [Fact]
    public void TryParse_RejectsNullFrame()
    {
        Assert.Throws<ArgumentNullException>(() => GameLatencyTickPacket.TryParse(null!, out _));
    }

    private static byte[] CreateHeartbeat()
    {
        // Fill unrelated payload bytes to verify the echo preserves them without interpretation.
        byte[] wire = new byte[GameLatencyTickPacket.FixedPacketLength];
        for (int index = WireFrameHeader.Size; index < wire.Length; index++)
        {
            wire[index] = (byte)(index * 7);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(wire, GameLatencyTickPacket.FixedPacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(2), GameLatencyTickPacket.PacketIdentifier);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(4), 1000001u);
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(12), GameLatencyTickPacket.LatencyTickSubtype);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(16), 0x0012D687u);
        return wire;
    }

    private static GameInboundFrame CreateFrame(byte[] packet)
    {
        Assert.True(WireFrameHeader.TryRead(packet, out WireFrameHeader header));

        byte[] buffer = ArrayPool<byte>.Shared.Rent(packet.Length);
        packet.CopyTo(buffer, 0);

        return new GameInboundFrame(buffer, header);
    }
}
