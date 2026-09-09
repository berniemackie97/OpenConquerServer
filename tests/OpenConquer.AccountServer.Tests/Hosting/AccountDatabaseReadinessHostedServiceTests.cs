using Microsoft.Extensions.Logging.Abstractions;
using OpenConquer.AccountServer.Hosting;
using OpenConquer.Infrastructure.Persistence.Accounts.Readiness;

namespace OpenConquer.AccountServer.Tests.Hosting;

public sealed class AccountDatabaseReadinessHostedServiceTests
{
    [Fact]
    public void Constructor_RejectsMissingDependencies()
    {
        ControlledReadinessVerifier verifier = new();

        Assert.Throws<ArgumentNullException>(() => new AccountDatabaseReadinessHostedService(null!, NullLogger<AccountDatabaseReadinessHostedService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new AccountDatabaseReadinessHostedService(verifier, null!));
    }

    [Fact]
    public async Task StartAsync_DoesNotCompleteUntilReadinessVerificationCompletes()
    {
        ControlledReadinessVerifier verifier = new();
        AccountDatabaseReadinessHostedService service = new(verifier, NullLogger<AccountDatabaseReadinessHostedService>.Instance);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task start = service.StartAsync(cancellation.Token);

        await verifier.Started.WaitAsync(TestContext.Current.CancellationToken);

        Assert.False(start.IsCompleted);
        Assert.Equal(cancellation.Token, verifier.ObservedCancellationToken);

        verifier.Complete();

        await start.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartAsync_PropagatesReadinessFailure()
    {
        InvalidOperationException expected = new("database readiness failed");
        AccountDatabaseReadinessHostedService service = new(new FailingReadinessVerifier(expected), NullLogger<AccountDatabaseReadinessHostedService>.Instance);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(TestContext.Current.CancellationToken));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task StartAsync_PropagatesCallerCancellation()
    {
        ControlledReadinessVerifier verifier = new();
        AccountDatabaseReadinessHostedService service = new(verifier, NullLogger<AccountDatabaseReadinessHostedService>.Instance);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task start = service.StartAsync(cancellation.Token);

        await verifier.Started.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
    }

    private sealed class ControlledReadinessVerifier : IAccountDatabaseReadinessVerifier
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public CancellationToken ObservedCancellationToken { get; private set; }

        public async Task VerifyAsync(CancellationToken cancellationToken = default)
        {
            ObservedCancellationToken = cancellationToken;
            _started.TrySetResult();

            await _completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete() => _completion.TrySetResult();
    }

    private sealed class FailingReadinessVerifier(Exception exception) : IAccountDatabaseReadinessVerifier
    {
        private readonly Exception _exception = exception ?? throw new ArgumentNullException(nameof(exception));

        public Task VerifyAsync(CancellationToken cancellationToken = default) => Task.FromException(_exception);
    }
}
