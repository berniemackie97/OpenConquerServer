using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenConquer.GameServer.Hosting;

namespace OpenConquer.GameServer.Tests.Hosting;

public sealed class GameServerProgramTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task RunAsync_ReportsFatalRuntimeStateThroughProcessExitCode(bool fatal, int expectedExitCode)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);
        builder.Services.AddSingleton<FatalBackgroundServiceFailureState>();

        IHost host = builder.Build();
        IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        FatalBackgroundServiceFailureState failureState = host.Services.GetRequiredService<FatalBackgroundServiceFailureState>();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenRegistration registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());

        Task<int> running = OpenConquer.GameServer.Program.RunAsync(host);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

        if (fatal)
        {
            failureState.Record(new InvalidOperationException("The world runtime terminated unexpectedly."));
        }

        lifetime.StopApplication();

        Assert.Equal(expectedExitCode, await running.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken));
    }
}
