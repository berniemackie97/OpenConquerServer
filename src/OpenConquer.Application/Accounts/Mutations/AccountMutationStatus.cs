namespace OpenConquer.Application.Accounts.Mutations;

public enum AccountMutationStatus
{
    Applied = 1,
    AccountNotFound = 2,
    StateConflict = 3,
    InvalidState = 4,
    NoChange = 5,
}
