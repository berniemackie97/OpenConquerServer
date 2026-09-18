using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Represents the native 5517 server-to-client local-user information packet.
/// </summary>
public sealed class GameUserInfoPacket1006(string name, string spouseName) : IPacket
{
    public const ushort PacketIdentifier = 1006;
    public const int FixedPayloadLength = 106;
    public const int MaximumStringEntryEncodedLength = 15;

    private const byte StringCount = 3;
    private const byte KnownCompatibleInertField74Value = 1;
    private const int StringListMetadataLength = sizeof(byte) + StringCount * sizeof(byte);

    private readonly int _nameEncodedLength = GetValidatedStringLength(name, nameof(name));
    private readonly int _spouseNameEncodedLength = GetValidatedStringLength(spouseName, nameof(spouseName));

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength => FixedPayloadLength + StringListMetadataLength + _nameEncodedLength + _spouseNameEncodedLength;

    public required uint Identity { get; init; }
    public required ushort TransformLookSourceId { get; init; }
    public required uint AppearanceComposite { get; init; }
    public required ushort HairComposite { get; init; }
    public required uint Silver { get; init; }
    public required uint ConquerPoints { get; init; }
    public required ulong Experience { get; init; }
    public required ushort Strength { get; init; }
    public required ushort Agility { get; init; }
    public required ushort Vitality { get; init; }
    public required ushort Spirit { get; init; }
    public required ushort UnspentAttributePoints { get; init; }
    public required ushort CurrentLife { get; init; }
    public required ushort CurrentMana { get; init; }
    public required ushort PkPoints { get; init; }
    public required byte Level { get; init; }
    public required byte Profession { get; init; }
    public required byte FirstProfession { get; init; }
    public required byte PreviousProfession { get; init; }
    public required byte RebirthCount { get; init; }

    /// <summary>
    /// Coach/mentor enlightenment points in the client's scaled-by-100 representation.
    /// </summary>
    public required ushort EnlightenmentPoints { get; init; }

    public required ushort TitleId { get; init; }
    public required uint BoundConquerPoints { get; init; }
    public string Name { get; } = name;
    public string SpouseName { get; } = spouseName;

    public void WritePayload(ref PacketWriter writer)
    {
        int start = writer.Written;

        writer.WriteUInt32(Identity);
        writer.WriteUInt16(TransformLookSourceId);
        writer.WriteUInt32(AppearanceComposite);
        writer.WriteUInt16(HairComposite);
        writer.WriteUInt32(Silver);
        writer.WriteUInt32(ConquerPoints);
        writer.WriteUInt64(Experience);

        // +32..+47: client-loaded but behaviorally inert in 5517.
        writer.Reserve(16);

        // +48: tutor level-equivalent wire value. Not currently persisted.
        writer.Reserve(sizeof(uint));

        writer.WriteUInt16(Strength);
        writer.WriteUInt16(Agility);
        writer.WriteUInt16(Vitality);
        writer.WriteUInt16(Spirit);
        writer.WriteUInt16(UnspentAttributePoints);
        writer.WriteUInt16(CurrentLife);
        writer.WriteUInt16(CurrentMana);
        writer.WriteUInt16(PkPoints);
        writer.WriteByte(Level);
        writer.WriteByte(Profession);
        writer.WriteByte(FirstProfession);
        writer.WriteByte(PreviousProfession);

        // +72: client-loaded but behaviorally inert in 5517.
        writer.Reserve(sizeof(byte));

        writer.WriteByte(RebirthCount);

        // +74: semantics remain unproven; 1 is the known-compatible reference policy.
        writer.WriteByte(KnownCompatibleInertField74Value);

        // +75: aura-tier score. The current character-login profile does not model it.
        writer.Reserve(sizeof(uint));

        writer.WriteUInt16(EnlightenmentPoints);

        // +81: coach EXP-share count.
        writer.Reserve(sizeof(ushort));

        // +83..+84: not read by the 5517 handler.
        writer.Reserve(sizeof(ushort));

        // +85: coach-session state.
        writer.Reserve(sizeof(ushort));

        // +87: flower-status tier.
        writer.Reserve(sizeof(uint));

        writer.WriteUInt16(TitleId);
        writer.WriteUInt32(BoundConquerPoints);

        // +97 and +98..+105: selected index and packed per-type levels.
        writer.Reserve(sizeof(byte));
        writer.Reserve(sizeof(ulong));

        // +106: race points.
        writer.Reserve(sizeof(uint));

        int fixedPayloadLength = writer.Written - start;
        if (fixedPayloadLength != FixedPayloadLength)
        {
            throw new InvalidOperationException($"MsgUserInfo fixed payload must be exactly {FixedPayloadLength} bytes; wrote {fixedPayloadLength}.");
        }

        writer.WriteByte(StringCount);
        writer.WriteByteString(Name);
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
