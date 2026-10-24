namespace OpenConquer.Application.Accounts.Registration;

public enum AccountRegistrationStatus
{
    Created = 1,
    InvalidRequest = 2,
    UsernameTaken = 3,
    Throttled = 4,
}
