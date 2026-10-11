using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenConquer.GameServer.Hosting;

namespace OpenConquer.GameServer;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        return RunAsync(GameServerHost.Create(args));
    }

    internal static async Task<int> RunAsync(IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        GameServerFailureState failureState = host.Services.GetRequiredService<GameServerFailureState>();

        try
        {
            await host.RunAsync().ConfigureAwait(false);
        }
        catch (Exception) when (failureState.Failure is not null)
        {
            return 1;
        }

        return failureState.Failure is null ? 0 : 1;
    }
}
