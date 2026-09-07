namespace OpenConquer.Application.Accounts.GameLogin;

public sealed class GameLoginTicketAllocationException()
    : Exception("A unique game-login session UID could not be allocated within the permitted number of attempts.");
