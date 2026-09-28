using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameUserInfoPacket1006Tests
{
    [Fact]
    public void Packet_ExposesCompleteVerifiedWireContract()
    {
        GameUserInfoPacket1006 packet = CreatePopulatedPacket();

        Assert.Equal((ushort)1006, packet.PacketId);
        Assert.Equal(114, packet.PayloadLength);

        Assert.Equal(0x01020304u, packet.EntityId);
        Assert.Equal((ushort)0x0506, packet.TransformLookSourceId);
        Assert.Equal(0x0708090Au, packet.PackedAppearance);
        Assert.Equal((ushort)0x0B0C, packet.HairComposite);
        Assert.Equal(0x0D0E0F10u, packet.Silver);
        Assert.Equal(0x11121314u, packet.ConquerPoints);
        Assert.Equal(0x15161718191A1B1Cul, packet.Experience);

        Assert.Equal(0x1D1E1F20u, packet.LegacyDeed);
        Assert.Equal(0x21222324u, packet.LegacyMedal);
        Assert.Equal(0x25262728u, packet.LegacyMedalSelect);
        Assert.Equal(0x292A2B2Cu, packet.VirtuePoints);
        Assert.Equal(0x2D2E2F30u, packet.EncodedPreRebirthLevel);

        Assert.Equal((ushort)0x3132, packet.Strength);
        Assert.Equal((ushort)0x3334, packet.Agility);
        Assert.Equal((ushort)0x3536, packet.Vitality);
        Assert.Equal((ushort)0x3738, packet.Spirit);
        Assert.Equal((ushort)0x393A, packet.UnspentAttributePoints);
        Assert.Equal((ushort)0x3B3C, packet.CurrentLife);
        Assert.Equal((ushort)0x3D3E, packet.CurrentMana);
        Assert.Equal((short)-321, packet.PkPoints);

        Assert.Equal((byte)0x41, packet.Level);
        Assert.Equal((byte)0x42, packet.CurrentProfession);
        Assert.Equal((byte)0x43, packet.FirstProfession);
        Assert.Equal((byte)0x44, packet.PreviousProfession);
        Assert.Equal((byte)0x45, packet.LegacyNobility);
        Assert.Equal((byte)0x46, packet.RebirthCount);
        Assert.Equal((byte)0x47, packet.LegacyAutoAllot);

        Assert.Equal(0x48494A4Bu, packet.AuraTierScore);
        Assert.Equal((ushort)0x4C4D, packet.CoachPointsHundredths);
        Assert.Equal((ushort)0x4E4F, packet.CoachExperienceShareCount);
        Assert.Equal((ushort)0x5051, packet.CoachSessionState);
        Assert.Equal(0x52535455u, packet.FlowerStatusTier);
        Assert.Equal((ushort)0x5657, packet.TitleId);
        Assert.Equal(0x58595A5Bu, packet.BoundConquerPoints);

        Assert.Equal((byte)0x5C, packet.ActiveSubProfessionId);
        Assert.Equal(0x5D5E5F6061626364ul, packet.PackedSubProfessionPhases);
        Assert.Equal(0x65666768u, packet.RacePoints);

        Assert.Equal("A€", packet.PlayerName);
        Assert.Equal("BC", packet.SpouseName);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesCompleteVerifiedNativeLayout()
    {
        GameUserInfoPacket1006 packet = CreatePopulatedPacket();
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x76, 0x00, 0xEE, 0x03, 0x04, 0x03, 0x02, 0x01,
            0x06, 0x05, 0x0A, 0x09, 0x08, 0x07, 0x0C, 0x0B,
            0x10, 0x0F, 0x0E, 0x0D, 0x14, 0x13, 0x12, 0x11,
            0x1C, 0x1B, 0x1A, 0x19, 0x18, 0x17, 0x16, 0x15,
            0x20, 0x1F, 0x1E, 0x1D, 0x24, 0x23, 0x22, 0x21,
            0x28, 0x27, 0x26, 0x25, 0x2C, 0x2B, 0x2A, 0x29,
            0x30, 0x2F, 0x2E, 0x2D, 0x32, 0x31, 0x34, 0x33,
            0x36, 0x35, 0x38, 0x37, 0x3A, 0x39, 0x3C, 0x3B,
            0x3E, 0x3D, 0xBF, 0xFE, 0x41, 0x42, 0x43, 0x44,
            0x45, 0x46, 0x47, 0x4B, 0x4A, 0x49, 0x48, 0x4D,
            0x4C, 0x4F, 0x4E, 0x00, 0x00, 0x51, 0x50, 0x55,
            0x54, 0x53, 0x52, 0x57, 0x56, 0x5B, 0x5A, 0x59,
            0x58, 0x5C, 0x64, 0x63, 0x62, 0x61, 0x60, 0x5F,
            0x5E, 0x5D, 0x68, 0x67, 0x66, 0x65, 0x03, 0x02,
            0x41, 0x80, 0x00, 0x02, 0x42, 0x43,
        ];

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesKnownCompatibleReferenceFrame()
    {
        GameUserInfoPacket1006 packet = CreateKnownCompatibleReferencePacket();
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x7B, 0x00, 0xEE, 0x03, 0x02, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x7B, 0xAF, 0x1E, 0x00, 0x53, 0x01,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x01, 0x00,
            0x04, 0x00, 0x02, 0x00, 0x00, 0x00, 0x72, 0x00,
            0x0A, 0x00, 0x00, 0x00, 0x01, 0x3C, 0x3C, 0x3C,
            0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x05,
            0x73, 0x61, 0x64, 0x73, 0x61, 0x00, 0x04, 0x4E,
            0x6F, 0x6E, 0x65,
        ];

        Assert.Equal(123, packet.PayloadLength + 4);
        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void Packet_UsesEncodedByteLengthsForStringList()
    {
        GameUserInfoPacket1006 packet = CreatePacket(playerName: "€", spouseName: "€");

        Assert.Equal(112, packet.PayloadLength);

        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(0x03, destination[110]);
        Assert.Equal(0x01, destination[111]);
        Assert.Equal(0x80, destination[112]);
        Assert.Equal(0x00, destination[113]);
        Assert.Equal(0x01, destination[114]);
        Assert.Equal(0x80, destination[115]);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesStructurallyRequiredEmptySecondStringEntry()
    {
        GameUserInfoPacket1006 packet = CreatePacket(playerName: "A", spouseName: "B");
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(0x03, destination[110]);
        Assert.Equal(0x01, destination[111]);
        Assert.Equal((byte)'A', destination[112]);
        Assert.Equal(0x00, destination[113]);
        Assert.Equal(0x01, destination[114]);
        Assert.Equal((byte)'B', destination[115]);
    }

    [Fact]
    public void GameWireFrameEncoder_ZeroesNativeNoReadSpan()
    {
        GameUserInfoPacket1006 packet = CreatePopulatedPacket();
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(0x00, destination[83]);
        Assert.Equal(0x00, destination[84]);
    }

    [Fact]
    public void Constructor_AllowsMaximumVerifiedStringLengths()
    {
        string playerName = new('A', GameUserInfoPacket1006.MaximumStringEntryEncodedLength);
        string spouseName = new('B', GameUserInfoPacket1006.MaximumStringEntryEncodedLength);

        GameUserInfoPacket1006 packet = CreatePacket(playerName, spouseName);

        Assert.Equal(140, packet.PayloadLength);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Constructor_RejectsStringEntriesAboveVerifiedMaximum(bool playerName)
    {
        string oversized = new('A', GameUserInfoPacket1006.MaximumStringEntryEncodedLength + 1);

        ArgumentOutOfRangeException exception = playerName
            ? Assert.Throws<ArgumentOutOfRangeException>(() => CreatePacket(oversized, "None"))
            : Assert.Throws<ArgumentOutOfRangeException>(() => CreatePacket("Player", oversized));

        Assert.Equal(playerName ? "playerName" : "spouseName", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsNullPlayerName()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            CreatePacket(null!, "None"));

        Assert.Equal("playerName", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsNullSpouseName()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            CreatePacket("Player", null!));

        Assert.Equal("spouseName", exception.ParamName);
    }

    [Fact]
    public void Constructor_AllowsEmbeddedNullInLengthPrefixedStrings()
    {
        GameUserInfoPacket1006 packet = CreatePacket("A\0B", "C\0D");
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(116, packet.PayloadLength);
        Assert.Equal(120, written);

        Assert.Equal(0x03, destination[110]);

        Assert.Equal(0x03, destination[111]);
        Assert.Equal((byte)'A', destination[112]);
        Assert.Equal(0x00, destination[113]);
        Assert.Equal((byte)'B', destination[114]);

        Assert.Equal(0x00, destination[115]);

        Assert.Equal(0x03, destination[116]);
        Assert.Equal((byte)'C', destination[117]);
        Assert.Equal(0x00, destination[118]);
        Assert.Equal((byte)'D', destination[119]);
    }

    private static GameUserInfoPacket1006 CreatePopulatedPacket()
    {
        return new GameUserInfoPacket1006("A€", "BC")
        {
            EntityId = 0x01020304,
            TransformLookSourceId = 0x0506,
            PackedAppearance = 0x0708090A,
            HairComposite = 0x0B0C,
            Silver = 0x0D0E0F10,
            ConquerPoints = 0x11121314,
            Experience = 0x15161718191A1B1C,
            LegacyDeed = 0x1D1E1F20,
            LegacyMedal = 0x21222324,
            LegacyMedalSelect = 0x25262728,
            VirtuePoints = 0x292A2B2C,
            EncodedPreRebirthLevel = 0x2D2E2F30,
            Strength = 0x3132,
            Agility = 0x3334,
            Vitality = 0x3536,
            Spirit = 0x3738,
            UnspentAttributePoints = 0x393A,
            CurrentLife = 0x3B3C,
            CurrentMana = 0x3D3E,
            PkPoints = -321,
            Level = 0x41,
            CurrentProfession = 0x42,
            FirstProfession = 0x43,
            PreviousProfession = 0x44,
            LegacyNobility = 0x45,
            RebirthCount = 0x46,
            LegacyAutoAllot = 0x47,
            AuraTierScore = 0x48494A4B,
            CoachPointsHundredths = 0x4C4D,
            CoachExperienceShareCount = 0x4E4F,
            CoachSessionState = 0x5051,
            FlowerStatusTier = 0x52535455,
            TitleId = 0x5657,
            BoundConquerPoints = 0x58595A5B,
            ActiveSubProfessionId = 0x5C,
            PackedSubProfessionPhases = 0x5D5E5F6061626364,
            RacePoints = 0x65666768,
        };
    }

    private static GameUserInfoPacket1006 CreateKnownCompatibleReferencePacket()
    {
        return new GameUserInfoPacket1006("sadsa", "None")
        {
            EntityId = 2,
            TransformLookSourceId = 0,
            PackedAppearance = 2011003,
            HairComposite = 339,
            Silver = 0,
            ConquerPoints = 0,
            Experience = 0,
            LegacyDeed = 0,
            LegacyMedal = 0,
            LegacyMedalSelect = 0,
            VirtuePoints = 0,
            EncodedPreRebirthLevel = 0,
            Strength = 3,
            Agility = 1,
            Vitality = 4,
            Spirit = 2,
            UnspentAttributePoints = 0,
            CurrentLife = 114,
            CurrentMana = 10,
            PkPoints = 0,
            Level = 1,
            CurrentProfession = 60,
            FirstProfession = 60,
            PreviousProfession = 60,
            LegacyNobility = 0,
            RebirthCount = 0,
            LegacyAutoAllot = 1,
            AuraTierScore = 0,
            CoachPointsHundredths = 0,
            CoachExperienceShareCount = 0,
            CoachSessionState = 0,
            FlowerStatusTier = 0,
            TitleId = 0,
            BoundConquerPoints = 0,
            ActiveSubProfessionId = 0,
            PackedSubProfessionPhases = 0,
            RacePoints = 0,
        };
    }

    private static GameUserInfoPacket1006 CreatePacket(string playerName, string spouseName)
    {
        return new GameUserInfoPacket1006(playerName, spouseName)
        {
            EntityId = 0,
            TransformLookSourceId = 0,
            PackedAppearance = 0,
            HairComposite = 0,
            Silver = 0,
            ConquerPoints = 0,
            Experience = 0,
            LegacyDeed = 0,
            LegacyMedal = 0,
            LegacyMedalSelect = 0,
            VirtuePoints = 0,
            EncodedPreRebirthLevel = 0,
            Strength = 0,
            Agility = 0,
            Vitality = 0,
            Spirit = 0,
            UnspentAttributePoints = 0,
            CurrentLife = 0,
            CurrentMana = 0,
            PkPoints = 0,
            Level = 0,
            CurrentProfession = 0,
            FirstProfession = 0,
            PreviousProfession = 0,
            LegacyNobility = 0,
            RebirthCount = 0,
            LegacyAutoAllot = 0,
            AuraTierScore = 0,
            CoachPointsHundredths = 0,
            CoachExperienceShareCount = 0,
            CoachSessionState = 0,
            FlowerStatusTier = 0,
            TitleId = 0,
            BoundConquerPoints = 0,
            ActiveSubProfessionId = 0,
            PackedSubProfessionPhases = 0,
            RacePoints = 0,
        };
    }
}
