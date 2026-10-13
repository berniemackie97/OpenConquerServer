using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Bootstrap.Reports;

/// <summary>
/// The statistics request is the client's last login request. It has no response.
/// </summary>
internal sealed class ExistingCharacterStatisticsRequestProcessor
{
    public async ValueTask ProcessAsync(ExistingCharacterLoginContext login, CancellationToken cancellationToken = default)
    {
        GameAction request = await LoginActionRequest.ReadAsync(login.Connection, GameAction.GetStatisticAction, "statistics request", cancellationToken)
            .ConfigureAwait(false);

        LoginActionRequest.RequireCharacterRequest(request, login.CharacterId, "statistics request");

        cancellationToken.ThrowIfCancellationRequested();
    }
}
