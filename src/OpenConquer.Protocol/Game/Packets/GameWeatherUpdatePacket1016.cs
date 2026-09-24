using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Represents the native 5517 server-to-client weather update packet.
/// </summary>
public sealed class GameWeatherUpdatePacket1016 : IPacket
{
    public const ushort PacketIdentifier = 1016;
    public const int PayloadSize = sizeof(uint) * 4;
    public const uint MinimumKind = 1;
    public const uint MaximumKind = 11;
    public const uint ClearKind = 1;
    public const uint UnmappedKind = 6;
    public const uint MaximumIntensity = 1022;
    public const uint MaximumDirectionDegrees = 359;

    public GameWeatherUpdatePacket1016(uint kind, uint intensity, uint directionDegrees, uint parameter)
    {
        if (kind is < MinimumKind or > MaximumKind)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), $"Weather kind must be between {MinimumKind} and {MaximumKind}.");
        }

        if (kind == UnmappedKind)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), $"Weather kind {UnmappedKind} has no valid native client mapping.");
        }

        if (intensity > MaximumIntensity)
        {
            throw new ArgumentOutOfRangeException(nameof(intensity), $"Weather intensity must not exceed {MaximumIntensity}.");
        }

        if (directionDegrees > MaximumDirectionDegrees)
        {
            throw new ArgumentOutOfRangeException(nameof(directionDegrees), $"Weather direction must not exceed {MaximumDirectionDegrees} degrees.");
        }

        Kind = kind;
        Intensity = intensity;
        DirectionDegrees = directionDegrees;
        Parameter = parameter;
    }

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength => PayloadSize;
    public uint Kind { get; }
    public uint Intensity { get; }
    public uint DirectionDegrees { get; }
    public uint Parameter { get; }

    public static GameWeatherUpdatePacket1016 CreateClear() => new(ClearKind, intensity: 0, directionDegrees: 0, parameter: 0);

    public void WritePayload(ref PacketWriter writer)
    {
        writer.WriteUInt32(Kind);
        writer.WriteUInt32(Intensity);
        writer.WriteUInt32(DirectionDegrees);
        writer.WriteUInt32(Parameter);
    }
}
