using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameLocalItemSnapshotPacket1008Tests
{
    [Fact]
    public void Packet_ExposesCompleteVerifiedWireContract()
    {
        GameLocalItemSnapshotPacket1008 packet = CreatePopulatedPacket();

        Assert.Equal((ushort)1008, packet.PacketId);
        Assert.Equal(GameLocalItemSnapshotPacket1008.PayloadSize, packet.PayloadLength);
        Assert.Equal(0x01020304u, packet.ItemId);
        Assert.Equal(0x05060708u, packet.ItemTypeId);
        Assert.Equal((ushort)0x090A, packet.Durability);
        Assert.Equal((ushort)0x0B0C, packet.MaximumDurability);
        Assert.Equal((byte)0x0D, packet.RetailCompatibilityByteA);
        Assert.Equal((byte)0x12, packet.ItemWirePlacement);
        Assert.Equal(0x0F101112u, packet.TalismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline);
        Assert.Equal((byte)0x13, packet.Socket1Code);
        Assert.Equal((byte)0x14, packet.Socket2Code);
        Assert.Equal(0x15161718u, packet.HiddenAttackEffect);
        Assert.Equal((byte)0x19, packet.RetailCompatibilityByteB);
        Assert.Equal((byte)0x1A, packet.AdditionLevel);
        Assert.Equal((byte)0x1B, packet.DamageReductionPercentOrSteedCompositionRed);
        Assert.Equal((byte)0x1C, packet.ItemBindingCode);
        Assert.Equal((byte)0x1D, packet.EnchantmentLifeBonusOrSteedCompositionGreen);
        Assert.Equal(0x1E1F2021u, packet.MonsterRestraintIdOrSteedCompositionBlue);
        Assert.True(packet.IsSuspicious);
        Assert.Equal((ushort)0x2223, packet.EquipmentLockStateMask);
        Assert.Equal((ushort)0x2425, packet.EquipmentColor);
        Assert.Equal(0x26272829u, packet.CompositionProgress);
        Assert.Equal(0x2A2B2C2Du, packet.InscribedSyndicateId);
        Assert.Equal(-1234567, packet.WireLifetimeSeconds);
        Assert.Equal((ushort)0x2E2F, packet.StackQuantity);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesCompleteVerifiedNativeLayout()
    {
        GameLocalItemSnapshotPacket1008 packet = CreatePopulatedPacket();
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x44, 0x00, 0xF0, 0x03, 0x04, 0x03, 0x02, 0x01,
            0x08, 0x07, 0x06, 0x05, 0x0A, 0x09, 0x0C, 0x0B,
            0x01, 0x0D, 0x12, 0x00, 0x12, 0x11, 0x10, 0x0F,
            0x13, 0x14, 0x00, 0x00, 0x18, 0x17, 0x16, 0x15,
            0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x00, 0x00, 0x00,
            0x21, 0x20, 0x1F, 0x1E, 0x01, 0x00, 0x23, 0x22,
            0x25, 0x24, 0x00, 0x00, 0x29, 0x28, 0x27, 0x26,
            0x2D, 0x2C, 0x2B, 0x2A, 0x79, 0x29, 0xED, 0xFF,
            0x2F, 0x2E, 0x00, 0x00,
        ];

        Assert.Equal(GameLocalItemSnapshotPacket1008.FixedPacketLength, written);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void GameWireFrameEncoder_ZeroesVerifiedReservedBytes()
    {
        GameLocalItemSnapshotPacket1008 packet = CreatePopulatedPacket();
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal((byte)0, destination[0x13]);
        Assert.Equal([0, 0], destination[0x1A..0x1C]);
        Assert.Equal([0, 0, 0], destination[0x25..0x28]);
        Assert.Equal([0, 0], destination[0x32..0x34]);
        Assert.Equal([0, 0], destination[0x42..0x44]);
    }

    [Fact]
    public void GameWireFrameEncoder_PreservesSignedLifetimeBitPattern()
    {
        GameLocalItemSnapshotPacket1008 packet = CreatePacket(wireLifetimeSeconds: int.MinValue);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal([0x00, 0x00, 0x00, 0x80], destination[0x3C..0x40]);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesFalseSuspicionAsZero()
    {
        GameLocalItemSnapshotPacket1008 packet = CreatePacket(isSuspicious: false);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal([0x00, 0x00], destination[0x2C..0x2E]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(15)]
    [InlineData(18)]
    [InlineData(21)]
    [InlineData(29)]
    public void GameWireFrameEncoder_AllowsVerifiedLocalPlacement(byte placement)
    {
        GameLocalItemSnapshotPacket1008 packet = CreatePacket(itemWirePlacement: placement);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(GameLocalItemSnapshotPacket1008.FixedPacketLength, written);
        Assert.Equal(placement, destination[0x12]);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(255)]
    public void GameWireFrameEncoder_RejectsUnsupportedLocalPlacement(byte placement)
    {
        GameLocalItemSnapshotPacket1008 packet = CreatePacket(itemWirePlacement: placement);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => GameWireFrameEncoder.WriteFrame(packet, destination));

        Assert.Contains("placement", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameWireFrameEncoder_RejectsZeroItemId()
    {
        GameLocalItemSnapshotPacket1008 packet = CreatePacket(itemId: 0);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => GameWireFrameEncoder.WriteFrame(packet, destination));

        Assert.Contains("item ID", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GameWireFrameEncoder_RejectsZeroItemTypeId()
    {
        GameLocalItemSnapshotPacket1008 packet = CreatePacket(itemTypeId: 0);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => GameWireFrameEncoder.WriteFrame(packet, destination));

        Assert.Contains("item type ID", exception.Message, StringComparison.Ordinal);
    }

    private static GameLocalItemSnapshotPacket1008 CreatePopulatedPacket()
    {
        return new GameLocalItemSnapshotPacket1008
        {
            ItemId = 0x01020304,
            ItemTypeId = 0x05060708,
            Durability = 0x090A,
            MaximumDurability = 0x0B0C,
            RetailCompatibilityByteA = 0x0D,
            ItemWirePlacement = 0x12,
            TalismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline = 0x0F101112,
            Socket1Code = 0x13,
            Socket2Code = 0x14,
            HiddenAttackEffect = 0x15161718,
            RetailCompatibilityByteB = 0x19,
            AdditionLevel = 0x1A,
            DamageReductionPercentOrSteedCompositionRed = 0x1B,
            ItemBindingCode = 0x1C,
            EnchantmentLifeBonusOrSteedCompositionGreen = 0x1D,
            MonsterRestraintIdOrSteedCompositionBlue = 0x1E1F2021,
            IsSuspicious = true,
            EquipmentLockStateMask = 0x2223,
            EquipmentColor = 0x2425,
            CompositionProgress = 0x26272829,
            InscribedSyndicateId = 0x2A2B2C2D,
            WireLifetimeSeconds = -1234567,
            StackQuantity = 0x2E2F,
        };
    }

    private static GameLocalItemSnapshotPacket1008 CreatePacket(uint itemId = 1, uint itemTypeId = 100000,
        byte itemWirePlacement = GameLocalItemSnapshotPacket1008.InventoryPlacement, bool isSuspicious = false, int wireLifetimeSeconds = 0)
    {
        return new GameLocalItemSnapshotPacket1008
        {
            ItemId = itemId,
            ItemTypeId = itemTypeId,
            Durability = 0,
            MaximumDurability = 0,
            RetailCompatibilityByteA = 0,
            ItemWirePlacement = itemWirePlacement,
            TalismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline = 0,
            Socket1Code = 0,
            Socket2Code = 0,
            HiddenAttackEffect = 0,
            RetailCompatibilityByteB = 0,
            AdditionLevel = 0,
            DamageReductionPercentOrSteedCompositionRed = 0,
            ItemBindingCode = 0,
            EnchantmentLifeBonusOrSteedCompositionGreen = 0,
            MonsterRestraintIdOrSteedCompositionBlue = 0,
            IsSuspicious = isSuspicious,
            EquipmentLockStateMask = 0,
            EquipmentColor = 0,
            CompositionProgress = 0,
            InscribedSyndicateId = 0,
            WireLifetimeSeconds = wireLifetimeSeconds,
            StackQuantity = 0,
        };
    }
}
