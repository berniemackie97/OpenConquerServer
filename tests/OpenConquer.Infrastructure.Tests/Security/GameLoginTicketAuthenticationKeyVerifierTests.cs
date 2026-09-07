using OpenConquer.Infrastructure.Security;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Tests.Security;

public sealed class GameLoginTicketAuthenticationKeyVerifierTests
{
    private const uint SessionUid = 0x1020_3040u;
    private const uint AuthenticationKey = 0x5060_7080u;

    private static readonly byte[] s_verificationKey =
    [
        0x00,
        0x01,
        0x02,
        0x03,
        0x04,
        0x05,
        0x06,
        0x07,
        0x08,
        0x09,
        0x0A,
        0x0B,
        0x0C,
        0x0D,
        0x0E,
        0x0F,
        0x10,
        0x11,
        0x12,
        0x13,
        0x14,
        0x15,
        0x16,
        0x17,
        0x18,
        0x19,
        0x1A,
        0x1B,
        0x1C,
        0x1D,
        0x1E,
        0x1F,
    ];

    private static readonly byte[] s_expectedVerifier =
    [
        0x02,
        0x64,
        0x7D,
        0x57,
        0xEC,
        0xDD,
        0x4C,
        0x3D,
        0xFB,
        0x1D,
        0x3F,
        0x94,
        0xB5,
        0xAD,
        0xEE,
        0x25,
        0xFB,
        0x17,
        0x92,
        0x10,
        0x1B,
        0x10,
        0x0C,
        0xA2,
        0x58,
        0x77,
        0x23,
        0x32,
        0x1A,
        0x04,
        0x2F,
        0x18,
    ];

    [Fact]
    public void Create_KnownInputProducesExpectedVerifier()
    {
        byte[] verifier = GameLoginTicketAuthenticationKeyVerifier.Create(
            s_verificationKey,
            SessionUid,
            AuthenticationKey
        );

        Assert.Equal(s_expectedVerifier, verifier);
    }

    [Fact]
    public void Verify_MatchingCredentialsReturnsTrue()
    {
        bool verified = GameLoginTicketAuthenticationKeyVerifier.Verify(
            s_expectedVerifier,
            s_verificationKey,
            SessionUid,
            AuthenticationKey
        );

        Assert.True(verified);
    }

    [Fact]
    public void Verify_DifferentVerificationKeyReturnsFalse()
    {
        byte[] differentVerificationKey = (byte[])s_verificationKey.Clone();

        differentVerificationKey[0] ^= 0x01;

        bool verified = GameLoginTicketAuthenticationKeyVerifier.Verify(
            s_expectedVerifier,
            differentVerificationKey,
            SessionUid,
            AuthenticationKey
        );

        Assert.False(verified);
    }

    [Fact]
    public void Verify_DifferentSessionUidReturnsFalse()
    {
        bool verified = GameLoginTicketAuthenticationKeyVerifier.Verify(
            s_expectedVerifier,
            s_verificationKey,
            SessionUid + 1,
            AuthenticationKey
        );

        Assert.False(verified);
    }

    [Fact]
    public void Verify_DifferentAuthenticationKeyReturnsFalse()
    {
        bool verified = GameLoginTicketAuthenticationKeyVerifier.Verify(
            s_expectedVerifier,
            s_verificationKey,
            SessionUid,
            AuthenticationKey + 1
        );

        Assert.False(verified);
    }

    [Fact]
    public void Create_ShortVerificationKeyIsRejected()
    {
        byte[] verificationKey = new byte[
            GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize - 1
        ];

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            GameLoginTicketAuthenticationKeyVerifier.Create(
                verificationKey,
                SessionUid,
                AuthenticationKey
            )
        );

        Assert.Equal("verificationKey", exception.ParamName);
    }

    [Fact]
    public void Create_LongVerificationKeyIsRejected()
    {
        byte[] verificationKey = new byte[
            GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize + 1
        ];

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            GameLoginTicketAuthenticationKeyVerifier.Create(
                verificationKey,
                SessionUid,
                AuthenticationKey
            )
        );

        Assert.Equal("verificationKey", exception.ParamName);
    }

    [Fact]
    public void Verify_InvalidVerifierLengthIsRejected()
    {
        byte[] verifier = new byte[GameLoginTicketAuthenticationKeyVerifier.VerifierSize - 1];

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            GameLoginTicketAuthenticationKeyVerifier.Verify(
                verifier,
                s_verificationKey,
                SessionUid,
                AuthenticationKey
            )
        );

        Assert.Equal("expectedVerifier", exception.ParamName);
    }

    [Fact]
    public void Create_ZeroSessionUidIsRejected()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            GameLoginTicketAuthenticationKeyVerifier.Create(
                s_verificationKey,
                sessionUid: 0,
                AuthenticationKey
            )
        );

        Assert.Equal("sessionUid", exception.ParamName);
    }

    [Fact]
    public void Create_ZeroAuthenticationKeyIsRejected()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            GameLoginTicketAuthenticationKeyVerifier.Create(
                s_verificationKey,
                SessionUid,
                authenticationKey: 0
            )
        );

        Assert.Equal("authenticationKey", exception.ParamName);
    }
}
