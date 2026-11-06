namespace OpenConquer.GameServer.Connections;

/// <summary>
/// Tracks the legal bootstrap challenge, client response and secured-framing progression.
/// Every access is serialized by GameConnectionSession's existing protocol gate.
/// This component owns no pipes, cipher or connection resources.
/// </summary>
internal sealed class GameHandshakeProgress
{
    private const int ProtocolStateBootstrap = 0;
    private const int ProtocolStateSecured = 1;
    private const int ProtocolStateTerminal = 2;

    private const int ChallengeStateNotStarted = 0;
    private const int ChallengeStateActive = 1;
    private const int ChallengeStateCompleted = 2;

    private const int KeyExchangeStateNotStarted = 0;
    private const int KeyExchangeStateActive = 1;
    private const int KeyExchangeStateCompleted = 2;

    private int _protocolState = ProtocolStateBootstrap;
    private int _challengeState = ChallengeStateNotStarted;
    private int _keyExchangeState = KeyExchangeStateNotStarted;

    public bool IsSecured => _protocolState == ProtocolStateSecured;
    public bool CanResetKeyExchangeRead => _protocolState == ProtocolStateBootstrap && _keyExchangeState == KeyExchangeStateActive;

    public void RequireCompletion()
    {
        if (_protocolState != ProtocolStateBootstrap)
        {
            throw new InvalidOperationException("The GameServer connection is not awaiting handshake completion.");
        }

        if (_challengeState != ChallengeStateCompleted || _keyExchangeState != KeyExchangeStateCompleted)
        {
            throw new InvalidOperationException("The GameServer handshake cannot complete before its challenge and client key exchange have completed.");
        }
    }

    public void MarkSecured() => _protocolState = ProtocolStateSecured;
    public void MarkTerminal() => _protocolState = ProtocolStateTerminal;
    public void ResetKeyExchangeRead() => _keyExchangeState = KeyExchangeStateNotStarted;

    public void EnterChallengeWrite()
    {
        if (_protocolState != ProtocolStateBootstrap)
        {
            throw new InvalidOperationException("The GameServer connection is not in bootstrap handshake state.");
        }

        if (_challengeState != ChallengeStateNotStarted)
        {
            throw new InvalidOperationException("The GameServer handshake challenge may only be sent once.");
        }

        _challengeState = ChallengeStateActive;
    }

    public void CompleteChallengeWrite()
    {
        if (_protocolState != ProtocolStateBootstrap || _challengeState != ChallengeStateActive)
        {
            throw new InvalidOperationException("The GameServer handshake challenge state changed unexpectedly while it was being sent.");
        }

        _challengeState = ChallengeStateCompleted;
    }

    public void EnterKeyExchangeRead()
    {
        if (_protocolState != ProtocolStateBootstrap)
        {
            throw new InvalidOperationException("The GameServer connection is not in bootstrap handshake state.");
        }

        if (_challengeState != ChallengeStateCompleted)
        {
            throw new InvalidOperationException("The client key-exchange response cannot be read before the GameServer handshake challenge has been sent.");
        }

        if (_keyExchangeState != KeyExchangeStateNotStarted)
        {
            throw new InvalidOperationException("The client key-exchange response may only be read once at a time.");
        }

        _keyExchangeState = KeyExchangeStateActive;
    }

    public void CompleteKeyExchangeRead()
    {
        if (_protocolState != ProtocolStateBootstrap || _keyExchangeState != KeyExchangeStateActive)
        {
            throw new InvalidOperationException("The GameServer client key-exchange state changed unexpectedly while it was being read.");
        }

        _keyExchangeState = KeyExchangeStateCompleted;
    }
}
