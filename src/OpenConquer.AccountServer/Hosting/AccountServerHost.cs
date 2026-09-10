using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenConquer.AccountServer.Configuration;
using OpenConquer.AccountServer.Login.Handshake;
using OpenConquer.AccountServer.Login.Hosting;
using OpenConquer.AccountServer.Login.Observability;
using OpenConquer.AccountServer.Login.Workers;
using OpenConquer.AccountServer.Maintenance;
using OpenConquer.Infrastructure.Accounts.Extensions;
using OpenConquer.Transport.Admission;
using OpenConquer.Transport.Sockets;

namespace OpenConquer.AccountServer.Hosting;

internal static class AccountServerHost
{
    private static readonly TimeSpan s_startupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_shutdownTimeout = TimeSpan.FromSeconds(30);

    public static IHost Create(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        AccountServerConfiguration configuration = AccountServerConfiguration.Load(builder.Configuration);

        ConfigureServices(builder.Services, configuration);

        return builder.Build();
    }

    internal static void ConfigureServices(IServiceCollection services, AccountServerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        IPEndPoint loginEndPoint = new(configuration.LoginEndPoint.Address, configuration.LoginEndPoint.Port);
        int listenBacklog = configuration.ListenBacklog;
        int admissionCapacity = configuration.AdmissionCapacity;

        services.AddAccountLoginInfrastructure(configuration.AccountConnectionString, configuration.AuthenticationProtection, configuration.ActiveVerificationKeyId, configuration.EncodedVerificationKeys);

        services.Configure<HostOptions>(options =>
        {
            options.StartupTimeout = s_startupTimeout;
            options.ShutdownTimeout = s_shutdownTimeout;
            options.ServicesStartConcurrently = false;
            options.ServicesStopConcurrently = false;
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
        });

        services.AddMetrics();

        services.AddSingleton(configuration.WorkerPool);
        services.AddSingleton(configuration.Handshake);
        services.AddSingleton(configuration.TicketCleanup);

        services.AddSingleton<ILoginSeedGenerator, CryptographicLoginSeedGenerator>();
        services.AddSingleton<LoginHandshakeProcessor>();
        services.AddSingleton<LoginRuntimeMetrics>();

        services.AddSingleton<TransportConnectionAdmissionQueue>(_ => new TransportConnectionAdmissionQueue(admissionCapacity));
        services.AddSingleton<LoginTransportListenerFactory>(_ => () => new SocketTransportListener(loginEndPoint, listenBacklog));

        services.AddHostedService<AccountDatabaseReadinessHostedService>();
        services.AddHostedService<GameLoginTicketCleanupHostedService>();
        services.AddHostedService<LoginRuntimeHostedService>();
    }
}
