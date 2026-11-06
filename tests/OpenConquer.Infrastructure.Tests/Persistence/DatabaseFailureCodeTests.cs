using System.Data.Common;
using OpenConquer.Infrastructure.Persistence;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class DatabaseFailureCodeTests
{
    private const string DriverMessage = "Duplicate entry 'SecretHero' for key 'ux_characters_name'";

    [Fact]
    public void Describe_UsesOnlyTheSqlState()
    {
        string description = DatabaseFailureCode.Describe(new DriverException(DriverMessage, "23000"));

        Assert.Equal("SQLSTATE 23000", description);
    }

    [Fact]
    public void Describe_ReportsAMissingSqlState()
    {
        Assert.Equal("SQLSTATE unavailable", DatabaseFailureCode.Describe(new DriverException(DriverMessage, sqlState: null)));
    }

    [Fact]
    public void RedactForDiagnostics_ReplacesAChainContainingADatabaseFailure()
    {
        InvalidOperationException failure = new("Character creation failed.",
            new AggregateException(new TimeoutException("unrelated"), new DriverException(DriverMessage, "23000")));

        Exception redacted = DatabaseFailureCode.RedactForDiagnostics(failure);

        Assert.NotSame(failure, redacted);
        Assert.Null(redacted.InnerException);
        Assert.Contains(nameof(InvalidOperationException), redacted.Message, StringComparison.Ordinal);
        Assert.Contains("SQLSTATE 23000", redacted.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretHero", redacted.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RedactForDiagnostics_ReturnsAnUnrelatedFailureUnchanged()
    {
        IOException failure = new("The peer closed the connection.", new TimeoutException("read"));

        Assert.Same(failure, DatabaseFailureCode.RedactForDiagnostics(failure));
    }

    private sealed class DriverException(string message, string? sqlState) : DbException(message)
    {
        public override string? SqlState => sqlState;
    }
}
