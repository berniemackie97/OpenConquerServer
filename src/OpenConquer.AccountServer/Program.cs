using Microsoft.Extensions.Hosting;
using OpenConquer.AccountServer.Hosting;

namespace OpenConquer.AccountServer;

internal static class Program
{
    public static Task Main(string[] args) => AccountServerHost.Create(args).RunAsync();
}
