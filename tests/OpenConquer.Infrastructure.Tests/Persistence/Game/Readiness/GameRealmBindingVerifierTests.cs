using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Domain.Realms;
using OpenConquer.Infrastructure.Persistence.Game.Readiness;

namespace OpenConquer.Infrastructure.Tests.Persistence.Game.Readiness;

[Collection(GameSchemaDatabaseCollection.Name)]
public sealed class GameRealmBindingVerifierTests(GameDatabaseFixture database)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;
    private IGameRealmBindingVerifier Verifier => database.Services.GetRequiredService<IGameRealmBindingVerifier>();

    [Fact]
    public async Task VerifyAsync_RequiresExplicitPersistedRealmBinding()
    {
        await ClearBindingAsync();
        GameDatabaseReadinessException failure = await Assert.ThrowsAsync<GameDatabaseReadinessException>(
            () => Verifier.VerifyAsync(new RealmId(1), CancellationToken));
        Assert.Contains("no provisioned realm identity", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_AcceptsExactRealmAndRejectsAnotherRealm()
    {
        await ClearBindingAsync();
        try
        {
            await ExecuteAsync("INSERT INTO `realm_binding` (`binding_key`, `realm_id`) VALUES (1, 101)");
            await Verifier.VerifyAsync(new RealmId(101), CancellationToken);
            GameDatabaseReadinessException failure = await Assert.ThrowsAsync<GameDatabaseReadinessException>(
                () => Verifier.VerifyAsync(new RealmId(102), CancellationToken));
            Assert.Contains("bound to realm 101", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            await ClearBindingAsync();
        }
    }

    [Fact]
    public async Task RealmBinding_EnforcesPositiveIdentityAndExactlyOneKey()
    {
        await ClearBindingAsync();
        try
        {
            MySqlException invalidIdentity = await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(
                "INSERT INTO `realm_binding` (`binding_key`, `realm_id`) VALUES (1, 0)"));
            Assert.Equal(3819, invalidIdentity.Number);

            MySqlException invalidKey = await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(
                "INSERT INTO `realm_binding` (`binding_key`, `realm_id`) VALUES (2, 101)"));
            Assert.Equal(3819, invalidKey.Number);

            await ExecuteAsync("INSERT INTO `realm_binding` (`binding_key`, `realm_id`) VALUES (1, 101)");
            MySqlException duplicate = await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(
                "INSERT INTO `realm_binding` (`binding_key`, `realm_id`) VALUES (1, 102)"));
            Assert.Equal(1062, duplicate.Number);
        }
        finally
        {
            await ClearBindingAsync();
        }
    }

    [Fact]
    public async Task RuntimeIdentity_CannotChangeRealmBinding()
    {
        await ClearBindingAsync();
        try
        {
            await ExecuteAsync("INSERT INTO `realm_binding` (`binding_key`, `realm_id`) VALUES (1, 101)");
            await using MySqlConnection connection = new(database.RuntimeConnectionString);
            await connection.OpenAsync(CancellationToken);
            await using MySqlCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE `realm_binding` SET `realm_id` = 102 WHERE `binding_key` = 1";
            MySqlException failure = await Assert.ThrowsAsync<MySqlException>(() => command.ExecuteNonQueryAsync(CancellationToken));
            Assert.Equal(1142, failure.Number);
            await Verifier.VerifyAsync(new RealmId(101), CancellationToken);
        }
        finally
        {
            await ClearBindingAsync();
        }
    }

    [Fact]
    public async Task VerifyAsync_RejectsUnsetExpectedRealmAndHonorsCancellation()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Verifier.VerifyAsync(default, CancellationToken));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Verifier.VerifyAsync(new RealmId(1), cancellation.Token));
    }

    private Task ClearBindingAsync() => ExecuteAsync("DELETE FROM `realm_binding`");

    private async Task ExecuteAsync(string sql)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
