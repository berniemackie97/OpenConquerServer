using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.Domain.Syndicates;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.WorldEntry;

internal static class ExistingCharacterSyndicateAttributesWireProjection
{
    public static GameSyndicateAttributeInfoPacket1106? Create(CharacterSyndicateState state, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (utcNow.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Syndicate-attributes wire projection requires a UTC timestamp.", nameof(utcNow));
        }

        if (state.Membership is not { } membership)
        {
            return null;
        }

        Syndicate syndicate = state.Syndicate ?? throw new InvalidDataException($"Syndicate state for character {state.CharacterId} is missing its syndicate.");

        if (syndicate.Population == 0)
        {
            throw new InvalidDataException($"Syndicate {syndicate.SyndicateId} has an invalid population of zero.");
        }

        uint positionExpirationDate = membership.PositionExpirationUnixSeconds > utcNow.ToUnixTimeSeconds()
            ? ToUtcDateStamp(membership.PositionExpirationUnixSeconds) : 0;

        uint joinDate = ToUtcDateStamp(membership.JoinDateUnixSeconds);

        return new GameSyndicateAttributeInfoPacket1106(syndicate.SyndicateId, membership.Proffer, syndicate.SilverFund,
            syndicate.EmoneyFund, syndicate.Population, membership.Rank, syndicate.LeaderName, syndicate.RequiredLevel,
            syndicate.RequiredMetempsychosis, syndicate.RequiredProfession, 0, 0, positionExpirationDate, joinDate, syndicate.Name);
    }

    private static uint ToUtcDateStamp(uint unixSeconds)
    {
        if (unixSeconds == 0)
        {
            return 0;
        }

        DateTime date = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
        return checked((uint)(date.Year * 10_000 + date.Month * 100 + date.Day));
    }
}
