namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Failure codes carried in the second UInt32 field of a failed
/// account authentication response (packet 1055).
/// </summary>
public enum LoginAccountAuthenticationFailureCode : uint
{
    InvalidCredentials = 1,
    Banned = 12,
    InvalidAccount = 57,
}
