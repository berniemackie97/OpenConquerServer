namespace OpenConquer.GameServer.Login;

/// <summary>
/// A login failed to release its connection or owned state. The realm must stop before admitting more sessions.
/// </summary>
internal sealed class GameLoginCleanupException(string message, IEnumerable<Exception> failures) : AggregateException(message, failures);
