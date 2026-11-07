using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Bootstrap;

/// <summary>
/// Reads the fixed-size action packets the client sends during the existing-character login.
/// </summary>
internal static class LoginActionRequest
{
    // The ladder requests, in the order the 5517 client sends them (response edges 0x6FF540, 0x6FF3F4, 0x6FF38C, 0x6FF321;
    // 78 -> 97 -> 251 -> 132 per 21_msgaction_0061_syndicate_bootstrap.md).
    private static readonly ushort[] s_ladderActions =
    [
        GameAction.EnterMapAction, GameAction.GetItemSetAction, GameAction.GetFriendListAction,
        GameAction.GetWeaponSkillSetAction, GameAction.GetMagicSetAction, GameAction.GetSyndicateAttributesAction,
        GameAction.ReportSilentInfoAction, GameAction.GetStatisticAction,
    ];

    /// <summary>
    /// Reads the next login-ladder request, carrying <paramref name="expectedAction"/>. The 5517 client has no ladder
    /// gate: its UI and map events send other frames in between, for example 1151 subtype 2 after EnterMap
    /// (0x7648D3..0x7648E6), booth close 114 on map creation (0x61DB1E), PK mode 96 from main-dialog init (0x4BDD30)
    /// and action 408 on a map-flag bit38 change (0x732CFD). Those frames are consumed without a reply, as the
    /// historical MsgServer drops what it cannot create (sub_409CD0) and ignores unknown action subtypes
    /// (CMsgAction__Process 0x4672AE). Another ladder request out of order still fails the login. The mission catalog
    /// (1134) is validated where <paramref name="acceptMissionCatalog"/> is set, the EnterMap request and the step after.
    /// Evidence: 20_notes Network/bootstrap-tolerance-cp24d.md, including the 2026-11-06 retail-client probes.
    /// </summary>
    public static async ValueTask<GameAction> ReadAsync(ExistingCharacterLoginContext login, ushort expectedAction,
        string requestName, CancellationToken cancellationToken, bool acceptMissionCatalog = false)
    {
        ArgumentNullException.ThrowIfNull(login);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using GameInboundFrame frame = await login.Connection.ReadAsync(cancellationToken).ConfigureAwait(false)
                ?? throw ConnectionClosed(requestName);

            if (frame.PacketId == GameMissionCatalogReport.PacketIdentifier)
            {
                if (!acceptMissionCatalog)
                {
                    throw new InvalidDataException($"The mission catalog arrived after its login window, before the {requestName}.");
                }

                if (!GameMissionCatalogReport.TryParse(frame, out GameMissionCatalogReport catalog, out GameMissionCatalogReportParseError parseError))
                {
                    throw new InvalidDataException($"The mission catalog received before the {requestName} is malformed: {parseError}.");
                }

                login.MissionCatalog.Accept(catalog);
                continue;
            }

            login.MissionCatalog.EndBurst();
            if (GameLatencyTickPacket.TryParse(frame, out GameLatencyTickPacket? heartbeat))
            {
                // The client starts its 10 s heartbeat at the EnterMap reply; echo it as the historical MsgServer does.
                await login.Connection.WriteAsync(heartbeat, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (frame.PacketId != GameAction.PacketIdentifier)
            {
                continue;
            }

            // Every 5517 action builder writes the fixed 38-byte base, so a malformed 10010 still fails the login.
            GameAction request = Parse(frame, requestName);
            if (request.Action != expectedAction && Array.IndexOf(s_ladderActions, request.Action) < 0)
            {
                continue;
            }

            if (request.Action != expectedAction)
            {
                throw new InvalidDataException($"Expected {requestName} action {expectedAction}, received action {request.Action}.");
            }

            return request;
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

    private static GameAction Parse(GameInboundFrame frame, string requestName)
    {
        if (!GameAction.TryParse(frame, out GameAction request, out GameActionParseError parseError))
        {
            throw new InvalidDataException($"The frame received for the {requestName} is not a valid action packet: {parseError}.");
        }

        if (frame.Header.Length != GameAction.FixedPacketLength || request.StringCount != 0)
        {
            throw new InvalidDataException($"The {requestName} must contain the fixed action body without trailing strings.");
        }

        return request;
    }

    private static EndOfStreamException ConnectionClosed(string requestName) =>
        new($"The GameServer connection closed while awaiting the {requestName}.");
}
