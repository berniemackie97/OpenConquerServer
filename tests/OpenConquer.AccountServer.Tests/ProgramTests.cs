using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenConquer.AccountServer.Hosting;

namespace OpenConquer.AccountServer.Tests;

public sealed class ProgramTests
{
    [Fact]
    public async Task RunAsync_RejectsMissingHost()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Program.RunAsync(null!));
    }

    [Fact]
    public async Task RunAsync_ReturnsZeroForNormalHostShutdown()
    {
        FatalBackgroundServiceFailureState fatalFailureState = new();
        IHost host = CreateHost<StoppingBackgroundService>(fatalFailureState);

        int exitCode = await Program.RunAsync(host).WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Null(fatalFailureState.Failure);
    }

    [Fact]
    public async Task RunAsync_ReturnsOneWhenBackgroundServiceRecordsFatalFailure()
    {
        FatalBackgroundServiceFailureState fatalFailureState = new();
        IHost host = CreateHost<FailingBackgroundService>(fatalFailureState);

        int exitCode = await Program.RunAsync(host).WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);

        InvalidOperationException failure = Assert.IsType<InvalidOperationException>(fatalFailureState.Failure);
        Assert.Equal("fatal background failure", failure.Message);
    }

    private static IHost CreateHost<TBackgroundService>(FatalBackgroundServiceFailureState fatalFailureState)
        where TBackgroundService : class, IHostedService
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);

        builder.Services.Configure<HostOptions>(options =>
        {
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
        });

        builder.Services.AddSingleton(fatalFailureState);
        builder.Services.AddHostedService<TBackgroundService>();

        return builder.Build();
    }

    private sealed class StoppingBackgroundService(IHostApplicationLifetime applicationLifetime) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            stoppingToken.ThrowIfCancellationRequested();
            applicationLifetime.StopApplication();
        }
    }

    private sealed class FailingBackgroundService(FatalBackgroundServiceFailureState fatalFailureState) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            stoppingToken.ThrowIfCancellationRequested();

            InvalidOperationException failure = new("fatal background failure");

            fatalFailureState.Record(failure);
            throw failure;
        }
    }
}
