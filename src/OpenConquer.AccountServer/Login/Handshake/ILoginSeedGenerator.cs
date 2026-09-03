namespace OpenConquer.AccountServer.Login.Handshake;

/// <summary>
/// Generates the per-connection seed sent during the account-login handshake.
/// </summary>
internal interface ILoginSeedGenerator
{
    uint GenerateSeed();
}
