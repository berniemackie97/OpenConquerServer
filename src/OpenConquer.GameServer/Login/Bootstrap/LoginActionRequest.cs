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

        GameAction request;

        using (GameInboundFrame? frame = await connection.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (frame is null)
            {
                throw new EndOfStreamException($"The GameServer connection closed while awaiting the {requestName}.");
            }

            if (!GameAction.TryParse(frame, out request, out GameActionParseError parseError))
            {
                throw new InvalidDataException($"The frame received for the {requestName} is not a valid action packet: {parseError}.");
            }

            if (frame.Header.Length != GameAction.FixedPacketLength || request.StringCount != 0)
            {
                throw new InvalidDataException($"The {requestName} must contain the fixed action body without trailing strings.");
            }
        }

        if (request.Action != expectedAction)
        {
            throw new InvalidDataException($"Expected {requestName} action {expectedAction}, received action {request.Action}.");
        }

        return request;
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
}
