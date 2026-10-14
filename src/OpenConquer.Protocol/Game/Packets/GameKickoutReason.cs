namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Why the server is disconnecting the client (packet 1115). The client records the reason and hands it to
/// its login screen once the connection has closed.
/// </summary>
public enum GameKickoutReason : uint
{
    /// <summary>The account logged in again elsewhere. The client keeps the notice text.</summary>
    OtherLogin = 0,

    /// <summary>The account's password changed.</summary>
    PasswordChanged = 1,
}
