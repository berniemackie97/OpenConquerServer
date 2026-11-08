using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameMissionCatalogReportTests
{
    private const int RecordsOffset = 8;
    private const int RecordLength = 12;

    [Fact]
    public void PacketConstants_MatchClientBuilder()
    {
        Assert.Equal((ushort)1134, GameMissionCatalogReport.PacketIdentifier);
        Assert.Equal((ushort)3, GameMissionCatalogReport.CatalogSubtype);
        Assert.Equal(80, GameMissionCatalogReport.MaximumMissionsPerFrame);
        Assert.Equal(968, GameMissionCatalogReport.GetPacketLength(GameMissionCatalogReport.MaximumMissionsPerFrame));
    }

    [Fact]
    public void TryParse_IndependentlyCapturedCatalogFramesInOrder()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "WireGolden", "1134-mission-catalog.observed.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.Equal("20_notes/Network/evidence/cp32-world-entry-2026-11-08/capture.json, capture 2",
            document.RootElement.GetProperty("source").GetString());
        int sequence = 13;
        int previousMissionId = 0;
        int total = 0;

        foreach (JsonElement sample in document.RootElement.GetProperty("frames").EnumerateArray())
        {
            Assert.Equal(sequence++, sample.GetProperty("sequence").GetInt32());
            byte[] observed = Convert.FromBase64String(sample.GetProperty("base64").GetString()!);
            Assert.Equal(sample.GetProperty("sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(observed)).ToLowerInvariant());

            int count = sample.GetProperty("count").GetInt32();
            Assert.Equal(GameMissionCatalogReport.GetPacketLength(count), observed.Length);
            using GameInboundFrame frame = CreateFrame(observed);
            Assert.True(GameMissionCatalogReport.TryParse(
                frame, out GameMissionCatalogReport report, out GameMissionCatalogReportParseError error));
            Assert.Equal(GameMissionCatalogReportParseError.None, error);
            Assert.Equal(count, report.MissionCount);
            Assert.True(report.FirstMissionId > previousMissionId);
            previousMissionId = report.LastMissionId;
            total += report.MissionCount;
        }

        Assert.Equal(16, sequence);
        Assert.Equal(224, total);
        Assert.Equal(20001, previousMissionId);
    }

    [Fact]
    public void TryParse_FullFrame_MatchesTheRecordedRetailHeaderAndReadsTheIdRange()
    {
        byte[] packet = Build(Enumerable.Range(600, GameMissionCatalogReport.MaximumMissionsPerFrame).ToArray());

        // First twelve bytes of the frame the retail client sent on 2026-10-25: length 968, type 1134, subtype 3, 80 records, MissionId 600.
        Assert.Equal("C8036E040300500058020000", Convert.ToHexString(packet.AsSpan(0, 12)));

        using GameInboundFrame frame = CreateFrame(packet);

        Assert.True(GameMissionCatalogReport.TryParse(frame, out GameMissionCatalogReport report, out GameMissionCatalogReportParseError error));
        Assert.Equal(GameMissionCatalogReportParseError.None, error);
        Assert.Equal(new GameMissionCatalogReport(80, 600, 679), report);
        Assert.True(report.IsFull);
    }

    [Theory]
    [InlineData(new[] { 20001 })]
    [InlineData(new[] { 1, 2, int.MaxValue })]
    public void TryParse_PartialFrame_ReadsTheIdRange(int[] missionIds)
    {
        using GameInboundFrame frame = CreateFrame(Build(missionIds));

        Assert.True(GameMissionCatalogReport.TryParse(frame, out GameMissionCatalogReport report, out GameMissionCatalogReportParseError error));
        Assert.Equal(GameMissionCatalogReportParseError.None, error);
        Assert.Equal(new GameMissionCatalogReport(missionIds.Length, missionIds[0], missionIds[^1]), report);
        Assert.False(report.IsFull);
    }

    [Fact]
    public void TryParse_OtherPacketId_ReturnsInvalidPacketId()
    {
        byte[] packet = Build([600]);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 1135);

        AssertRejected(packet, GameMissionCatalogReportParseError.InvalidPacketId);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    public void TryParse_ShorterThanTheCatalogHeader_ReturnsInvalidPacketLength(int length)
    {
        byte[] packet = Build([600]).AsSpan(0, length).ToArray();
        WireFrameHeader.Write(packet, checked((ushort)length), GameMissionCatalogReport.PacketIdentifier);

        AssertRejected(packet, GameMissionCatalogReportParseError.InvalidPacketLength);
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)1)]
    [InlineData((ushort)2)]
    [InlineData((ushort)4)]
    [InlineData((ushort)8)]
    [InlineData((ushort)9)]
    public void TryParse_OtherSubtype_ReturnsUnsupportedSubtype(ushort subtype)
    {
        byte[] packet = Build([600]);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), subtype);

        AssertRejected(packet, GameMissionCatalogReportParseError.UnsupportedSubtype);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(81)]
    public void TryParse_CountOutsideOneToEighty_ReturnsInvalidMissionCount(int count)
    {
        byte[] packet = Build(Enumerable.Range(600, count).ToArray());

        AssertRejected(packet, GameMissionCatalogReportParseError.InvalidMissionCount);
    }

    [Theory]
    [InlineData(-12)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(12)]
    public void TryParse_LengthNotMatchingTheCount_ReturnsInvalidPacketLength(int difference)
    {
        byte[] built = Build([600, 601, 602]);
        byte[] packet = new byte[built.Length + difference];
        built.AsSpan(0, Math.Min(built.Length, packet.Length)).CopyTo(packet);
        WireFrameHeader.Write(packet, checked((ushort)packet.Length), GameMissionCatalogReport.PacketIdentifier);

        AssertRejected(packet, GameMissionCatalogReportParseError.InvalidPacketLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void TryParse_NonPositiveMissionId_ReturnsInvalidMissionId(int missionId)
    {
        AssertRejected(Build([600, missionId, 602]), GameMissionCatalogReportParseError.InvalidMissionId);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void TryParse_NonZeroByteAfterTheMissionId_ReturnsNonZeroRecordField(int recordOffset)
    {
        byte[] packet = Build([600, 601, 602]);
        packet[RecordsOffset + RecordLength + recordOffset] = 1;

        AssertRejected(packet, GameMissionCatalogReportParseError.NonZeroRecordField);
    }

    [Theory]
    [InlineData(new[] { 600, 600 })]
    [InlineData(new[] { 600, 602, 601 })]
    public void TryParse_MissionIdsNotAscending_ReturnsMissionIdsNotAscending(int[] missionIds)
    {
        AssertRejected(Build(missionIds), GameMissionCatalogReportParseError.MissionIdsNotAscending);
    }

    private static void AssertRejected(byte[] packet, GameMissionCatalogReportParseError expected)
    {
        using GameInboundFrame frame = CreateFrame(packet);

        Assert.False(GameMissionCatalogReport.TryParse(frame, out GameMissionCatalogReport report, out GameMissionCatalogReportParseError error));
        Assert.Equal(expected, error);
        Assert.Equal(default, report);
    }

    /// <summary>
    /// Builds a frame the way the client's builder does: zeroed buffer, then type, subtype, count and one MissionId per record.
    /// </summary>
    private static byte[] Build(int[] missionIds)
    {
        int length = GameMissionCatalogReport.GetPacketLength(missionIds.Length);
        byte[] packet = new byte[length];

        WireFrameHeader.Write(packet, checked((ushort)length), GameMissionCatalogReport.PacketIdentifier);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), GameMissionCatalogReport.CatalogSubtype);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6), checked((ushort)missionIds.Length));

        for (int index = 0; index < missionIds.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(RecordsOffset + (index * RecordLength)), missionIds[index]);
        }

        return packet;
    }

    private static GameInboundFrame CreateFrame(byte[] packet)
    {
        Assert.True(WireFrameHeader.TryRead(packet, out WireFrameHeader header));

        byte[] rented = ArrayPool<byte>.Shared.Rent(packet.Length);
        packet.CopyTo(rented, 0);

        return new GameInboundFrame(rented, header);
    }
}
