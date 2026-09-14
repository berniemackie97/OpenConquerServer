using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Characters.Login;
using OpenConquer.Domain.Characters;

namespace OpenConquer.Infrastructure.Tests.Persistence;

[Collection(GameSchemaDatabaseCollection.Name)]
public sealed class CharacterLoginProfileRepositoryTests(GameDatabaseFixture database)
{
    private static int s_nextAccountId = 100_000;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FindByAccountIdAsync_ExistingCharacter_MapsCompletePersistedProfile()
    {
        uint accountId = CreateAccountId();
        string name = CreateCharacterName();

        uint characterId = await InsertCharacterAsync(accountId, name);

        ICharacterLoginProfileRepository repository =
            database.Services.GetRequiredService<ICharacterLoginProfileRepository>();

        CharacterLoginProfile profile = Assert.IsType<CharacterLoginProfile>(
            await repository.FindByAccountIdAsync(accountId, CancellationToken)
        );

        Assert.Equal(characterId, profile.Identity.CharacterId);
        Assert.Equal(accountId, profile.Identity.AccountId);
        Assert.Equal(name, profile.Identity.Name);

        Assert.Equal(2_002_001u, profile.Appearance.Composite);
        Assert.Equal((ushort)456, profile.Appearance.Hair);

        Assert.Equal((byte)140, profile.Progression.Level);
        Assert.Equal(12_345_678_901UL, profile.Progression.Experience);
        Assert.Equal((byte)135, profile.Progression.Profession);
        Assert.Equal((byte)10, profile.Progression.FirstProfession);
        Assert.Equal((byte)20, profile.Progression.PreviousProfession);
        Assert.Equal((byte)2, profile.Progression.RebirthCount);

        Assert.Equal((ushort)101, profile.Attributes.Strength);
        Assert.Equal((ushort)102, profile.Attributes.Agility);
        Assert.Equal((ushort)103, profile.Attributes.Vitality);
        Assert.Equal((ushort)104, profile.Attributes.Spirit);
        Assert.Equal((ushort)105, profile.Attributes.UnspentPoints);

        Assert.Equal((ushort)106, profile.Vitals.Life);
        Assert.Equal((ushort)107, profile.Vitals.Mana);

        Assert.Equal(1_234_567_890u, profile.Economy.Silver);
        Assert.Equal(2_345_678_901u, profile.Economy.ConquerPoints);
        Assert.Equal(3_456_789_012u, profile.Economy.BoundConquerPoints);

        Assert.Equal((ushort)321, profile.PkPoints);
        Assert.Equal((ushort)654, profile.TitleId);
        Assert.Equal((ushort)987, profile.EnlightenmentPoints);

        Assert.Equal(1002u, profile.Location.MapId);
        Assert.Equal((ushort)431, profile.Location.X);
        Assert.Equal((ushort)379, profile.Location.Y);
    }

    [Fact]
    public async Task FindByAccountIdAsync_UnknownAccount_ReturnsNull()
    {
        ICharacterLoginProfileRepository repository =
            database.Services.GetRequiredService<ICharacterLoginProfileRepository>();

        CharacterLoginProfile? profile = await repository.FindByAccountIdAsync(
            CreateAccountId(),
            CancellationToken
        );

        Assert.Null(profile);
    }

