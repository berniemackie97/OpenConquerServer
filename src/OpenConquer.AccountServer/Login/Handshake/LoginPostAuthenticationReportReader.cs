using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.AccountServer.Login.Handshake;

internal sealed class LoginPostAuthenticationReportReader
{
    private const string ExpectedResourceName = "res.dat";

    private readonly LoginConnectionSession _session;

    public LoginPostAuthenticationReportReader(LoginConnectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    public ValueTask<LoginPostAuthenticationReportReadResult> ReadAsync(uint expectedSessionUid, CancellationToken cancellationToken = default)
    {
        return ReadCoreAsync(expectedSessionUid, phaseTimeout: null, cancellationToken);
    }

    public ValueTask<LoginPostAuthenticationReportReadResult> ReadAsync(uint expectedSessionUid, TimeSpan phaseTimeout, CancellationToken cancellationToken = default)
    {
        if (phaseTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(phaseTimeout), "The report phase timeout must be greater than zero.");
        }

        return ReadCoreAsync(expectedSessionUid, phaseTimeout, cancellationToken);
    }

    private async ValueTask<LoginPostAuthenticationReportReadResult> ReadCoreAsync(uint expectedSessionUid, TimeSpan? phaseTimeout, CancellationToken cancellationToken)
    {
        if (expectedSessionUid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedSessionUid), "The expected post-authentication session UID must be nonzero.");
        }

        FrameReadResult macRead = await ReadFrameAsync(phaseTimeout, cancellationToken).ConfigureAwait(false);

        if (macRead.TimedOut)
        {
            return LoginPostAuthenticationReportReadResult.TimedOut(LoginPostAuthenticationReportPhase.MacAddressReport);
        }

        if (macRead.Frame is null)
        {
            return LoginPostAuthenticationReportReadResult.EndOfStream(LoginPostAuthenticationReportPhase.MacAddressReport);
        }

        string macAddress;

        using (LoginInboundFrame frame = macRead.Frame)
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

        FrameReadResult resourceRead = await ReadFrameAsync(phaseTimeout, cancellationToken).ConfigureAwait(false);

        if (resourceRead.TimedOut)
        {
            return LoginPostAuthenticationReportReadResult.TimedOut(LoginPostAuthenticationReportPhase.ResourceVersionReport);
        }

        if (resourceRead.Frame is null)
        {
            return LoginPostAuthenticationReportReadResult.EndOfStream(LoginPostAuthenticationReportPhase.ResourceVersionReport);
        }

        int resourceVersion;

        using (LoginInboundFrame frame = resourceRead.Frame)
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

    private async ValueTask<FrameReadResult> ReadFrameAsync(TimeSpan? phaseTimeout, CancellationToken cancellationToken)
    {
        if (phaseTimeout is null)
        {
            return new FrameReadResult(await _session.ReadAsync(cancellationToken).ConfigureAwait(false), TimedOut: false);
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(phaseTimeout.Value);

        try
        {
            return new FrameReadResult(await _session.ReadAsync(timeout.Token).ConfigureAwait(false), TimedOut: false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return new FrameReadResult(Frame: null, TimedOut: true);
        }
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

    private readonly record struct FrameReadResult(LoginInboundFrame? Frame, bool TimedOut);
}
