namespace OpenConquer.Application.Accounts.Registration;

public readonly record struct AccountRegistrationResult(AccountRegistrationStatus Status, uint AccountId)
{
    public static AccountRegistrationResult Created(uint accountId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(accountId);

        return new AccountRegistrationResult(AccountRegistrationStatus.Created, accountId);
    }

    public static AccountRegistrationResult Refused(AccountRegistrationStatus status)
    {
        if (status is AccountRegistrationStatus.Created || !Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "A refused registration needs a refusal status.");
        }

        return new AccountRegistrationResult(status, 0);
    }
}
