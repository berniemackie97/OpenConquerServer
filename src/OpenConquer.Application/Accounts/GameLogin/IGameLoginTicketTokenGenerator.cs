namespace OpenConquer.Application.Accounts.GameLogin;

public interface IGameLoginTicketTokenGenerator
{
    uint GenerateSessionUid();
    uint GenerateAuthenticationKey();
}
