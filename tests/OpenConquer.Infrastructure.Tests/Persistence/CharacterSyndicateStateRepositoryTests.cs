using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Syndicates;

namespace OpenConquer.Infrastructure.Tests.Persistence;

[Collection(GameSchemaDatabaseCollection.Name)]
public sealed class CharacterSyndicateStateRepositoryTests(GameDatabaseFixture database)
{
    private static int s_nextAccountId = 1_400_000;
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LoadAsync_Member_HydratesAuthoritativeStateAndPreservesUnsignedValues()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        (uint leaderId, string leaderName) = await InsertCharacterAsync(connection);
        (uint memberId, _) = await InsertCharacterAsync(connection);
        (uint secondMemberId, _) = await InsertCharacterAsync(connection);

        string syndicateName = CreateSyndicateName();
        ushort syndicateId = await InsertSyndicateAsync(connection, leaderId, syndicateName,
            silverFund: ulong.MaxValue, emoneyFund: uint.MaxValue, requiredLevel: byte.MaxValue,
            requiredProfession: byte.MaxValue, requiredMetempsychosis: byte.MaxValue);

        await InsertMembershipAsync(connection, memberId, syndicateId, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue);
        await InsertMembershipAsync(connection, secondMemberId, syndicateId, rank: 1, proffer: 2, positionExpiration: 3, joinDate: 4);

        ICharacterSyndicateStateRepository repository = database.Services.GetRequiredService<ICharacterSyndicateStateRepository>();

        CharacterSyndicateState state = await repository.LoadAsync(memberId, CancellationToken);

        Assert.Equal(memberId, state.CharacterId);
        Assert.True(state.HasMembership);
        Assert.True(state.Membership.HasValue);

        CharacterSyndicateMembership membership = state.Membership.Value;

        Assert.Equal(memberId, membership.CharacterId);
        Assert.Equal(syndicateId, membership.SyndicateId);
        Assert.Equal(uint.MaxValue, membership.Rank);
        Assert.Equal(uint.MaxValue, membership.Proffer);
        Assert.Equal(uint.MaxValue, membership.PositionExpirationUnixSeconds);
        Assert.Equal(uint.MaxValue, membership.JoinDateUnixSeconds);

        Assert.NotNull(state.Syndicate);

        Syndicate? syndicate = state.Syndicate;

