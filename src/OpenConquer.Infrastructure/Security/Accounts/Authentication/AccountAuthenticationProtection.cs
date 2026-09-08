using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using OpenConquer.Application.Accounts.Authentication;

namespace OpenConquer.Infrastructure.Security.Accounts.Authentication;

internal sealed class AccountAuthenticationProtection(AccountAuthenticationProtectionOptions options, TimeProvider timeProvider)
    : IAccountAuthenticationRequestLimiter, IAccountAuthenticationAttemptLimiter
{
    private const int OpportunisticCleanupLimit = 16;

    private readonly AccountAuthenticationProtectionOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly Lock _gate = new();
    private readonly Dictionary<SourceKey, SourceState> _sources = [];
    private readonly Dictionary<uint, AccountState> _accounts = [];
    private readonly Dictionary<AccountSourceKey, AccountSourceState> _accountSources = [];
    private readonly LinkedList<SourceKey> _sourceRetention = [];
    private readonly LinkedList<AccountSourceKey> _accountSourceRetention = [];
    private int _inFlightRequests;

    public bool TryBeginAuthentication(IPAddress remoteAddress, [NotNullWhen(true)] out IAccountAuthenticationRequestLease? request)
    {
        ArgumentNullException.ThrowIfNull(remoteAddress);

        SourceKey sourceKey = CreateSourceKey(remoteAddress);
        long timestamp = _timeProvider.GetTimestamp();

        lock (_gate)
        {
            TrimExpiredEntries(timestamp, OpportunisticCleanupLimit);

            if (_inFlightRequests >= _options.MaximumConcurrentRequests)
            {
                request = null;
                return false;
            }

            _sources.TryGetValue(sourceKey, out SourceState? sourceState);

            if (sourceState is not null)
            {
                RefillSourceState(sourceState, timestamp);

                if (sourceState.AvailableTokens < 1d ||
                    sourceState.InFlightRequests >= _options.MaximumConcurrentRequestsPerSource)
                {
                    request = null;
                    return false;
                }
            }

            int requiredTrackedEntries = sourceState is null ? 1 : 0;

            if (!EnsureCapacity(requiredTrackedEntries, timestamp, sourceState, null))
            {
                request = null;
                return false;
            }

            if (sourceState is null)
            {
                sourceState = new SourceState(sourceKey, _options.RequestLimitPerSource, timestamp);
                _sources.Add(sourceKey, sourceState);
                _sourceRetention.AddLast(sourceState.RetentionNode);
            }

            sourceState.AvailableTokens -= 1d;
            sourceState.InFlightRequests++;
            _inFlightRequests++;

            TouchSourceState(sourceState, timestamp);

            request = new RequestLease(this, sourceState);
            return true;
        }
    }

    public bool TryBeginAuthentication(IPAddress remoteAddress, uint accountId, [NotNullWhen(true)] out IAccountAuthenticationAttemptLease? attempt)
    {
        ArgumentNullException.ThrowIfNull(remoteAddress);

        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "An authentication attempt requires a nonzero account ID.");
        }

        SourceKey sourceKey = CreateSourceKey(remoteAddress);
        AccountSourceKey accountSourceKey = new(accountId, sourceKey);
        long timestamp = _timeProvider.GetTimestamp();

        lock (_gate)
        {
            TrimExpiredEntries(timestamp, OpportunisticCleanupLimit);

            _accounts.TryGetValue(accountId, out AccountState? accountState);

            if (accountState is not null &&
                accountState.InFlightAttempts >= _options.MaximumConcurrentAttemptsPerAccount)
            {
                attempt = null;
                return false;
            }

            _accountSources.TryGetValue(accountSourceKey, out AccountSourceState? accountSourceState);

            if (accountSourceState is not null)
            {
                RefreshAccountSourceState(accountSourceState, timestamp);

                if (accountSourceState.IsLockedOut || accountSourceState.FailedAttempts + accountSourceState.InFlightAttempts >= _options.FailedAttemptLimitPerAccountSource)
                {
                    attempt = null;
                    return false;
                }
            }

            int requiredTrackedEntries = (accountState is null ? 1 : 0) + (accountSourceState is null ? 1 : 0);

            if (!EnsureCapacity(requiredTrackedEntries, timestamp, null, accountSourceState))
            {
                attempt = null;
                return false;
            }

            if (accountState is null)
            {
                accountState = new AccountState(accountId);
                _accounts.Add(accountId, accountState);
            }

            if (accountSourceState is null)
            {
                accountSourceState = new AccountSourceState(accountSourceKey, timestamp);
                _accountSources.Add(accountSourceKey, accountSourceState);
                _accountSourceRetention.AddLast(accountSourceState.RetentionNode);
            }

            accountState.InFlightAttempts++;
            accountSourceState.InFlightAttempts++;

            TouchAccountSourceState(accountSourceState, timestamp);

            attempt = new AttemptLease(this, accountState, accountSourceState);
            return true;
        }
    }

    private void ReleaseRequest(SourceState sourceState)
    {
        long timestamp = _timeProvider.GetTimestamp();

        lock (_gate)
        {
            sourceState.InFlightRequests--;
            _inFlightRequests--;

            TouchSourceState(sourceState, timestamp);
        }
    }

    private void CompleteAttempt(AccountState accountState, AccountSourceState accountSourceState, bool credentialsAccepted)
    {
        long timestamp = _timeProvider.GetTimestamp();

        lock (_gate)
        {
            ReleaseAttemptConcurrency(accountState, accountSourceState);

            if (credentialsAccepted)
            {
                accountSourceState.FailedAttempts = 0;
                accountSourceState.IsLockedOut = false;

                if (accountSourceState.InFlightAttempts == 0)
                {
                    RemoveAccountSourceState(accountSourceState);
                }
                else
                {
                    TouchAccountSourceState(accountSourceState, timestamp);
                }
            }
            else
            {
                RecordFailedAttempt(accountSourceState, timestamp);
                TouchAccountSourceState(accountSourceState, timestamp);
            }

            if (accountState.InFlightAttempts == 0)
            {
                RemoveAccountState(accountState);
            }
        }
    }

    private void AbandonAttempt(AccountState accountState, AccountSourceState accountSourceState)
    {
        long timestamp = _timeProvider.GetTimestamp();

        lock (_gate)
        {
            ReleaseAttemptConcurrency(accountState, accountSourceState);
            RefreshAccountSourceState(accountSourceState, timestamp);

            if (accountSourceState is { InFlightAttempts: 0, FailedAttempts: 0, IsLockedOut: false })
            {
                RemoveAccountSourceState(accountSourceState);
            }
            else
            {
                TouchAccountSourceState(accountSourceState, timestamp);
            }

            if (accountState.InFlightAttempts == 0)
            {
                RemoveAccountState(accountState);
            }
        }
    }

    private static void ReleaseAttemptConcurrency(AccountState accountState, AccountSourceState accountSourceState)
    {
        accountState.InFlightAttempts--;
        accountSourceState.InFlightAttempts--;
    }

    private void RecordFailedAttempt(AccountSourceState accountSourceState, long timestamp)
    {
        RefreshAccountSourceState(accountSourceState, timestamp);

        if (accountSourceState.IsLockedOut)
        {
            return;
        }

        if (accountSourceState.FailedAttempts == 0)
        {
            accountSourceState.FailureWindowStartedTimestamp = timestamp;
        }

        accountSourceState.FailedAttempts++;

        if (accountSourceState.FailedAttempts >= _options.FailedAttemptLimitPerAccountSource)
        {
            accountSourceState.IsLockedOut = true;
            accountSourceState.LockoutStartedTimestamp = timestamp;
        }
    }

    private void RefreshAccountSourceState(AccountSourceState accountSourceState, long timestamp)
    {
        if (accountSourceState.IsLockedOut)
        {
            if (_timeProvider.GetElapsedTime(accountSourceState.LockoutStartedTimestamp, timestamp) < _options.FailureLockout)
            {
                return;
            }

            accountSourceState.IsLockedOut = false;
            accountSourceState.FailedAttempts = 0;
        }

        if (accountSourceState.FailedAttempts > 0 && _timeProvider.GetElapsedTime(accountSourceState.FailureWindowStartedTimestamp, timestamp) >= _options.FailureWindow)
        {
            accountSourceState.FailedAttempts = 0;
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

    private bool EnsureCapacity(int requiredTrackedEntries, long timestamp, SourceState? protectedSourceState, AccountSourceState? protectedAccountSourceState)
    {
        while (_sources.Count + _accounts.Count + _accountSources.Count + requiredTrackedEntries > _options.MaximumTrackedEntries)
        {
            if (!TryRemoveExpiredEntry(timestamp, protectedSourceState, protectedAccountSourceState))
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

    private bool TryRemoveExpiredEntry(long timestamp, SourceState? protectedSourceState, AccountSourceState? protectedAccountSourceState)
    {
        return TryRemoveExpiredSourceState(timestamp, protectedSourceState) || TryRemoveExpiredAccountSourceState(timestamp, protectedAccountSourceState);
    }

    private bool TryRemoveExpiredSourceState(long timestamp, SourceState? protectedState)
    {
        LinkedListNode<SourceKey>? node = _sourceRetention.First;

        while (node is not null)
        {
            LinkedListNode<SourceKey>? next = node.Next;
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

            if (sourceState.InFlightRequests != 0)
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

    private bool TryRemoveExpiredAccountSourceState(long timestamp, AccountSourceState? protectedState)
    {
        LinkedListNode<AccountSourceKey>? node = _accountSourceRetention.First;

        while (node is not null)
        {
            LinkedListNode<AccountSourceKey>? next = node.Next;
            AccountSourceState accountSourceState = _accountSources[node.Value];

            if (!RetentionExpired(accountSourceState.LastActivityTimestamp, timestamp))
            {
                return false;
            }

            if (ReferenceEquals(accountSourceState, protectedState))
            {
                node = next;
                continue;
            }

            if (accountSourceState.InFlightAttempts != 0)
            {
                TouchAccountSourceState(accountSourceState, timestamp);
                node = next;
                continue;
            }

            RemoveAccountSourceState(accountSourceState);
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

    private void TouchAccountSourceState(AccountSourceState accountSourceState, long timestamp)
    {
        accountSourceState.LastActivityTimestamp = timestamp;

        if (accountSourceState.RetentionNode.List is not null)
        {
            _accountSourceRetention.Remove(accountSourceState.RetentionNode);
        }

        _accountSourceRetention.AddLast(accountSourceState.RetentionNode);
    }

    private void RemoveSourceState(SourceState sourceState)
    {
        _sources.Remove(sourceState.Key);

        if (sourceState.RetentionNode.List is not null)
        {
            _sourceRetention.Remove(sourceState.RetentionNode);
        }
    }

    private void RemoveAccountState(AccountState accountState)
    {
        _accounts.Remove(accountState.AccountId);
    }

    private void RemoveAccountSourceState(AccountSourceState accountSourceState)
    {
        _accountSources.Remove(accountSourceState.Key);

        if (accountSourceState.RetentionNode.List is not null)
        {
            _accountSourceRetention.Remove(accountSourceState.RetentionNode);
        }
    }

    private static SourceKey CreateSourceKey(IPAddress remoteAddress)
    {
        Span<byte> bytes = stackalloc byte[16];

        if (!remoteAddress.TryWriteBytes(bytes, out int bytesWritten))
        {
            throw new InvalidOperationException("The remote IP address could not be represented as bytes.");
        }

        if (remoteAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            if (bytesWritten != 4)
            {
                throw new InvalidOperationException("An IPv4 address produced an unexpected byte length.");
            }

            return new SourceKey(IsIpv6: false, Network: BinaryPrimitives.ReadUInt32BigEndian(bytes));
        }

        if (remoteAddress.AddressFamily != AddressFamily.InterNetworkV6 || bytesWritten != 16)
        {
            throw new ArgumentException("Only IPv4 and IPv6 remote addresses are supported.", nameof(remoteAddress));
        }

        if (remoteAddress.IsIPv4MappedToIPv6)
        {
            return new SourceKey(IsIpv6: false, Network: BinaryPrimitives.ReadUInt32BigEndian(bytes[12..]));
        }

        return new SourceKey(IsIpv6: true, Network: BinaryPrimitives.ReadUInt64BigEndian(bytes));
    }

    private readonly record struct SourceKey(bool IsIpv6, ulong Network);

    private readonly record struct AccountSourceKey(uint AccountId, SourceKey Source);

    private sealed class SourceState(SourceKey key, int availableTokens, long timestamp)
    {
        public SourceKey Key { get; } = key;
        public double AvailableTokens { get; set; } = availableTokens;
        public int InFlightRequests { get; set; }
        public long LastRefillTimestamp { get; set; } = timestamp;
        public long LastActivityTimestamp { get; set; } = timestamp;
        public LinkedListNode<SourceKey> RetentionNode { get; } = new(key);
    }

    private sealed class AccountState(uint accountId)
    {
        public uint AccountId { get; } = accountId;
        public int InFlightAttempts { get; set; }
    }

    private sealed class AccountSourceState(AccountSourceKey key, long timestamp)
    {
        public AccountSourceKey Key { get; } = key;
        public int FailedAttempts { get; set; }
        public int InFlightAttempts { get; set; }
        public bool IsLockedOut { get; set; }
        public long FailureWindowStartedTimestamp { get; set; } = timestamp;
        public long LockoutStartedTimestamp { get; set; } = timestamp;
        public long LastActivityTimestamp { get; set; } = timestamp;
        public LinkedListNode<AccountSourceKey> RetentionNode { get; } = new(key);
    }

    private sealed class RequestLease(AccountAuthenticationProtection owner, SourceState sourceState)
        : IAccountAuthenticationRequestLease
    {
        private const int Active = 0;
        private const int Disposed = 1;

        private int _state;

        public void Dispose()
        {
            int previousState = Interlocked.Exchange(ref _state, Disposed);

            if (previousState == Active)
            {
                owner.ReleaseRequest(sourceState);
            }
        }
    }

    private sealed class AttemptLease(AccountAuthenticationProtection owner, AccountState accountState, AccountSourceState accountSourceState)
        : IAccountAuthenticationAttemptLease
    {
        private const int Active = 0;
        private const int Completed = 1;
        private const int Disposed = 2;

        private int _state;

        public void Complete(bool credentialsAccepted)
        {
            int previousState = Interlocked.CompareExchange(ref _state, Completed, Active);

            if (previousState == Disposed)
            {
                throw new ObjectDisposedException(nameof(AttemptLease));
            }

            if (previousState != Active)
            {
                throw new InvalidOperationException("The authentication attempt has already been completed.");
            }

            owner.CompleteAttempt(accountState, accountSourceState, credentialsAccepted);
        }

        public void Dispose()
        {
            int previousState = Interlocked.Exchange(ref _state, Disposed);

            if (previousState == Active)
            {
                owner.AbandonAttempt(accountState, accountSourceState);
            }
        }
    }
}
