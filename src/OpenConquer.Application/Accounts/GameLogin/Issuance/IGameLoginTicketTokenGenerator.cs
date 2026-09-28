namespace OpenConquer.Application.Accounts.GameLogin.Issuance;

public interface IGameLoginTicketTokenGenerator
{
    uint GenerateSessionUid();
    uint GenerateAuthenticationKey();
}
