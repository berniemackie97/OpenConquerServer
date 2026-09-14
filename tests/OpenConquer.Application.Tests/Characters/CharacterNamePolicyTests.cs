using OpenConquer.Domain.Characters;

namespace OpenConquer.Application.Tests.Characters;

public sealed class CharacterNamePolicyTests
{
    [Theory]
    [InlineData("Hero")]
    [InlineData("Café")]
    [InlineData("123456789012345")]
    public void IsValid_CompatibleName_ReturnsTrue(string name)
    {
        Assert.True(CharacterNamePolicy.IsValid(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1234567890123456")]
    [InlineData("Bad Name")]
    [InlineData("Bad;Name")]
    [InlineData("Bad/Name")]
    [InlineData("Bad\\Name")]
    [InlineData("Bad=Name")]
    [InlineData("Bad%Name")]
    [InlineData("Bad@Name")]
    [InlineData("Bad'Name")]
    [InlineData("Bad\"Name")]
    [InlineData("Bad[Name")]
    [InlineData("Bad]Name")]
    [InlineData("A\0BC")]
    [InlineData("A\u001FBC")]
    [InlineData("A漢BC")]
    public void IsValid_IncompatibleName_ReturnsFalse(string? name)
    {
        Assert.False(CharacterNamePolicy.IsValid(name));
    }
}
