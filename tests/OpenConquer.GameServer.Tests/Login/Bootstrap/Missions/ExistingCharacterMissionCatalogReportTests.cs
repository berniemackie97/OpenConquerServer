using OpenConquer.GameServer.Login.Bootstrap.Missions;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Bootstrap.Missions;

public sealed class ExistingCharacterMissionCatalogReportTests
{
    private static readonly GameMissionCatalogReport s_firstFull = new(80, 600, 938);
    private static readonly GameMissionCatalogReport s_secondFull = new(80, 950, 1141);
    private static readonly GameMissionCatalogReport s_lastPartial = new(64, 1142, 20001);

    [Fact]
    public void RetailMissionCatalog_MatchesTheShippedQuestInfoAndTheRecordedFrame()
    {
        int[] missionIds = RetailMissionCatalog.MissionIds;

        Assert.Equal(224, missionIds.Length);
        Assert.Equal(missionIds.Order(), missionIds);
        Assert.Equal(missionIds.Length, missionIds.Distinct().Count());
        Assert.Equal([600, 938, 950, 1141, 1142, 20001], new[] { missionIds[0], missionIds[79], missionIds[80], missionIds[159], missionIds[160], missionIds[223] });
        Assert.Equal([968, 968, 776], RetailMissionCatalog.Frames.Select(static frame => frame.Length));
        Assert.Equal("C8036E040300500058020000", Convert.ToHexString(RetailMissionCatalog.Frames[0].AsSpan(0, 12)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(80)]
    public void EndBurst_ShorterClientCatalog_IsAcceptedAndCannotRestart(int count)
    {
        ExistingCharacterMissionCatalogReport catalog = new();
        catalog.Accept(new GameMissionCatalogReport(count, 1, count));
        catalog.EndBurst();
        catalog.EndBurst();

        Assert.Throws<InvalidDataException>(() => catalog.Accept(new GameMissionCatalogReport(1, count + 1, count + 1)));
    }

    [Fact]
    public void Accept_RetailFramesInOrder_CompletesTheCatalog()
    {
        ExistingCharacterMissionCatalogReport catalog = new();

        catalog.Accept(s_firstFull);
        catalog.Accept(s_secondFull);
        catalog.Accept(s_lastPartial);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => catalog.Accept(new GameMissionCatalogReport(1, 20002, 20002)));

        Assert.Contains("second mission catalog", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Accept_FrameAfterAPartialFrame_IsRejectedAsASecondCatalog()
    {
        ExistingCharacterMissionCatalogReport catalog = new();

        catalog.Accept(new GameMissionCatalogReport(64, 600, 700));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => catalog.Accept(new GameMissionCatalogReport(1, 701, 701)));

        Assert.Contains("second mission catalog", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Accept_FrameAfterTheBurstEnded_IsRejectedAsASecondCatalog()
    {
        ExistingCharacterMissionCatalogReport catalog = new();

        catalog.Accept(s_firstFull);
        catalog.EndBurst();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => catalog.Accept(s_secondFull));

        Assert.Contains("second mission catalog", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EndBurst_BeforeAnyFrame_LeavesTheCatalogOpen()
    {
        ExistingCharacterMissionCatalogReport catalog = new();

        catalog.EndBurst();
        catalog.Accept(s_firstFull);
        catalog.EndBurst();
        catalog.EndBurst();

        Assert.Throws<InvalidDataException>(() => catalog.Accept(s_secondFull));
    }

    [Fact]
    public void Accept_MoreThanTheRetailFrameCount_IsRejected()
    {
        ExistingCharacterMissionCatalogReport catalog = new();

        for (int frame = 0; frame < ExistingCharacterMissionCatalogReport.MaximumFrameCount; frame++)
        {
            catalog.Accept(new GameMissionCatalogReport(80, 1 + (frame * 80), 80 + (frame * 80)));
        }

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => catalog.Accept(new GameMissionCatalogReport(1, 1000, 1000)));

        Assert.Contains($"exceeds {ExistingCharacterMissionCatalogReport.MaximumFrameCount} frames", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(938)]
    [InlineData(900)]
    public void Accept_FrameNotContinuingTheMissionIds_IsRejected(int firstMissionId)
    {
        ExistingCharacterMissionCatalogReport catalog = new();

        catalog.Accept(s_firstFull);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => catalog.Accept(new GameMissionCatalogReport(64, firstMissionId, 20001)));

        Assert.Contains("ascending MissionId order", exception.Message, StringComparison.Ordinal);
    }
}
