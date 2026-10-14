namespace OpenConquer.GameServer.Configuration;

internal sealed class RealmStatusOptions
{
    private const int ReportsPerLifetime = 3;

    private static readonly TimeSpan s_minimumReportInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_maximumReportInterval = TimeSpan.FromMinutes(5);

    public RealmStatusOptions(TimeSpan reportInterval)
    {
        if (reportInterval < s_minimumReportInterval || reportInterval > s_maximumReportInterval)
        {
            throw new ArgumentOutOfRangeException(nameof(reportInterval), $"The realm status report interval must be between {s_minimumReportInterval} and {s_maximumReportInterval}.");
        }

        ReportInterval = reportInterval;
        ReportLifetime = reportInterval * ReportsPerLifetime;
    }

    public TimeSpan ReportInterval { get; }

    /// <summary>
    /// Gets how long one report stays valid. It outlasts two missed reports, so a single slow
    /// database write does not make a running realm look down.
    /// </summary>
    public TimeSpan ReportLifetime { get; }
}
