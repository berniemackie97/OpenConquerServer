using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class WireGoldenObservedClientFramesTests
{
    private const string EntryEvidence = "20_notes/Network/evidence/cp32-world-entry-2026-11-08/capture.json";
    private const string CorrectionEvidence = "20_notes/Network/evidence/cp32-correction-2026-11-08/capture.json";
    private const string ConditionalEvidence = "20_notes/Network/evidence/cp32-conditional-world-2026-11-08/capture.json";

    [Fact]
    public void WalkSelectors_ParseAndEncodeOriginalClientFields()
    {
        Sample[] samples = Read("10005-walk.observed.json", GameWalkPacket.PacketIdentifier, EntryEvidence);
        Assert.Equal(new ushort[] { 1, 0 }, samples.Select(static sample => sample.Subtype));
        Assert.Equal(2, samples.Length);
        Assert.Equal((1, 44), (samples[0].Capture, samples[0].Sequence));
        Assert.Equal((2, 46), (samples[1].Capture, samples[1].Sequence));

        foreach (Sample sample in samples)
        {
            Assert.True(GameWalkPacket.TryParse(sample.Frame, out GameWalkPacket walk));
            Assert.Equal(1_000_000u, walk.EntityId);
            Assert.Equal(1010u, walk.MapState);
            Assert.Equal((uint)sample.Subtype, walk.CommandSelector);
            Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(sample.Frame.AsSpan(4)), walk.PackedDirection);
            Assert.Equal((byte)6, walk.DirectionIndex);

            byte[] encoded = new byte[GameWireFrameEncoder.GetFrameLength(walk)];
            Assert.Equal(sample.Frame.Length, GameWireFrameEncoder.WriteFrame(walk, encoded));
            Assert.Equal(sample.Frame, encoded);
        }
    }

    [Fact]
    public void WorldEntryActions_ParseWithExactFieldPositions()
    {
        Sample[] samples = Read("10010-world-entry.observed.json", GameAction.PacketIdentifier, EntryEvidence);
        Assert.Equal(new ushort[] { 74, 114, 75, 76, 96, 77, 78, 97, 251, 132 },
            samples.Select(static sample => sample.Subtype));

        foreach (Sample sample in samples)
        {
            using GameInboundFrame frame = Wrap(sample.Frame);
            Assert.True(GameAction.TryParse(frame, out GameAction action, out GameActionParseError error));
            Assert.Equal(GameActionParseError.None, error);
            Assert.Equal(sample.Subtype, action.Action);
            Assert.Equal(sample.Subtype == 96 ? 3u : 0u, action.ParameterPair);
            Assert.Equal(sample.Subtype == 251 ? 0u : 1_000_000u, action.EntityId);
            Assert.Equal((byte)0, action.StringCount);
            Assert.Equal(0u, action.ActionParameter);
            Assert.Equal((ushort)0, action.Direction);
            Assert.Equal((ushort)0, action.PositionX);
            Assert.Equal((ushort)0, action.PositionY);
            Assert.Equal(0u, action.Data1);
            Assert.Equal(0u, action.Data2);
            Assert.Equal((byte)0, action.Flag);
            Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(sample.Frame.AsSpan(16)), action.Timestamp);

            AssertRoundTrip(action, sample.Frame);
        }
    }

    [Fact]
    public void ReplayedWorldEntryResponses_DecodeAndPreserveBothCapturedWindows()
    {
        Sample[] samples = Read("10010-world-replay.observed.json",
            GameAction.PacketIdentifier, EntryEvidence);
        int[] records = [112, 114, 115, 117, 119, 121, 123, 133, 135, 136, 138, 140, 142, 144];
        ushort[] actions = [76, 96, 77, 78, 97, 251, 132];
        Assert.Equal(records.Length, samples.Length);

        uint previousTick = 0;
        for (int index = 0; index < samples.Length; index++)
        {
            Sample sample = samples[index];
            ushort expectedAction = actions[index % actions.Length];
            Assert.Equal((1, records[index]), (sample.Capture, sample.Sequence));
            Assert.Equal(expectedAction, sample.Subtype);
            using GameInboundFrame frame = Wrap(sample.Frame);
            Assert.True(GameAction.TryParse(frame, out GameAction action, out GameActionParseError error));
            Assert.Equal(GameActionParseError.None, error);
            Assert.Equal(expectedAction, action.Action);
            Assert.Equal(expectedAction == 251 ? 0u : 1_000_000u, action.EntityId);
            Assert.Equal(expectedAction == 96 ? 3u : 0u, action.ParameterPair);
            Assert.Equal(0u, action.ActionParameter);
            Assert.Equal((ushort)0, action.Direction);
            Assert.Equal((ushort)0, action.PositionX);
            Assert.Equal((ushort)0, action.PositionY);
            Assert.Equal(0u, action.Data1);
            Assert.Equal(0u, action.Data2);
            Assert.Equal((byte)0, action.Flag);
            Assert.Equal((byte)0, action.StringCount);

            if (expectedAction == 251)
            {
                Assert.Equal(1u, action.Timestamp);
            }
            else
            {
                Assert.True(action.Timestamp > previousTick);
                previousTick = action.Timestamp;
            }

            AssertRoundTrip(action, sample.Frame);
        }
    }

    [Fact]
    public void CorrectionReplies_ParseWithoutConfusingWireCoordinateFields()
    {
        Sample[] samples = Read("10010-correction.observed.json", GameAction.PacketIdentifier, CorrectionEvidence);
        Assert.Equal(3, samples.Length);
        Assert.Equal((1, 43), (samples[0].Capture, samples[0].Sequence));
        Assert.Equal((1, 47), (samples[1].Capture, samples[1].Sequence));
        Assert.Equal((1, 51), (samples[2].Capture, samples[2].Sequence));

        ushort[] expectedX = [61, 62, 62];
        uint lastTick = 0;
        for (int index = 0; index < samples.Length; index++)
        {
            Sample sample = samples[index];
            using GameInboundFrame frame = Wrap(sample.Frame);
            Assert.True(GamePositionReport.TryParse(frame, out GamePositionReport position));
            Assert.Equal(GamePositionReport.CorrectionReplyAction, position.Action);
            Assert.Equal(1_000_000u, position.EntityId);
            Assert.Equal(expectedX[index], position.X);
            Assert.Equal((ushort)109, position.Y);

            Assert.True(GameAction.TryParse(frame, out GameAction action, out GameActionParseError error));
            Assert.Equal(GameActionParseError.None, error);
            Assert.Equal(146, action.Action);
            Assert.True(action.Timestamp > lastTick);
            Assert.Equal(1010u, action.Data1);
            Assert.Equal(uint.MaxValue, action.Data2);
            Assert.Equal((ushort)0, action.PositionX);
            Assert.Equal((ushort)0, action.PositionY);
            lastTick = action.Timestamp;

            AssertRoundTrip(action, sample.Frame);
        }
    }

    private const ushort FlowerRankPacketId = 1151;

    [Fact]
    public void FlowerRankEntryRequests_AreNotParsedAsWorldActions()
    {
        Sample[] samples = Read("1151-flower-rank.observed.json", FlowerRankPacketId, EntryEvidence);
        Assert.Equal(2, samples.Length);
        Assert.Equal((1, 21), (samples[0].Capture, samples[0].Sequence));
        Assert.Equal((2, 20), (samples[1].Capture, samples[1].Sequence));

        foreach (Sample sample in samples)
        {
            Assert.Equal((ushort)2, sample.Subtype);
            using GameInboundFrame frame = Wrap(sample.Frame);
            Assert.Equal(80, frame.Header.Length);
            Assert.Equal(FlowerRankPacketId, frame.PacketId);
            Assert.False(GameAction.TryParse(frame, out _, out GameActionParseError error));
            Assert.Equal(GameActionParseError.InvalidPacketId, error);
            Assert.All(sample.Frame.AsSpan(5).ToArray(), value => Assert.Equal(0, value));
        }
    }

    [Fact]
    public void GhostTransition_ParsesCapturedClientRequestAndPreservesBytes()
    {
        Sample sample = Assert.Single(Read("10010-ghost-transition.observed.json",
            GameAction.PacketIdentifier, EntryEvidence));
        Assert.Equal((2, 45), (sample.Capture, sample.Sequence));
        using GameInboundFrame frame = Wrap(sample.Frame);
        Assert.True(GameGhostTransitionRequest.TryParse(frame, out GameGhostTransitionRequest request));
        Assert.Equal(1_000_000u, request.EntityId);
        Assert.True(GameAction.TryParse(frame, out GameAction action, out GameActionParseError error));
        Assert.Equal(GameActionParseError.None, error);
        Assert.Equal(GameGhostTransitionRequest.Action, action.Action);
        Assert.Equal(0u, action.ParameterPair);
        Assert.Equal(0u, action.ActionParameter);
        Assert.Equal((ushort)0, action.Direction);
        Assert.Equal((ushort)0, action.PositionX);
        Assert.Equal((ushort)0, action.PositionY);
        Assert.Equal(0u, action.Data1);
        Assert.Equal(0u, action.Data2);
        Assert.Equal((byte)0, action.Flag);
        Assert.Equal((byte)0, action.StringCount);
        AssertRoundTrip(action, sample.Frame);
    }

    [Fact]
    public void ClientStateApplied_ParsesTheCapturedZeroFieldReport()
    {
        Sample sample = Assert.Single(Read("10010-client-state.observed.json",
            GameAction.PacketIdentifier, ConditionalEvidence));
        Assert.Equal((1, 23), (sample.Capture, sample.Sequence));
        Assert.Equal((ushort)408, sample.Subtype);
        using GameInboundFrame frame = Wrap(sample.Frame);
        Assert.True(GameAction.TryParse(frame, out GameAction action, out GameActionParseError error));
        Assert.Equal(GameActionParseError.None, error);
        Assert.Equal((ushort)408, action.Action);
        Assert.Equal(0u, action.EntityId);
        Assert.Equal(0u, action.ParameterPair);
        Assert.Equal(0u, action.ActionParameter);
        Assert.Equal(0u, action.Timestamp);
        Assert.Equal((ushort)0, action.Direction);
        Assert.Equal((ushort)0, action.PositionX);
        Assert.Equal((ushort)0, action.PositionY);
        Assert.Equal(0u, action.Data1);
        Assert.Equal(0u, action.Data2);
        Assert.Equal((byte)0, action.Flag);
        Assert.Equal((byte)0, action.StringCount);
        AssertRoundTrip(action, sample.Frame);
    }

    [Fact]
    public void GroundJumps_ParseAndProduceBytePreservingAcknowledgements()
    {
        Sample[] samples = Read("10010-ground-jump.observed.json",
            GameAction.PacketIdentifier, ConditionalEvidence);
        int[] sequences = [46, 47, 48, 51, 52, 53, 54, 55, 56, 57, 58, 59];
        Assert.Equal(sequences, samples.Select(static sample => sample.Sequence));
        (ushort TargetX, ushort TargetY, ushort FromX, ushort FromY)[] coordinates =
        [
            (62, 110, 63, 106),
            (59, 109, 62, 110),
            (64, 109, 59, 109),
            (60, 111, 64, 109),
            (62, 106, 60, 111),
            (60, 112, 62, 106),
            (66, 110, 60, 112),
            (56, 109, 66, 110),
            (50, 104, 56, 109),
            (49, 95, 50, 104),
            (58, 104, 49, 95),
            (63, 106, 58, 104)
        ];
        for (int index = 0; index < samples.Length; index++)
        {
            Sample sample = samples[index];
            Assert.Equal(1, sample.Capture);
            Assert.Equal(GameJumpAction.JumpAction, sample.Subtype);
            using GameInboundFrame frame = Wrap(sample.Frame);
            Assert.True(GameJumpAction.TryParse(frame, out GameJumpAction jump));
            Assert.Equal(1_000_000u, jump.EntityId);
            Assert.True(jump.IsGroundJump);
            Assert.False(jump.IsMountedCharge);
            Assert.Equal(0u, jump.Action.ActionParameter);
            Assert.Equal((ushort)0, jump.Action.Direction);
            Assert.Equal(1010u, jump.Action.Data1);
            Assert.Equal(uint.MaxValue, jump.Action.Data2);
            Assert.Equal((byte)0, jump.Action.StringCount);
            Assert.Equal((coordinates[index].TargetX, coordinates[index].TargetY),
                (jump.TargetX, jump.TargetY));
            Assert.Equal((coordinates[index].FromX, coordinates[index].FromY),
                (jump.Action.PositionX, jump.Action.PositionY));

            GameActionPacket acknowledgement = jump.CreateAcknowledgement();
            byte[] encoded = new byte[GameWireFrameEncoder.GetFrameLength(acknowledgement)];
            Assert.Equal(sample.Frame.Length, GameWireFrameEncoder.WriteFrame(acknowledgement, encoded));
            Assert.Equal(sample.Frame, encoded);
        }
    }

    [Fact]
    public void TradeQuit_ParsesTheObservedDeathTriggeredRequest()
    {
        Sample sample = Assert.Single(Read("1056-trade-quit.observed.json",
            GameTradeQuitRequest.PacketIdentifier, ConditionalEvidence));
        Assert.Equal((1, 68), (sample.Capture, sample.Sequence));
        Assert.Equal(GameTradeQuitRequest.QuitMode, sample.Subtype);
        using GameInboundFrame frame = Wrap(sample.Frame);
        Assert.True(GameTradeQuitRequest.TryParse(frame, out GameTradeQuitRequest request));
        Assert.Equal(1_000_000u, request.EntityId);
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(sample.Frame.AsSpan(10)));
    }

    private static void AssertRoundTrip(GameAction action, byte[] expected)
    {
        GameActionPacket packet = new(action.EntityId, action.ParameterPair, action.ActionParameter,
            action.Timestamp, action.Action, action.Direction, action.PositionX, action.PositionY,
            action.Data1, action.Data2, action.Flag);
        byte[] actual = new byte[GameWireFrameEncoder.GetFrameLength(packet)];
        Assert.Equal(expected.Length, GameWireFrameEncoder.WriteFrame(packet, actual));
        Assert.Equal(expected, actual);
    }

    private static GameInboundFrame Wrap(byte[] bytes)
    {
        Assert.True(WireFrameHeader.TryRead(bytes, out WireFrameHeader header));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(bytes.Length);
        bytes.CopyTo(buffer, 0);
        return new GameInboundFrame(buffer, header);
    }

    private static Sample[] Read(string fileName, ushort packetId, string evidence)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "WireGolden", fileName)));
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("observed", root.GetProperty("classification").GetString());
        Assert.True(root.GetProperty("runtimeCaptured").GetBoolean());
        Assert.Equal("game", root.GetProperty("protocol").GetString());
        Assert.Equal("client-to-server", root.GetProperty("direction").GetString());
        Assert.Equal(packetId, root.GetProperty("packetId").GetUInt16());
        Assert.Equal("b6ba5ab8573ec073ea271f57b16fa14de1c67bfb44efd5aa7cfbfc0d3b488a62",
            root.GetProperty("referenceImageSha256").GetString());
        Assert.Equal(evidence, root.GetProperty("sourceEvidence").GetString());
        Assert.Equal("peer after game decrypt and client trailer validation", root.GetProperty("capturePoint").GetString());

        return root.GetProperty("samples").EnumerateArray().Select(row =>
        {
            byte[] frame = Convert.FromHexString(row.GetProperty("frameHex").GetString()!);
            Assert.Equal(row.GetProperty("frameSha256").GetString(),
                Convert.ToHexString(SHA256.HashData(frame)).ToLowerInvariant());
            Assert.Equal(frame.Length, BinaryPrimitives.ReadUInt16LittleEndian(frame));
            Assert.Equal(packetId, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(2)));
            ushort subtype = row.GetProperty("subtype").GetUInt16();
            uint decodedSubtype = packetId switch
            {
                GameWalkPacket.PacketIdentifier => BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(12)),
                FlowerRankPacketId => frame[4],
                GameTradeQuitRequest.PacketIdentifier => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(8)),
                _ => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(20))
            };
            Assert.Equal((uint)subtype, decodedSubtype);
            return new Sample(row.GetProperty("capture").GetInt32(),
                row.GetProperty("sequence").GetInt32(), subtype, frame);
        }).ToArray();
    }

    private readonly record struct Sample(int Capture, int Sequence, ushort Subtype, byte[] Frame);
}