        Assert.Equal(syndicateId, syndicate.SyndicateId);
        Assert.Equal(syndicateName, syndicate.Name);
        Assert.Equal(leaderId, syndicate.LeaderCharacterId);
        Assert.Equal(leaderName, syndicate.LeaderName);
        Assert.Equal(ulong.MaxValue, syndicate.SilverFund);
        Assert.Equal(uint.MaxValue, syndicate.EmoneyFund);
        Assert.Equal(2u, syndicate.Population);
        Assert.Equal(byte.MaxValue, syndicate.RequiredLevel);
        Assert.Equal(byte.MaxValue, syndicate.RequiredProfession);
        Assert.Equal(byte.MaxValue, syndicate.RequiredMetempsychosis);
    }

    [Fact]
    public async Task LoadAsync_CharacterWithoutMembership_ReturnsEmptyState()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        (uint characterId, _) = await InsertCharacterAsync(connection);

        ICharacterSyndicateStateRepository repository = database.Services.GetRequiredService<ICharacterSyndicateStateRepository>();
        CharacterSyndicateState state = await repository.LoadAsync(characterId, CancellationToken);

        Assert.Equal(characterId, state.CharacterId);
        Assert.False(state.HasMembership);
        Assert.Null(state.Membership);
        Assert.Null(state.Syndicate);
    }

    [Fact]
    public async Task LoadAsync_NonexistentPlayerCharacter_ReturnsEmptyState()
    {
        ICharacterSyndicateStateRepository repository = database.Services.GetRequiredService<ICharacterSyndicateStateRepository>();

        CharacterSyndicateState state = await repository.LoadAsync(uint.MaxValue, CancellationToken);

        Assert.Equal(uint.MaxValue, state.CharacterId);
        Assert.False(state.HasMembership);
        Assert.Null(state.Membership);
        Assert.Null(state.Syndicate);
    }

    [Fact]
    public async Task LoadAsync_OtherCharactersMembershipIsExcluded()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        (uint leaderId, _) = await InsertCharacterAsync(connection);
        (uint memberId, _) = await InsertCharacterAsync(connection);
        (uint unrelatedCharacterId, _) = await InsertCharacterAsync(connection);

        ushort syndicateId = await InsertSyndicateAsync(connection, leaderId, CreateSyndicateName());
        await InsertMembershipAsync(connection, memberId, syndicateId);

        ICharacterSyndicateStateRepository repository = database.Services.GetRequiredService<ICharacterSyndicateStateRepository>();
        CharacterSyndicateState state = await repository.LoadAsync(unrelatedCharacterId, CancellationToken);

        Assert.False(state.HasMembership);
        Assert.Null(state.Membership);
        Assert.Null(state.Syndicate);
    }

    [Fact]
    public async Task LoadAsync_LeaderNameIsReadFromCurrentCharacterRecord()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        (uint leaderId, _) = await InsertCharacterAsync(connection);
        (uint memberId, _) = await InsertCharacterAsync(connection);

        ushort syndicateId = await InsertSyndicateAsync(connection, leaderId, CreateSyndicateName());
        await InsertMembershipAsync(connection, memberId, syndicateId);

        string renamedLeader = "Renamed" + Guid.NewGuid().ToString("N")[..7];
        await UpdateCharacterNameAsync(connection, leaderId, renamedLeader);

        ICharacterSyndicateStateRepository repository = database.Services.GetRequiredService<ICharacterSyndicateStateRepository>();
        CharacterSyndicateState state = await repository.LoadAsync(memberId, CancellationToken);

        Assert.NotNull(state.Syndicate);
        Assert.Equal(renamedLeader, state.Syndicate.LeaderName);
        Assert.Equal(1u, state.Syndicate.Population);
    }

    [Fact]
    public async Task LoadAsync_LeaderDoesNotRequireAnInventedMembership()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        (uint leaderId, string leaderName) = await InsertCharacterAsync(connection);
        (uint memberId, _) = await InsertCharacterAsync(connection);

        ushort syndicateId = await InsertSyndicateAsync(connection, leaderId, CreateSyndicateName());
        await InsertMembershipAsync(connection, memberId, syndicateId);

        ICharacterSyndicateStateRepository repository = database.Services.GetRequiredService<ICharacterSyndicateStateRepository>();
        CharacterSyndicateState state = await repository.LoadAsync(memberId, CancellationToken);

        Assert.True(state.HasMembership);
        Assert.NotNull(state.Syndicate);
        Assert.Equal(leaderId, state.Syndicate.LeaderCharacterId);
        Assert.Equal(leaderName, state.Syndicate.LeaderName);
        Assert.Equal(1u, state.Syndicate.Population);
    }

    [Fact]
    public async Task LoadAsync_InvalidPersistedSyndicateName_FailsClosed()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        (uint leaderId, _) = await InsertCharacterAsync(connection);
        (uint memberId, _) = await InsertCharacterAsync(connection);

        string validName = CreateSyndicateName();
        ushort syndicateId = await InsertSyndicateAsync(connection, leaderId, validName);
        await InsertMembershipAsync(connection, memberId, syndicateId);
        await UpdateSyndicateNameAsync(connection, syndicateId, "Bad\nGuild");

        try
        {
            ICharacterSyndicateStateRepository repository = database.Services.GetRequiredService<ICharacterSyndicateStateRepository>();

            InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.LoadAsync(memberId, CancellationToken));

            Assert.Contains(memberId.ToString(), exception.Message, StringComparison.Ordinal);
            Assert.IsType<ArgumentException>(exception.InnerException);
        }
        finally
        {
            await UpdateSyndicateNameAsync(connection, syndicateId, validName);
        }
    }

    [Fact]
    public async Task LoadAsync_InvalidPersistedLeaderName_FailsClosed()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        (uint leaderId, string validLeaderName) = await InsertCharacterAsync(connection);
        (uint memberId, _) = await InsertCharacterAsync(connection);

        ushort syndicateId = await InsertSyndicateAsync(connection, leaderId, CreateSyndicateName());
        await InsertMembershipAsync(connection, memberId, syndicateId);
        await UpdateCharacterNameAsync(connection, leaderId, "漢Leader");

        try
        {
            ICharacterSyndicateStateRepository repository = database.Services.GetRequiredService<ICharacterSyndicateStateRepository>();

            InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.LoadAsync(memberId, CancellationToken));

            Assert.Contains(memberId.ToString(), exception.Message, StringComparison.Ordinal);
            Assert.IsType<ArgumentException>(exception.InnerException);
        }
        finally
        {
            await UpdateCharacterNameAsync(connection, leaderId, validLeaderName);
        }
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public async Task LoadAsync_NonPlayerCharacterId_IsRejected(uint characterId)
    {
        ICharacterSyndicateStateRepository repository = database.Services.GetRequiredService<ICharacterSyndicateStateRepository>();

        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await repository.LoadAsync(characterId, CancellationToken));

        Assert.Equal("characterId", exception.ParamName);
    }

    [Fact]
    public async Task LoadAsync_PreCanceledOperation_IsObserved()
    {
        ICharacterSyndicateStateRepository repository = database.Services.GetRequiredService<ICharacterSyndicateStateRepository>();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await repository.LoadAsync(CharacterIdentityPolicy.FirstPlayerEntityId, cancellation.Token));
    }

    private static string CreateSyndicateName() => "Guild" + Guid.NewGuid().ToString("N")[..8];

    private static async Task<(uint CharacterId, string Name)> InsertCharacterAsync(MySqlConnection connection)
    {
        uint accountId = checked((uint)Interlocked.Increment(ref s_nextAccountId));
        string name = "Syn" + Guid.NewGuid().ToString("N")[..10];

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `characters` (`account_id`, `name`, `appearance_composite`, `hair_composite`, `level`, `experience`,
                `strength`, `agility`, `vitality`, `spirit`, `unspent_attribute_points`, `current_life`, `current_mana`,
                `profession`, `first_profession`, `previous_profession`, `rebirth_count`, `pre_rebirth_level`, `silver`,
                `conquer_points`, `bound_conquer_points`, `pk_points`, `title_id`, `enlightenment_points`, `map_id`,
                `position_x`, `position_y`)
            VALUES (@account_id, @name, 1003, 410, 1, 0, 10, 10, 10, 10, 0, 100, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1002, 430, 378)
            """;

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;
        command.Parameters.Add("@name", MySqlDbType.VarChar).Value = name;

        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));

        return (checked((uint)command.LastInsertedId), name);
    }

    private static async Task<ushort> InsertSyndicateAsync(MySqlConnection connection, uint leaderId, string name, ulong silverFund = 0,
        uint emoneyFund = 0, byte requiredLevel = 0, byte requiredProfession = 0, byte requiredMetempsychosis = 0)
    {
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `syndicates` (`name`, `leader_character_id`, `silver_fund`, `emoney_fund`,
                `required_level`, `required_profession`, `required_metempsychosis`)
            VALUES (@name, @leader_id, @silver_fund, @emoney_fund,
                @required_level, @required_profession, @required_metempsychosis)
            """;

        command.Parameters.Add("@name", MySqlDbType.VarChar).Value = name;
        command.Parameters.Add("@leader_id", MySqlDbType.UInt32).Value = leaderId;
        command.Parameters.Add("@silver_fund", MySqlDbType.UInt64).Value = silverFund;
        command.Parameters.Add("@emoney_fund", MySqlDbType.UInt32).Value = emoneyFund;
        command.Parameters.Add("@required_level", MySqlDbType.UByte).Value = requiredLevel;
        command.Parameters.Add("@required_profession", MySqlDbType.UByte).Value = requiredProfession;
        command.Parameters.Add("@required_metempsychosis", MySqlDbType.UByte).Value = requiredMetempsychosis;

        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));

        return checked((ushort)command.LastInsertedId);
    }

    private static async Task InsertMembershipAsync(MySqlConnection connection, uint characterId, ushort syndicateId, uint rank = 0,
        uint proffer = 0, uint positionExpiration = 0, uint joinDate = 0)
    {
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `syndicate_memberships` (`character_id`, `syndicate_id`, `rank`, `proffer`,
                `position_expiration_unix_seconds`, `join_date_unix_seconds`)
            VALUES (@character_id, @syndicate_id, @rank, @proffer, @position_expiration, @join_date)
            """;

        command.Parameters.Add("@character_id", MySqlDbType.UInt32).Value = characterId;
        command.Parameters.Add("@syndicate_id", MySqlDbType.UInt16).Value = syndicateId;
        command.Parameters.Add("@rank", MySqlDbType.UInt32).Value = rank;
        command.Parameters.Add("@proffer", MySqlDbType.UInt32).Value = proffer;
        command.Parameters.Add("@position_expiration", MySqlDbType.UInt32).Value = positionExpiration;
        command.Parameters.Add("@join_date", MySqlDbType.UInt32).Value = joinDate;

        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
    }

    private static async Task UpdateCharacterNameAsync(MySqlConnection connection, uint characterId, string name)
    {
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE `characters` SET `name` = @name WHERE `character_id` = @character_id";
        command.Parameters.Add("@name", MySqlDbType.VarChar).Value = name;
        command.Parameters.Add("@character_id", MySqlDbType.UInt32).Value = characterId;

        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
    }

    private static async Task UpdateSyndicateNameAsync(MySqlConnection connection, ushort syndicateId, string name)
    {
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE `syndicates` SET `name` = @name WHERE `syndicate_id` = @syndicate_id";
        command.Parameters.Add("@name", MySqlDbType.VarChar).Value = name;
        command.Parameters.Add("@syndicate_id", MySqlDbType.UInt16).Value = syndicateId;

        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
    }
}
