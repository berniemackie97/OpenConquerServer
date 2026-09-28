using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Represents the native 5517 server-to-client local-user information packet.
/// </summary>
public sealed class GameUserInfoPacket1006(string playerName, string spouseName) : IPacket
{
    public const ushort PacketIdentifier = 1006;
    public const int FixedPayloadLength = 106;
    public const int MaximumStringEntryEncodedLength = 15;

    private const byte StringCount = 3;
    private const int StringListMetadataLength = sizeof(byte) + StringCount * sizeof(byte);

    private readonly int _playerNameEncodedLength = GetValidatedStringLength(playerName, nameof(playerName));
    private readonly int _spouseNameEncodedLength = GetValidatedStringLength(spouseName, nameof(spouseName));

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength => FixedPayloadLength + StringListMetadataLength + _playerNameEncodedLength + _spouseNameEncodedLength;

    public required uint EntityId { get; init; }
    public required ushort TransformLookSourceId { get; init; }
    public required uint PackedAppearance { get; init; }
    public required ushort HairComposite { get; init; }
    public required uint Silver { get; init; }
    public required uint ConquerPoints { get; init; }
    public required ulong Experience { get; init; }

    public required uint LegacyDeed { get; init; }
    public required uint LegacyMedal { get; init; }
    public required uint LegacyMedalSelect { get; init; }
    public required uint VirtuePoints { get; init; }
    public required uint EncodedPreRebirthLevel { get; init; }

    public required ushort Strength { get; init; }
    public required ushort Agility { get; init; }
    public required ushort Vitality { get; init; }
    public required ushort Spirit { get; init; }
    public required ushort UnspentAttributePoints { get; init; }
    public required ushort CurrentLife { get; init; }
    public required ushort CurrentMana { get; init; }
    public required short PkPoints { get; init; }

    public required byte Level { get; init; }
    public required byte CurrentProfession { get; init; }
    public required byte FirstProfession { get; init; }
    public required byte PreviousProfession { get; init; }
    public required byte LegacyNobility { get; init; }
    public required byte RebirthCount { get; init; }
    public required byte LegacyAutoAllot { get; init; }

    public required uint AuraTierScore { get; init; }
    public required ushort CoachPointsHundredths { get; init; }
    public required ushort CoachExperienceShareCount { get; init; }
    public required ushort CoachSessionState { get; init; }
    public required uint FlowerStatusTier { get; init; }
    public required ushort TitleId { get; init; }
    public required uint BoundConquerPoints { get; init; }

    public required byte ActiveSubProfessionId { get; init; }
    public required ulong PackedSubProfessionPhases { get; init; }
    public required uint RacePoints { get; init; }

    public string PlayerName { get; } = playerName;
    public string SpouseName { get; } = spouseName;

    public void WritePayload(ref PacketWriter writer)
    {
        int start = writer.Written;

        writer.WriteUInt32(EntityId);
        writer.WriteUInt16(TransformLookSourceId);
        writer.WriteUInt32(PackedAppearance);
        writer.WriteUInt16(HairComposite);
        writer.WriteUInt32(Silver);
        writer.WriteUInt32(ConquerPoints);
        writer.WriteUInt64(Experience);

        writer.WriteUInt32(LegacyDeed);
        writer.WriteUInt32(LegacyMedal);
        writer.WriteUInt32(LegacyMedalSelect);
        writer.WriteUInt32(VirtuePoints);
        writer.WriteUInt32(EncodedPreRebirthLevel);

        writer.WriteUInt16(Strength);
        writer.WriteUInt16(Agility);
        writer.WriteUInt16(Vitality);
        writer.WriteUInt16(Spirit);
        writer.WriteUInt16(UnspentAttributePoints);
        writer.WriteUInt16(CurrentLife);
        writer.WriteUInt16(CurrentMana);
        writer.WriteInt16(PkPoints);

        writer.WriteByte(Level);
        writer.WriteByte(CurrentProfession);
        writer.WriteByte(FirstProfession);
        writer.WriteByte(PreviousProfession);
        writer.WriteByte(LegacyNobility);
        writer.WriteByte(RebirthCount);
        writer.WriteByte(LegacyAutoAllot);

        writer.WriteUInt32(AuraTierScore);
        writer.WriteUInt16(CoachPointsHundredths);
        writer.WriteUInt16(CoachExperienceShareCount);

        // +83..+84 are not loaded by the verified native 5517 handler.
        writer.Reserve(sizeof(ushort));

        writer.WriteUInt16(CoachSessionState);
        writer.WriteUInt32(FlowerStatusTier);
        writer.WriteUInt16(TitleId);
        writer.WriteUInt32(BoundConquerPoints);

        writer.WriteByte(ActiveSubProfessionId);
        writer.WriteUInt64(PackedSubProfessionPhases);
        writer.WriteUInt32(RacePoints);

        int fixedPayloadLength = writer.Written - start;
        if (fixedPayloadLength != FixedPayloadLength)
        {
            throw new InvalidOperationException($"MsgUserInfo fixed payload must be exactly {FixedPayloadLength} bytes; wrote {fixedPayloadLength}.");
        }

        writer.WriteByte(StringCount);
        writer.WriteByteString(PlayerName);

        // String entry 1 is structurally required but discarded on the verified native path.
        writer.WriteByteString(string.Empty);

        writer.WriteByteString(SpouseName);
    }

    private static int GetValidatedStringLength(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        int encodedLength = TqEncoding.Resolve(TqTextEncoding.Ansi).GetByteCount(value);

        if (encodedLength > MaximumStringEntryEncodedLength)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"MsgUserInfo string-list entries must not exceed {MaximumStringEntryEncodedLength} encoded bytes.");
        }

        return encodedLength;
    }
}
