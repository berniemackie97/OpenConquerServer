namespace OpenConquer.Protocol.Login.Packets;

public enum LoginAccountAuthenticationFailureCode : uint
{
    InvalidCredentials = 1,
    Banned = 12,
    InvalidAccount = 57,
}
