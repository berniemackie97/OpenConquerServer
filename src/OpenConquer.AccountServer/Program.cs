using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenConquer.AccountServer.Hosting;

namespace OpenConquer.AccountServer;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        return RunAsync(AccountServerHost.Create(args));
    }

    internal static async Task<int> RunAsync(IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        FatalBackgroundServiceFailureState fatalFailureState = host.Services.GetRequiredService<FatalBackgroundServiceFailureState>();

        await host.RunAsync().ConfigureAwait(false);

        return fatalFailureState.Failure is null ? 0 : 1;
    }
}
