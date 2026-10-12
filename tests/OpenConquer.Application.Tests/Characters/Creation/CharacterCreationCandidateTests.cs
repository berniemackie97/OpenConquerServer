using OpenConquer.Application.Characters.Creation;

namespace OpenConquer.Application.Tests.Characters.Creation;

public sealed class CharacterCreationCandidateTests
{
    [Fact]
    public void Constructor_AcceptsValidStartingState()
    {
        CharacterCreationCandidate character = CreateCandidate();

        Assert.Equal(1u, character.AccountId);
        Assert.Equal("NewHero", character.Name);
        Assert.Equal((ushort)1003, character.Body);
        Assert.Equal((byte)10, character.Profession);
        Assert.Equal((byte)1, CharacterCreationCandidate.StartingLevel);
        Assert.Equal((ushort)114, character.CurrentLife);
        Assert.Equal((ushort)0, character.CurrentMana);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(50)]
    [InlineData(60)]
    [InlineData(100)]
    public void Constructor_AcceptsNativeStartingProfessions(ushort profession)
    {
        CharacterCreationCandidate character = CreateCandidate(profession: profession);

        Assert.Equal((byte)profession, character.Profession);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(30)]
    [InlineData(70)]
    [InlineData(101)]
    [InlineData(ushort.MaxValue)]
    public void Constructor_RejectsUnsupportedStartingProfessions(ushort profession)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateCandidate(profession: profession));
    }

    [Theory]
    [InlineData(1003)]
    [InlineData(1004)]
    [InlineData(2001)]
    [InlineData(2002)]
    public void Constructor_AcceptsNativeStartingBodies(ushort body)
    {
        CharacterCreationCandidate character = CreateCandidate(body: body);

        Assert.Equal(body, character.Body);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    [InlineData(1002)]
    [InlineData(2000)]
    [InlineData(ushort.MaxValue)]
    public void Constructor_RejectsUnsupportedStartingBodies(ushort body)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateCandidate(body: body));
    }

    [Fact]
    public void Constructor_RejectsZeroAccountIdentity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateCandidate(accountId: 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("SixteenLettersXX")]
    [InlineData("Bad Name")]
    [InlineData("Bad;Name")]
    public void Constructor_RejectsInvalidCharacterNames(string name)
    {
        Assert.Throws<ArgumentException>(() => CreateCandidate(name: name));
    }

    [Fact]
    public void Constructor_RejectsAppearanceThatDoesNotMatchBody()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CharacterCreationCandidate(1, "NewHero", 1003, 10, 2_012_001, 456,
                4, 1, 4, 1, 0, 1002, 431, 379));
    }

    [Fact]
    public void Constructor_RejectsIncompleteStartingAttributeBudget()
    {
        Assert.Throws<ArgumentException>(() =>
            new CharacterCreationCandidate(1, "NewHero", 1003, 10, 2_011_003, 456,
                4, 1, 3, 1, 0, 1002, 431, 379));
    }

    [Fact]
    public void Constructor_RejectsExcessStartingAttributeBudget()
    {
        Assert.Throws<ArgumentException>(() =>
            new CharacterCreationCandidate(1, "NewHero", 1003, 10, 2_011_003, 456,
                4, 1, 5, 1, 0, 1002, 431, 379));
    }

    [Fact]
    public void Constructor_RejectsMissingStartingMap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CharacterCreationCandidate(1, "NewHero", 1003, 10, 2_011_003, 456,
                4, 1, 4, 1, 0, 0, 431, 379));
    }

    private static CharacterCreationCandidate CreateCandidate(uint accountId = 1, string name = "NewHero", ushort body = 1003, ushort profession = 10)
    {
        return new CharacterCreationCandidate(accountId, name, body, profession, 2_010_000u + body, 456,
            4, 1, 4, 1, 0, 1002, 431, 379);
    }
}
