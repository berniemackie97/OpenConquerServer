using System.Diagnostics.CodeAnalysis;
using System.Net;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Infrastructure.Security;

namespace OpenConquer.Infrastructure.Security.Accounts.GameLogin;

internal sealed class GameLoginTicketRedemptionAttemptLimiter(GameLoginTicketRedemptionAttemptLimiterOptions options, TimeProvider timeProvider)
    : IGameLoginTicketRedemptionAttemptLimiter
{
    private const int OpportunisticCleanupLimit = 16;

    private readonly GameLoginTicketRedemptionAttemptLimiterOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly Lock _gate = new();
    private readonly Dictionary<RemoteAddressSourceKey, SourceState> _sources = [];
    private readonly Dictionary<uint, SessionState> _sessions = [];
    private readonly LinkedList<RemoteAddressSourceKey> _sourceRetention = [];
    private readonly LinkedList<uint> _sessionRetention = [];
    private int _inFlightAttempts;

    public bool TryBeginRedemption(IPAddress remoteAddress, uint sessionUid, [NotNullWhen(true)] out IGameLoginTicketRedemptionAttemptLease? attempt)
    {
        ArgumentNullException.ThrowIfNull(remoteAddress);

        if (sessionUid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionUid), "A game-login ticket redemption requires a nonzero session UID.");
        }

        RemoteAddressSourceKey sourceKey = RemoteAddressSourceKey.Create(remoteAddress);
        long timestamp = _timeProvider.GetTimestamp();

        lock (_gate)
        {
            TrimExpiredEntries(timestamp, OpportunisticCleanupLimit);

            if (_inFlightAttempts >= _options.MaximumConcurrentAttempts)
            {
                attempt = null;
                return false;
            }

            _sources.TryGetValue(sourceKey, out SourceState? sourceState);

            if (sourceState is not null)
            {
                RefillSourceState(sourceState, timestamp);

                if (sourceState.AvailableTokens < 1d || sourceState.InFlightAttempts >= _options.MaximumConcurrentAttemptsPerSource)
                {
                    attempt = null;
                    return false;
                }
            }

            _sessions.TryGetValue(sessionUid, out SessionState? sessionState);

            if (sessionState is not null)
            {
                RefreshSessionState(sessionState, timestamp);

                if (sessionState.AuthorizationAccepted || sessionState.IsLockedOut || sessionState.InFlightAttempts >= _options.MaximumConcurrentAttemptsPerSession
                    || sessionState.FailedAttempts + sessionState.InFlightAttempts >= _options.FailedAttemptLimitPerSession)
                {
                    attempt = null;
                    return false;
                }
            }

            int requiredTrackedEntries = (sourceState is null ? 1 : 0) + (sessionState is null ? 1 : 0);

            if (!EnsureCapacity(requiredTrackedEntries, timestamp, sourceState, sessionState))
            {
                attempt = null;
                return false;
            }

            if (sourceState is null)
            {
                sourceState = new SourceState(sourceKey, _options.RequestLimitPerSource, timestamp);
                _sources.Add(sourceKey, sourceState);
                _sourceRetention.AddLast(sourceState.RetentionNode);
            }

            if (sessionState is null)
            {
                sessionState = new SessionState(sessionUid, timestamp);
                _sessions.Add(sessionUid, sessionState);
                _sessionRetention.AddLast(sessionState.RetentionNode);
            }

            sourceState.AvailableTokens -= 1d;
            sourceState.InFlightAttempts++;
            sessionState.InFlightAttempts++;
            _inFlightAttempts++;

            TouchSourceState(sourceState, timestamp);
            TouchSessionState(sessionState, timestamp);

            attempt = new AttemptLease(this, sourceState, sessionState);
            return true;
        }
    }

    private void CompleteAttempt(SourceState sourceState, SessionState sessionState, bool authorizationAccepted)
    {
        long timestamp = _timeProvider.GetTimestamp();

        lock (_gate)
        {
            ReleaseConcurrency(sourceState, sessionState);
            TouchSourceState(sourceState, timestamp);

            if (authorizationAccepted)
            {
                sessionState.AuthorizationAccepted = true;
                sessionState.FailedAttempts = 0;
                sessionState.IsLockedOut = false;
            }
            else if (!sessionState.AuthorizationAccepted)
            {
                RecordFailedAttempt(sessionState, timestamp);
            }

            if (sessionState.AuthorizationAccepted && sessionState.InFlightAttempts == 0)
            {
                RemoveSessionState(sessionState);
            }
            else
            {
                TouchSessionState(sessionState, timestamp);
            }
        }
    }

    private void AbandonAttempt(SourceState sourceState, SessionState sessionState)
    {
        long timestamp = _timeProvider.GetTimestamp();

        lock (_gate)
        {
            ReleaseConcurrency(sourceState, sessionState);
            TouchSourceState(sourceState, timestamp);

            if (sessionState.AuthorizationAccepted)
            {
                if (sessionState.InFlightAttempts == 0)
                {
                    RemoveSessionState(sessionState);
                }

                return;
            }

            RefreshSessionState(sessionState, timestamp);

            if (sessionState.InFlightAttempts == 0 && sessionState.FailedAttempts == 0 && !sessionState.IsLockedOut)
            {
                RemoveSessionState(sessionState);
                return;
            }

            TouchSessionState(sessionState, timestamp);
        }
    }

    private void ReleaseConcurrency(SourceState sourceState, SessionState sessionState)
    {
        sourceState.InFlightAttempts--;
        sessionState.InFlightAttempts--;
        _inFlightAttempts--;
    }

    private void RecordFailedAttempt(SessionState sessionState, long timestamp)
    {
        RefreshSessionState(sessionState, timestamp);

        if (sessionState.IsLockedOut)
        {
            return;
        }

        if (sessionState.FailedAttempts == 0)
        {
            sessionState.FailureWindowStartedTimestamp = timestamp;
        }

        sessionState.FailedAttempts++;

        if (sessionState.FailedAttempts >= _options.FailedAttemptLimitPerSession)
        {
            sessionState.IsLockedOut = true;
            sessionState.LockoutStartedTimestamp = timestamp;
        }
    }

    private void RefreshSessionState(SessionState sessionState, long timestamp)
    {
        if (sessionState.IsLockedOut)
        {
            if (_timeProvider.GetElapsedTime(sessionState.LockoutStartedTimestamp, timestamp) < _options.FailureLockout)
            {
                return;
            }

            sessionState.IsLockedOut = false;
            sessionState.FailedAttempts = 0;
        }

        if (sessionState.FailedAttempts > 0 && _timeProvider.GetElapsedTime(sessionState.FailureWindowStartedTimestamp, timestamp) >= _options.FailureWindow)
        {
            sessionState.FailedAttempts = 0;
        }
    }

    private void RefillSourceState(SourceState sourceState, long timestamp)
    {
        TimeSpan elapsed = _timeProvider.GetElapsedTime(sourceState.LastRefillTimestamp, timestamp);

        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }

        double replenishedTokens = elapsed.TotalSeconds * _options.RequestLimitPerSource / _options.RequestWindow.TotalSeconds;

        sourceState.AvailableTokens = Math.Min(_options.RequestLimitPerSource, sourceState.AvailableTokens + replenishedTokens);
        sourceState.LastRefillTimestamp = timestamp;
    }

    private bool EnsureCapacity(int requiredTrackedEntries, long timestamp, SourceState? protectedSourceState, SessionState? protectedSessionState)
    {
        while (_sources.Count + _sessions.Count + requiredTrackedEntries > _options.MaximumTrackedEntries)
        {
            if (!TryRemoveExpiredEntry(timestamp, protectedSourceState, protectedSessionState))
            {
                return false;
            }
        }

        return true;
    }

    private void TrimExpiredEntries(long timestamp, int maximumRemovals)
    {
        for (int i = 0; i < maximumRemovals; i++)
        {
            if (!TryRemoveExpiredEntry(timestamp, null, null))
            {
                return;
            }
        }
    }

    private bool TryRemoveExpiredEntry(long timestamp, SourceState? protectedSourceState, SessionState? protectedSessionState)
    {
        return TryRemoveExpiredSourceState(timestamp, protectedSourceState) || TryRemoveExpiredSessionState(timestamp, protectedSessionState);
    }

    private bool TryRemoveExpiredSourceState(long timestamp, SourceState? protectedState)
    {
        LinkedListNode<RemoteAddressSourceKey>? node = _sourceRetention.First;

        while (node is not null)
        {
            LinkedListNode<RemoteAddressSourceKey>? next = node.Next;
            SourceState sourceState = _sources[node.Value];

            if (!RetentionExpired(sourceState.LastActivityTimestamp, timestamp))
            {
                return false;
            }

            if (ReferenceEquals(sourceState, protectedState))
            {
                node = next;
                continue;
            }

            if (sourceState.InFlightAttempts != 0)
            {
                TouchSourceState(sourceState, timestamp);
                node = next;
                continue;
            }

            RemoveSourceState(sourceState);
            return true;
        }

        return false;
    }

    private bool TryRemoveExpiredSessionState(long timestamp, SessionState? protectedState)
    {
        LinkedListNode<uint>? node = _sessionRetention.First;

        while (node is not null)
        {
            LinkedListNode<uint>? next = node.Next;
            SessionState sessionState = _sessions[node.Value];

            if (!RetentionExpired(sessionState.LastActivityTimestamp, timestamp))
            {
                return false;
            }

            if (ReferenceEquals(sessionState, protectedState))
            {
                node = next;
                continue;
            }

            if (sessionState.InFlightAttempts != 0)
            {
                TouchSessionState(sessionState, timestamp);
                node = next;
                continue;
            }

            RemoveSessionState(sessionState);
            return true;
        }

        return false;
    }

    private bool RetentionExpired(long lastActivityTimestamp, long timestamp)
    {
        return _timeProvider.GetElapsedTime(lastActivityTimestamp, timestamp) >= _options.EntryRetention;
    }

    private void TouchSourceState(SourceState sourceState, long timestamp)
    {
        sourceState.LastActivityTimestamp = timestamp;

        if (sourceState.RetentionNode.List is not null)
        {
            _sourceRetention.Remove(sourceState.RetentionNode);
        }

        _sourceRetention.AddLast(sourceState.RetentionNode);
    }

    private void TouchSessionState(SessionState sessionState, long timestamp)
    {
        sessionState.LastActivityTimestamp = timestamp;

        if (sessionState.RetentionNode.List is not null)
        {
            _sessionRetention.Remove(sessionState.RetentionNode);
        }

        _sessionRetention.AddLast(sessionState.RetentionNode);
    }

    private void RemoveSourceState(SourceState sourceState)
    {
        _sources.Remove(sourceState.Key);

        if (sourceState.RetentionNode.List is not null)
        {
            _sourceRetention.Remove(sourceState.RetentionNode);
        }
    }

    private void RemoveSessionState(SessionState sessionState)
    {
        _sessions.Remove(sessionState.SessionUid);

        if (sessionState.RetentionNode.List is not null)
        {
            _sessionRetention.Remove(sessionState.RetentionNode);
        }
    }

    private sealed class SourceState(RemoteAddressSourceKey key, int availableTokens, long timestamp)
    {
        public RemoteAddressSourceKey Key { get; } = key;
        public double AvailableTokens { get; set; } = availableTokens;
        public int InFlightAttempts { get; set; }
        public long LastRefillTimestamp { get; set; } = timestamp;
        public long LastActivityTimestamp { get; set; } = timestamp;
        public LinkedListNode<RemoteAddressSourceKey> RetentionNode { get; } = new(key);
    }

    private sealed class SessionState(uint sessionUid, long timestamp)
    {
        public uint SessionUid { get; } = sessionUid;
        public int FailedAttempts { get; set; }
        public int InFlightAttempts { get; set; }
        public bool IsLockedOut { get; set; }
        public bool AuthorizationAccepted { get; set; }
        public long FailureWindowStartedTimestamp { get; set; } = timestamp;
        public long LockoutStartedTimestamp { get; set; } = timestamp;
        public long LastActivityTimestamp { get; set; } = timestamp;
        public LinkedListNode<uint> RetentionNode { get; } = new(sessionUid);
    }

    private sealed class AttemptLease(GameLoginTicketRedemptionAttemptLimiter owner, SourceState sourceState, SessionState sessionState)
        : IGameLoginTicketRedemptionAttemptLease
    {
        private const int Active = 0;
        private const int Completed = 1;
        private const int Disposed = 2;

        private int _state;

        public void Complete(bool authorizationAccepted)
        {
            int previousState = Interlocked.CompareExchange(ref _state, Completed, Active);

            if (previousState == Disposed)
            {
                throw new ObjectDisposedException(nameof(AttemptLease));
            }

            if (previousState != Active)
            {
                throw new InvalidOperationException("The game-login ticket redemption attempt has already been completed.");
            }

            owner.CompleteAttempt(sourceState, sessionState, authorizationAccepted);
        }

        public void Dispose()
        {
            int previousState = Interlocked.Exchange(ref _state, Disposed);

            if (previousState == Active)
            {
                owner.AbandonAttempt(sourceState, sessionState);
            }
        }
    }
}
