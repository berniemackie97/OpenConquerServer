using OpenConquer.AccountServer.Hosting;

namespace OpenConquer.AccountServer.Tests.Hosting;

public sealed class FatalBackgroundServiceFailureStateTests
{
    [Fact]
    public void Record_RejectsNullFailure()
    {
        FatalBackgroundServiceFailureState state = new();

        Assert.Throws<ArgumentNullException>(() => state.Record(null!));
        Assert.Null(state.Failure);
    }

    [Fact]
    public void Record_PreservesFirstFailure()
    {
        FatalBackgroundServiceFailureState state = new();
        InvalidOperationException first = new("first");
        IOException second = new("second");

        state.Record(first);
        state.Record(second);

        Assert.Same(first, state.Failure);
    }

    [Fact]
    public async Task Record_IsThreadSafeAndRetainsSingleFailure()
    {
        FatalBackgroundServiceFailureState state = new();
        Exception[] failures = Enumerable.Range(0, 32)
            .Select(static index => new InvalidOperationException($"failure-{index}"))
            .ToArray();

        await Task.WhenAll(failures.Select(failure => Task.Run(() => state.Record(failure), TestContext.Current.CancellationToken)));

        Exception recorded = Assert.IsType<InvalidOperationException>(state.Failure);

        Assert.Contains(recorded, failures);

        state.Record(new IOException("later"));

        Assert.Same(recorded, state.Failure);
    }
}
