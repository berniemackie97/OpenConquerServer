using OpenConquer.Application.Characters.Creation;

namespace OpenConquer.Application.Tests.Characters.Creation;

public sealed class CharacterCreationStartingStateTests
{
    [Theory]
    [InlineData(10, 5, 2, 3, 0, 93)]
    [InlineData(20, 5, 2, 3, 0, 93)]
    [InlineData(40, 2, 7, 1, 0, 51)]
    [InlineData(50, 1, 8, 1, 0, 51)]
    [InlineData(60, 3, 1, 4, 2, 114)]
    [InlineData(100, 0, 2, 3, 5, 93)]
    public void CreateCandidate_UsesVerifiedLevelOneAllotment(
        ushort profession,
        ushort strength,
        ushort agility,
        ushort vitality,
        ushort spirit,
        ushort life)
    {
        CharacterCreationStartingState state = CreateState();

        CharacterCreationCandidate character = Assert.IsType<CharacterCreationCandidate>(
            state.CreateCandidate(42, "NewHero", 1003, profession));

        Assert.Equal((byte)profession, character.Profession);
        Assert.Equal(strength, character.Strength);
        Assert.Equal(agility, character.Agility);
        Assert.Equal(vitality, character.Vitality);
        Assert.Equal(spirit, character.Spirit);
        Assert.Equal(life, character.CurrentLife);
        Assert.Equal((ushort)0, character.CurrentMana);
    }

    [Theory]
    [InlineData(1003, 2_011_003u)]
    [InlineData(1004, 2_011_004u)]
    [InlineData(2001, 12_001u)]
    [InlineData(2002, 12_002u)]
    public void CreateCandidate_UsesHistoricalServerAppearancePolicy(
        ushort body,
        uint appearance)
    {
        CharacterCreationStartingState state = CreateState();

        CharacterCreationCandidate character = Assert.IsType<CharacterCreationCandidate>(
            state.CreateCandidate(42, "NewHero", body, 10));

        Assert.Equal(body, character.Body);
        Assert.Equal(appearance, character.AppearanceComposite);

        Assert.InRange(character.HairComposite / 100, 3, 8);
        Assert.InRange(character.HairComposite % 100, 30, 55);

        Assert.Equal(1010u, character.MapId);
        Assert.Equal((ushort)61, character.PositionX);
        Assert.Equal((ushort)101, character.PositionY);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1001)]
    [InlineData(1002)]
    [InlineData(2000)]
    [InlineData(2003)]
    [InlineData(ushort.MaxValue)]
    public void CreateCandidate_RejectsUnsupportedBodies(ushort body)
    {
        CharacterCreationStartingState state = CreateState();

        Assert.Null(state.CreateCandidate(42, "NewHero", body, 10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(30)]
    [InlineData(70)]
    [InlineData(101)]
    [InlineData(ushort.MaxValue)]
    public void CreateCandidate_RejectsUnsupportedProfessions(ushort profession)
    {
        CharacterCreationStartingState state = CreateState();

        Assert.Null(state.CreateCandidate(42, "NewHero", 1003, profession));
    }

    [Fact]
    public void Constructor_RejectsZeroStartingMap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CharacterCreationStartingState(0, 61, 101, CreateAllotments()));
    }

    [Fact]
    public void Constructor_RejectsNullAllotments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CharacterCreationStartingState(1010, 61, 101, null!));
    }

    [Fact]
    public void Constructor_RejectsIncompleteAllotments()
    {
        CharacterCreationStartingAllotment[] allotments = CreateAllotments();

        Assert.Throws<ArgumentException>(() =>
            new CharacterCreationStartingState(1010, 61, 101, allotments[..5]));
    }

    [Fact]
    public void Constructor_RejectsDuplicateFamilies()
    {
        CharacterCreationStartingAllotment[] allotments = CreateAllotments();
        allotments[5] = allotments[0];

        Assert.Throws<ArgumentException>(() =>
            new CharacterCreationStartingState(1010, 61, 101, allotments));
    }

    [Fact]
    public void Constructor_RejectsUnknownFamilies()
    {
        CharacterCreationStartingAllotment[] allotments = CreateAllotments();
        allotments[5] = new CharacterCreationStartingAllotment(11, 0, 2, 3, 5);

        Assert.Throws<ArgumentException>(() =>
            new CharacterCreationStartingState(1010, 61, 101, allotments));
    }

    [Fact]
    public void Constructor_RejectsIncorrectStartingPointBudget()
    {
        CharacterCreationStartingAllotment[] allotments = CreateAllotments();
        allotments[0] = new CharacterCreationStartingAllotment(1, 5, 2, 4, 0);

        Assert.Throws<ArgumentException>(() =>
            new CharacterCreationStartingState(1010, 61, 101, allotments));
    }

    [Fact]
    public void Constructor_DoesNotRetainMutableInputCollection()
    {
        CharacterCreationStartingAllotment[] allotments = CreateAllotments();
        CharacterCreationStartingState state = new(1010, 61, 101, allotments);

        allotments[0] = new CharacterCreationStartingAllotment(1, 10, 0, 0, 0);

        CharacterCreationCandidate character = Assert.IsType<CharacterCreationCandidate>(
            state.CreateCandidate(42, "NewHero", 1003, 10));

        Assert.Equal((ushort)5, character.Strength);
        Assert.Equal((ushort)2, character.Agility);
        Assert.Equal((ushort)3, character.Vitality);
        Assert.Equal((ushort)0, character.Spirit);
    }

    private static CharacterCreationStartingState CreateState()
    {
        return new CharacterCreationStartingState(1010, 61, 101, CreateAllotments());
    }

    private static CharacterCreationStartingAllotment[] CreateAllotments()
    {
        return
        [
            new(1, 5, 2, 3, 0),
            new(2, 5, 2, 3, 0),
            new(4, 2, 7, 1, 0),
            new(5, 1, 8, 1, 0),
            new(6, 3, 1, 4, 2),
            new(10, 0, 2, 3, 5),
        ];
    }
}
