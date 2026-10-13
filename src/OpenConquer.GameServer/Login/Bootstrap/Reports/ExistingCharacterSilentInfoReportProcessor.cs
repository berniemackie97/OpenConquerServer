using OpenConquer.GameServer.Login.Character;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Bootstrap.Reports;

/// <summary>
/// The report carries a client checksum and data version. Neither is trusted; both are echoed in the acknowledgement and kept for the world session.
/// </summary>
internal sealed class ExistingCharacterSilentInfoReportProcessor
{
    public async ValueTask ProcessAsync(ExistingCharacterLoginContext login, CancellationToken cancellationToken = default)
    {
        ExistingCharacterGameConnection connection = login.Connection;
        GameAction report = await LoginActionRequest.ReadAsync(connection, GameAction.ReportSilentInfoAction, "silent-info report", cancellationToken)
            .ConfigureAwait(false);

        if (report.EntityId != 0)
        {
            throw new InvalidDataException("The silent-info report requires a zero entity ID.");
        }

        if (report.ActionParameter != 0 || report.Direction != 0 || report.PositionX != 0
            || report.PositionY != 0 || report.Data1 != 0 || report.Data2 != 0 || report.Flag != 0)
        {
            throw new InvalidDataException("The silent-info report requires all fields other than its checksum, version, and action to be zero.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        GameActionPacket acknowledgement = new(login.CharacterId, report.ParameterPair,
            actionParameter: 0, report.Timestamp, GameAction.ReportSilentInfoAction, direction: 0,
            positionX: 0, positionY: 0, data1: 0, data2: 0, flag: 0);

        await connection.WriteAsync(acknowledgement, cancellationToken).ConfigureAwait(false);

        login.ClientReportedSilentDataChecksum = report.ParameterPair;
        login.ClientReportedSilentDataVersion = report.Timestamp;
    }
}