    [Fact]
    public async Task FindByAccountIdAsync_ZeroAccountId_IsRejected()
    {
        ICharacterLoginProfileRepository repository =
            database.Services.GetRequiredService<ICharacterLoginProfileRepository>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await repository.FindByAccountIdAsync(0, CancellationToken)
        );
    }

    [Fact]
    public async Task FindByAccountIdAsync_PreCanceledOperation_IsObserved()
    {
        ICharacterLoginProfileRepository repository =
            database.Services.GetRequiredService<ICharacterLoginProfileRepository>();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await repository.FindByAccountIdAsync(CreateAccountId(), cancellation.Token)
        );
    }

    [Fact]
    public async Task FindByAccountIdAsync_PersistedNonPlayerEntityId_IsRejected()
    {
        uint accountId = CreateAccountId();
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId - 1;

        await InsertCharacterAsync(accountId, CreateCharacterName(), characterId);

        try
        {
            ICharacterLoginProfileRepository repository = database.Services.GetRequiredService<ICharacterLoginProfileRepository>();

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await repository.FindByAccountIdAsync(accountId, CancellationToken));
        }
        finally
        {
            await DeleteCharacterAsync(characterId);
        }
    }

    [Fact]
    public async Task RuntimeIdentity_CanReadCharactersButCannotWriteThem()
    {
        uint accountId = CreateAccountId();

        await InsertCharacterAsync(accountId, CreateCharacterName());

        ICharacterLoginProfileRepository repository =
            database.Services.GetRequiredService<ICharacterLoginProfileRepository>();

        Assert.NotNull(await repository.FindByAccountIdAsync(accountId, CancellationToken));

        await using MySqlConnection connection = new(database.RuntimeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM `characters` WHERE `account_id` = @account_id";
        command.Parameters.AddWithValue("@account_id", accountId);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            command.ExecuteNonQueryAsync(CancellationToken)
        );

        Assert.Equal(1142, exception.Number);
    }

    private async Task DeleteCharacterAsync(uint characterId)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM `characters` WHERE `character_id` = @character_id";
        command.Parameters.AddWithValue("@character_id", characterId);

        int affected = await command.ExecuteNonQueryAsync(CancellationToken);

        Assert.Equal(1, affected);
    }

    private async Task<uint> InsertCharacterAsync(uint accountId, string name, uint? characterId = null)
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = characterId is null
            ? """
                INSERT INTO `characters`
                    (`account_id`,
                     `name`,
                     `appearance_composite`,
                     `hair_composite`,
                     `level`,
                     `experience`,
                     `strength`,
                     `agility`,
                     `vitality`,
                     `spirit`,
                     `unspent_attribute_points`,
                     `current_life`,
                     `current_mana`,
                     `profession`,
                     `first_profession`,
                     `previous_profession`,
                     `rebirth_count`,
                     `silver`,
                     `conquer_points`,
                     `bound_conquer_points`,
                     `pk_points`,
                     `title_id`,
                     `enlightenment_points`,
                     `map_id`,
                     `position_x`,
                     `position_y`)
                VALUES
                    (@account_id,
                     @name,
                     2002001,
                     456,
                     140,
                     12345678901,
                     101,
                     102,
                     103,
                     104,
                     105,
                     106,
                     107,
                     135,
                     10,
                     20,
                     2,
                     1234567890,
                     2345678901,
                     3456789012,
                     321,
                     654,
                     987,
                     1002,
                     431,
                     379)
                """
            : """
                INSERT INTO `characters`
                    (`character_id`,
                     `account_id`,
                     `name`,
                     `appearance_composite`,
                     `hair_composite`,
                     `level`,
                     `experience`,
                     `strength`,
                     `agility`,
                     `vitality`,
                     `spirit`,
                     `unspent_attribute_points`,
                     `current_life`,
                     `current_mana`,
                     `profession`,
                     `first_profession`,
                     `previous_profession`,
                     `rebirth_count`,
                     `silver`,
                     `conquer_points`,
                     `bound_conquer_points`,
                     `pk_points`,
                     `title_id`,
                     `enlightenment_points`,
                     `map_id`,
                     `position_x`,
                     `position_y`)
                VALUES
                    (@character_id,
                     @account_id,
                     @name,
                     2002001,
                     456,
                     140,
                     12345678901,
                     101,
                     102,
                     103,
                     104,
                     105,
                     106,
                     107,
                     135,
                     10,
                     20,
                     2,
                     1234567890,
                     2345678901,
                     3456789012,
                     321,
                     654,
                     987,
                     1002,
                     431,
                     379)
                """;

        if (characterId is not null)
        {
            command.Parameters.AddWithValue("@character_id", characterId.Value);
        }

        command.Parameters.AddWithValue("@account_id", accountId);
        command.Parameters.AddWithValue("@name", name);

        int affected = await command.ExecuteNonQueryAsync(CancellationToken);

        Assert.Equal(1, affected);

        return characterId ?? checked((uint)command.LastInsertedId);
    }

    private static uint CreateAccountId()
    {
        return checked((uint)Interlocked.Increment(ref s_nextAccountId));
    }

    private static string CreateCharacterName()
    {
        return "Repo" + Guid.NewGuid().ToString("N")[..11];
    }
}
