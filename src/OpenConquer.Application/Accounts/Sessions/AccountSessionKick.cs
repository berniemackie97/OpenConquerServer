namespace OpenConquer.Application.Accounts.Sessions;

/// <summary>
/// A request for this realm to disconnect an account. The character and session are <see langword="null"/>
/// when the account no longer has a session recorded in this realm.
/// </summary>
public readonly record struct AccountSessionKick(uint AccountId, uint? CharacterId, uint? SessionUid);
