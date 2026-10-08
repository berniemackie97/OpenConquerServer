using OpenConquer.Domain.Syndicates;

namespace OpenConquer.Domain.Tests.Syndicates;

public sealed class SyndicateNamePolicyTests
{
    [Fact]
    public void IsValid_RepresentableName_ReturnsTrue()
    {
        Assert.True(SyndicateNamePolicy.IsValid("OpenConquer"));
    }

    [Fact]
    public void IsValid_MaximumEncodedLength_ReturnsTrue()
    {
        Assert.True(SyndicateNamePolicy.IsValid("1234567890123456"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsValid_MissingName_ReturnsFalse(string? name)
    {
        Assert.False(SyndicateNamePolicy.IsValid(name));
    }

    [Fact]
    public void IsValid_NameAboveMaximumEncodedLength_ReturnsFalse()
    {
        Assert.False(SyndicateNamePolicy.IsValid("12345678901234567"));
    }

    [Fact]
    public void IsValid_ControlCharacter_ReturnsFalse()
    {
        Assert.False(SyndicateNamePolicy.IsValid("Open\nConquer"));
        Assert.False(SyndicateNamePolicy.IsValid("Open\0Conquer"));
        Assert.False(SyndicateNamePolicy.IsValid("Open\u007FConquer"));
        Assert.False(SyndicateNamePolicy.IsValid("Open\u009FConquer"));
    }

    [Fact]
    public void IsValid_TextOutsideWindows1252_ReturnsFalse()
    {
        Assert.False(SyndicateNamePolicy.IsValid("Open漢Conquer"));
    }

    [Fact]
    public void IsValid_Windows1252Text_UsesEncodedByteLength()
    {
        Assert.True(SyndicateNamePolicy.IsValid("€€€€€€€€€€€€€€€€"));
        Assert.False(SyndicateNamePolicy.IsValid("€€€€€€€€€€€€€€€€€"));
    }
}
