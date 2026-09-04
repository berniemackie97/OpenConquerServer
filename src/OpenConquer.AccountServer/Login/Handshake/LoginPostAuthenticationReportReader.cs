using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.AccountServer.Login.Handshake;

/// <summary>
/// Consumes the two native client-to-AccountServer reports sent after a
/// successful packet-1055 authentication response.
/// </summary>
/// <remarks>
/// The verified standard 5517 client sends packet 1100 first and the
/// AccountServer form of packet 1052 second, then disconnects the account
/// connection.
///
/// This reader validates protocol ordering and correlation only. The reports are
/// client-controlled telemetry and do not authorize the later GameServer
/// connection.
/// </remarks>
internal sealed class LoginPostAuthenticationReportReader
{
    private const string ExpectedResourceName = "res.dat";

    private readonly LoginConnectionSession _session;

    public LoginPostAuthenticationReportReader(LoginConnectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
    }

    /// <summary>
    /// Consumes the native packet-1100 and AccountServer packet-1052 sequence
    /// associated with <paramref name="expectedSessionUid"/>.
    /// </summary>
    /// <remarks>
    /// Transport failures and caller-requested cancellation propagate
    /// unchanged. No authorization grant is revoked or otherwise mutated by
    /// this operation.
    /// </remarks>
    public async ValueTask<LoginPostAuthenticationReportReadResult> ReadAsync(uint expectedSessionUid, CancellationToken cancellationToken = default)
    {
        if (expectedSessionUid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedSessionUid), "The expected post-authentication session UID must be nonzero.");
        }

        LoginInboundFrame? macInboundFrame = await _session.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (macInboundFrame is null)
        {
            return LoginPostAuthenticationReportReadResult.EndOfStream(LoginPostAuthenticationReportPhase.MacAddressReport);
        }

        string macAddress;

        using (LoginInboundFrame frame = macInboundFrame)
        {
            if (frame.PacketId != LoginAccountMacAddressReportPacket.PacketIdentifier)
            {
                return LoginPostAuthenticationReportReadResult.UnexpectedPacket(LoginPostAuthenticationReportPhase.MacAddressReport, frame.PacketId);
            }

            if (!LoginAccountMacAddressReportPacket.TryDecode(frame.Payload.Span, out LoginAccountMacAddressReport? report))
            {
                return LoginPostAuthenticationReportReadResult.InvalidReport(LoginPostAuthenticationReportPhase.MacAddressReport);
            }

            LoginAccountMacAddressReport decodedReport = report ?? throw new InvalidOperationException("Successful MAC-address report decoding did not return a report.");

            if (decodedReport.SessionUid != expectedSessionUid)
            {
                return LoginPostAuthenticationReportReadResult.SessionMismatch(LoginPostAuthenticationReportPhase.MacAddressReport);
            }

            if (!IsValidMacAddress(decodedReport.MacAddress))
            {
                return LoginPostAuthenticationReportReadResult.InvalidMacAddress();
            }

            macAddress = decodedReport.MacAddress;
        }

        LoginInboundFrame? resourceInboundFrame = await _session.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (resourceInboundFrame is null)
        {
            return LoginPostAuthenticationReportReadResult.EndOfStream(LoginPostAuthenticationReportPhase.ResourceVersionReport);
        }

        int resourceVersion;

        using (LoginInboundFrame frame = resourceInboundFrame)
        {
            if (frame.PacketId != LoginAccountResourceVersionReportPacket.PacketIdentifier)
            {
                return LoginPostAuthenticationReportReadResult.UnexpectedPacket(LoginPostAuthenticationReportPhase.ResourceVersionReport, frame.PacketId);
            }

            if (!LoginAccountResourceVersionReportPacket.TryDecode(frame.Payload.Span, out LoginAccountResourceVersionReport? report))
            {
                return LoginPostAuthenticationReportReadResult.InvalidReport(LoginPostAuthenticationReportPhase.ResourceVersionReport);
            }

            LoginAccountResourceVersionReport decodedReport = report ?? throw new InvalidOperationException("Successful resource-version report decoding did not return a report.");

            if (decodedReport.SessionUid != expectedSessionUid)
            {
                return LoginPostAuthenticationReportReadResult.SessionMismatch(LoginPostAuthenticationReportPhase.ResourceVersionReport);
            }

            if (!string.Equals(decodedReport.ResourceName, ExpectedResourceName, StringComparison.Ordinal))
            {
                return LoginPostAuthenticationReportReadResult.UnexpectedResourceName();
            }

            resourceVersion = decodedReport.ResourceVersion;
        }

        return LoginPostAuthenticationReportReadResult.Success(new LoginPostAuthenticationReports(macAddress, resourceVersion));
    }

    private static bool IsValidMacAddress(string macAddress)
    {
        if (macAddress.Length == 0)
        {
            return true;
        }

        if (macAddress.Length != 12)
        {
            return false;
        }

        foreach (char value in macAddress)
        {
            if (value is not (>= '0' and <= '9') and not (>= 'A' and <= 'F'))
            {
                return false;
            }
        }

        return true;
    }
}
