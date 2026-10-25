using OpenConquer.GameServer.Login.Character;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Bootstrap;

/// <summary>
/// Reads the fixed-size action packets the client sends during the existing-character login.
/// </summary>
internal static class LoginActionRequest
{
    /// <summary>
    /// Reads the next frame and requires a fixed-size action packet with no strings carrying <paramref name="expectedAction"/>.
    /// The remaining fields are left to the caller because each login step accepts different values.
    /// </summary>
    public static async ValueTask<GameAction> ReadAsync(ExistingCharacterGameConnection connection, ushort expectedAction, string requestName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using GameInboundFrame frame = await connection.ReadAsync(cancellationToken).ConfigureAwait(false) ?? throw ConnectionClosed(requestName);

        return Parse(frame, expectedAction, requestName);
    }

    /// <summary>
    /// Reads the next action like <see cref="ReadAsync"/>, first consuming any mission-catalog frames (1134) in front of it.
    /// The client sends its catalog while handling ANSWER_OK, which places it before the EnterMap request or between that
    /// request and the client-state-applied notification, so only those two steps read through this.
    /// </summary>
    public static async ValueTask<GameAction> ReadAfterMissionCatalogAsync(ExistingCharacterLoginContext login, ushort expectedAction,
        string requestName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ExistingCharacterGameConnection connection = login.Connection;

        while (true)
        {
            using GameInboundFrame frame = await connection.ReadAsync(cancellationToken).ConfigureAwait(false) ?? throw ConnectionClosed(requestName);

            if (frame.PacketId != GameMissionCatalogReport.PacketIdentifier)
            {
                login.MissionCatalog.EndBurst();
                return Parse(frame, expectedAction, requestName);
            }

            if (!GameMissionCatalogReport.TryParse(frame, out GameMissionCatalogReport catalog, out GameMissionCatalogReportParseError parseError))
            {
                throw new InvalidDataException($"The mission catalog received before the {requestName} is malformed: {parseError}.");
            }

            login.MissionCatalog.Accept(catalog);
        }
    }

    /// <summary>
    /// Requires the authenticated character's entity ID and zero in every field except the timestamp and action.
    /// </summary>
    public static void RequireCharacterRequest(GameAction request, uint characterId, string requestName)
    {
        if (request.EntityId != characterId)
        {
            throw new InvalidDataException($"The {requestName} character ID does not match the authenticated character.");
        }

        if (request.ParameterPair != 0 || request.ActionParameter != 0 || request.Direction != 0
            || request.PositionX != 0 || request.PositionY != 0 || request.Data1 != 0
            || request.Data2 != 0 || request.Flag != 0)
        {
            throw new InvalidDataException($"The {requestName} requires all non-identity, non-timestamp and non-action fields to be zero.");
        }
    }

    private static GameAction Parse(GameInboundFrame frame, ushort expectedAction, string requestName)
    {
        if (!GameAction.TryParse(frame, out GameAction request, out GameActionParseError parseError))
        {
            throw new InvalidDataException($"The frame received for the {requestName} is not a valid action packet: {parseError}.");
        }

        if (frame.Header.Length != GameAction.FixedPacketLength || request.StringCount != 0)
        {
            throw new InvalidDataException($"The {requestName} must contain the fixed action body without trailing strings.");
        }

        if (request.Action != expectedAction)
        {
            throw new InvalidDataException($"Expected {requestName} action {expectedAction}, received action {request.Action}.");
        }

        return request;
    }

    private static EndOfStreamException ConnectionClosed(string requestName) =>
        new($"The GameServer connection closed while awaiting the {requestName}.");
}
