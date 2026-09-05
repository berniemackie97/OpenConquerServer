namespace OpenConquer.AccountServer.Login.Handshake;

/// <summary>
/// Represents the result of consuming the native post authentication AccountServer report sequence.
/// </summary>
internal sealed class LoginPostAuthenticationReportReadResult
{
    private LoginPostAuthenticationReportReadResult(LoginPostAuthenticationReportReadStatus status, LoginPostAuthenticationReportPhase? failurePhase, LoginPostAuthenticationReports? reports, ushort? unexpectedPacketId)
    {
        Status = status;
        FailurePhase = failurePhase;
        Reports = reports;
        UnexpectedPacketId = unexpectedPacketId;
    }

    public LoginPostAuthenticationReportReadStatus Status { get; }
    public LoginPostAuthenticationReportPhase? FailurePhase { get; }
    public LoginPostAuthenticationReports? Reports { get; }
    public ushort? UnexpectedPacketId { get; }

    public static LoginPostAuthenticationReportReadResult Success(LoginPostAuthenticationReports reports)
    {
        ArgumentNullException.ThrowIfNull(reports);

        return new LoginPostAuthenticationReportReadResult(LoginPostAuthenticationReportReadStatus.Success, failurePhase: null, reports, unexpectedPacketId: null);
    }

    public static LoginPostAuthenticationReportReadResult EndOfStream(LoginPostAuthenticationReportPhase phase)
    {
        return Failure(LoginPostAuthenticationReportReadStatus.EndOfStream, phase);
    }

    public static LoginPostAuthenticationReportReadResult UnexpectedPacket(LoginPostAuthenticationReportPhase phase, ushort packetId)
    {
        return new LoginPostAuthenticationReportReadResult(LoginPostAuthenticationReportReadStatus.UnexpectedPacket, phase, reports: null, packetId);
    }

    public static LoginPostAuthenticationReportReadResult InvalidReport(LoginPostAuthenticationReportPhase phase)
    {
        return Failure(LoginPostAuthenticationReportReadStatus.InvalidReport, phase);
    }

    public static LoginPostAuthenticationReportReadResult SessionMismatch(LoginPostAuthenticationReportPhase phase)
    {
        return Failure(LoginPostAuthenticationReportReadStatus.SessionMismatch, phase);
    }

    public static LoginPostAuthenticationReportReadResult InvalidMacAddress()
    {
        return Failure(LoginPostAuthenticationReportReadStatus.InvalidMacAddress, LoginPostAuthenticationReportPhase.MacAddressReport);
    }

    public static LoginPostAuthenticationReportReadResult UnexpectedResourceName()
    {
        return Failure(LoginPostAuthenticationReportReadStatus.UnexpectedResourceName, LoginPostAuthenticationReportPhase.ResourceVersionReport);
    }

    private static LoginPostAuthenticationReportReadResult Failure(LoginPostAuthenticationReportReadStatus status, LoginPostAuthenticationReportPhase phase)
    {
        return new LoginPostAuthenticationReportReadResult(status, phase, reports: null, unexpectedPacketId: null);
    }
}
