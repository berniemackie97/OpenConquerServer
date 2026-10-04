using OpenConquer.Domain.Skills;

namespace OpenConquer.Domain.Tests.Skills;

public sealed class WeaponSkillExperienceCurveTests
{
    [Fact]
    public void MaximumLevel_MatchesVerifiedNativeLimit()
    {
        Assert.Equal((byte)20, WeaponSkillExperienceCurve.MaximumLevel);
    }

    [Theory]
    [InlineData((byte)0, 0u)]
    [InlineData((byte)1, 1_200u)]
    [InlineData((byte)2, 68_000u)]
    [InlineData((byte)3, 250_000u)]
    [InlineData((byte)4, 640_000u)]
    [InlineData((byte)5, 1_600_000u)]
    [InlineData((byte)6, 4_000_000u)]
    [InlineData((byte)7, 10_000_000u)]
    [InlineData((byte)8, 22_000_000u)]
    [InlineData((byte)9, 40_000_000u)]
    [InlineData((byte)10, 90_000_000u)]
    [InlineData((byte)11, 95_000_000u)]
    [InlineData((byte)12, 142_500_000u)]
    [InlineData((byte)13, 213_750_000u)]
    [InlineData((byte)14, 320_625_000u)]
    [InlineData((byte)15, 480_937_500u)]
    [InlineData((byte)16, 721_406_250u)]
    [InlineData((byte)17, 1_082_109_375u)]
    [InlineData((byte)18, 1_623_164_063u)]
    [InlineData((byte)19, 2_100_000_000u)]
    [InlineData((byte)20, 0u)]
    public void GetNextLevelExperienceRequirement_ValidLevel_ReturnsVerifiedRequirement(byte currentLevel, uint expected)
    {
        Assert.Equal(expected, WeaponSkillExperienceCurve.GetNextLevelExperienceRequirement(currentLevel));
    }

    [Theory]
    [InlineData((byte)21)]
    [InlineData(byte.MaxValue)]
    public void GetNextLevelExperienceRequirement_LevelAboveMaximum_ThrowsArgumentOutOfRangeException(byte currentLevel)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            WeaponSkillExperienceCurve.GetNextLevelExperienceRequirement(currentLevel));

        Assert.Equal("currentLevel", exception.ParamName);
    }
}
