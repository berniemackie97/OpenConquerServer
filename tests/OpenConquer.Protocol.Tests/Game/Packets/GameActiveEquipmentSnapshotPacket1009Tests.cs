using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.Protocol.Tests.Game.Packets;

public sealed class GameActiveEquipmentSnapshotPacket1009Tests
{
    [Fact]
    public void Packet_ExposesCompleteVerifiedWireContract()
    {
        GameActiveEquipmentSnapshotPacket1009 packet = CreatePopulatedPacket();

        Assert.Equal((ushort)1009, packet.PacketId);
        Assert.Equal(GameActiveEquipmentSnapshotPacket1009.PayloadSize, packet.PayloadLength);
        Assert.Equal(GameActiveEquipmentSnapshotPacket1009.AlternateEquipmentMode, packet.EquipmentMode);
        Assert.Equal(0x01020304u, packet.HeadwearItemId);
        Assert.Equal(0x05060708u, packet.NecklaceItemId);
        Assert.Equal(0x090A0B0Cu, packet.ArmorItemId);
        Assert.Equal(0x0D0E0F10u, packet.RightHandItemId);
        Assert.Equal(0x11121314u, packet.LeftHandItemId);
        Assert.Equal(0x15161718u, packet.RingItemId);
        Assert.Equal(0x191A1B1Cu, packet.BottleItemId);
        Assert.Equal(0x1D1E1F20u, packet.BootsItemId);
        Assert.Equal(0x21222324u, packet.GarmentItemId);
        Assert.Equal(0x25262728u, packet.RightWeaponAccessoryItemId);
        Assert.Equal(0x292A2B2Cu, packet.LeftWeaponAccessoryItemId);
        Assert.Equal(0x2D2E2F30u, packet.SteedArmorItemId);
        Assert.Equal(0x31323334u, packet.RidingCropItemId);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesCompleteVerifiedNativeLayout()
    {
        GameActiveEquipmentSnapshotPacket1009 packet = CreatePopulatedPacket();
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        byte[] expected =
        [
            0x58, 0x00, 0xF1, 0x03,
            0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x00,
            0x2E, 0x00,
            0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x04, 0x03, 0x02, 0x01,
            0x08, 0x07, 0x06, 0x05,
            0x0C, 0x0B, 0x0A, 0x09,
            0x10, 0x0F, 0x0E, 0x0D,
            0x14, 0x13, 0x12, 0x11,
            0x18, 0x17, 0x16, 0x15,
            0x1C, 0x1B, 0x1A, 0x19,
            0x20, 0x1F, 0x1E, 0x1D,
            0x24, 0x23, 0x22, 0x21,
            0x28, 0x27, 0x26, 0x25,
            0x2C, 0x2B, 0x2A, 0x29,
            0x30, 0x2F, 0x2E, 0x2D,
            0x34, 0x33, 0x32, 0x31,
            0x00, 0x00, 0x00, 0x00,
        ];

        Assert.Equal(GameActiveEquipmentSnapshotPacket1009.FixedPacketLength, written);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void GameWireFrameEncoder_ZeroesVerifiedUnusedFields()
    {
        GameActiveEquipmentSnapshotPacket1009 packet = CreatePopulatedPacket();
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal([0, 0, 0, 0], destination[0x04..0x08]);
        Assert.Equal([0, 0], destination[0x0E..0x10]);
        Assert.Equal([0, 0, 0, 0], destination[0x10..0x14]);
        Assert.Equal([0, 0, 0, 0], destination[0x14..0x18]);
        Assert.Equal([0, 0, 0, 0], destination[0x18..0x1C]);
        Assert.Equal([0, 0, 0, 0], destination[0x1C..0x20]);
        Assert.Equal([0, 0, 0, 0], destination[0x54..0x58]);
    }

    [Fact]
    public void GameWireFrameEncoder_WritesVerifiedSubtypeAtNativeOffset()
    {
        GameActiveEquipmentSnapshotPacket1009 packet = CreatePacket(
            equipmentMode: GameActiveEquipmentSnapshotPacket1009.MainEquipmentMode);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal((byte)0x2E, destination[0x0C]);
        Assert.Equal((byte)0x00, destination[0x0D]);
    }

    [Theory]
    [InlineData(GameActiveEquipmentSnapshotPacket1009.MainEquipmentMode)]
    [InlineData(GameActiveEquipmentSnapshotPacket1009.AlternateEquipmentMode)]
    public void GameWireFrameEncoder_AllowsVerifiedEquipmentMode(uint equipmentMode)
    {
        GameActiveEquipmentSnapshotPacket1009 packet = CreatePacket(equipmentMode);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(GameActiveEquipmentSnapshotPacket1009.FixedPacketLength, written);
        Assert.Equal((byte)equipmentMode, destination[0x08]);
        Assert.Equal((byte)0, destination[0x09]);
        Assert.Equal((byte)0, destination[0x0A]);
        Assert.Equal((byte)0, destination[0x0B]);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(uint.MaxValue)]
    public void GameWireFrameEncoder_RejectsUnsupportedEquipmentMode(uint equipmentMode)
    {
        GameActiveEquipmentSnapshotPacket1009 packet = CreatePacket(equipmentMode);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            GameWireFrameEncoder.WriteFrame(packet, destination));

        Assert.Contains("equipment mode", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameWireFrameEncoder_AllowsEmptyEquipmentSnapshot()
    {
        GameActiveEquipmentSnapshotPacket1009 packet = CreatePacket(
            equipmentMode: GameActiveEquipmentSnapshotPacket1009.MainEquipmentMode);
        byte[] destination = new byte[GameWireFrameEncoder.GetFrameLength(packet)];

        int written = GameWireFrameEncoder.WriteFrame(packet, destination);

        Assert.Equal(GameActiveEquipmentSnapshotPacket1009.FixedPacketLength, written);
        Assert.Equal(new byte[52], destination[0x20..0x54]);
    }

    private static GameActiveEquipmentSnapshotPacket1009 CreatePopulatedPacket()
    {
        return new GameActiveEquipmentSnapshotPacket1009
        {
            EquipmentMode = GameActiveEquipmentSnapshotPacket1009.AlternateEquipmentMode,
            HeadwearItemId = 0x01020304,
            NecklaceItemId = 0x05060708,
            ArmorItemId = 0x090A0B0C,
            RightHandItemId = 0x0D0E0F10,
            LeftHandItemId = 0x11121314,
            RingItemId = 0x15161718,
            BottleItemId = 0x191A1B1C,
            BootsItemId = 0x1D1E1F20,
            GarmentItemId = 0x21222324,
            RightWeaponAccessoryItemId = 0x25262728,
            LeftWeaponAccessoryItemId = 0x292A2B2C,
            SteedArmorItemId = 0x2D2E2F30,
            RidingCropItemId = 0x31323334,
        };
    }

    private static GameActiveEquipmentSnapshotPacket1009 CreatePacket(uint equipmentMode)
    {
        return new GameActiveEquipmentSnapshotPacket1009
        {
            EquipmentMode = equipmentMode,
            HeadwearItemId = 0,
            NecklaceItemId = 0,
            ArmorItemId = 0,
            RightHandItemId = 0,
            LeftHandItemId = 0,
            RingItemId = 0,
            BottleItemId = 0,
            BootsItemId = 0,
            GarmentItemId = 0,
            RightWeaponAccessoryItemId = 0,
            LeftWeaponAccessoryItemId = 0,
            SteedArmorItemId = 0,
            RidingCropItemId = 0,
        };
    }
}
