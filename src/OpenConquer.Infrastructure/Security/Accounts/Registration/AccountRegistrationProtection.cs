using System.Diagnostics.CodeAnalysis;
using System.Net;
using OpenConquer.Application.Accounts.Registration;

namespace OpenConquer.Infrastructure.Security.Accounts.Registration;

/// <summary>
/// Bounds self-service registration: a fixed number of attempts per source per window, one in flight per
/// source, and a global cap on concurrent password hashing.
/// </summary>
/// <remarks>
/// Every admitted attempt counts, including ones refused because the name is taken, so a source cannot probe
/// names faster than its quota. Sources are tracked in window-start order and expire together with their window.
/// </remarks>
internal sealed class AccountRegistrationProtection(AccountRegistrationProtectionOptions options, TimeProvider timeProvider)
    : IAccountRegistrationLimiter
{
    private readonly AccountRegistrationProtectionOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly Lock _gate = new();
    private readonly Dictionary<RemoteAddressSourceKey, SourceState> _sources = [];
    private readonly LinkedList<SourceState> _windows = [];
    private int _inFlight;

    public bool TryBeginRegistration(IPAddress remoteAddress, [NotNullWhen(true)] out IDisposable? lease)
    {
        ArgumentNullException.ThrowIfNull(remoteAddress);

        RemoteAddressSourceKey key = RemoteAddressSourceKey.Create(remoteAddress);
        long now = _timeProvider.GetTimestamp();

        lock (_gate)
        {
            ExpireWindows(now);

            if (_inFlight >= _options.MaximumConcurrentAttempts)
            {
                lease = null;
                return false;
            }

            if (_sources.TryGetValue(key, out SourceState? source))
            {
                if (source.InFlight || source.Attempts >= _options.AttemptLimitPerSource)
                {
                    lease = null;
                    return false;
                }
            }
            else
            {
                if (_sources.Count >= _options.MaximumTrackedSources)
                {
                    lease = null;
                    return false;
                }

                source = new SourceState(key, now);
                _windows.AddLast(source);
                _sources.Add(key, source);
            }

            source.Attempts++;
            source.InFlight = true;
            _inFlight++;
            lease = new Lease(this, source);
            return true;
        }
    }

    private void Release(SourceState source)
    {
        lock (_gate)
        {
            if (!source.InFlight || _inFlight < 1)
            {
                throw new InvalidOperationException("The registration admission state is inconsistent.");
            }

            source.InFlight = false;
            _inFlight--;
            ExpireWindows(_timeProvider.GetTimestamp());
        }
    }

    private void ExpireWindows(long now)
    {
        while (_windows.First is { } first &&
            !first.Value.InFlight &&
            _timeProvider.GetElapsedTime(first.Value.WindowStart, now) >= _options.AttemptWindow)
        {
            _windows.RemoveFirst();
            _sources.Remove(first.Value.Key);
        }
    }

    private sealed class SourceState(RemoteAddressSourceKey key, long windowStart)
    {
        public RemoteAddressSourceKey Key { get; } = key;
        public long WindowStart { get; } = windowStart;
        public int Attempts { get; set; }
        public bool InFlight { get; set; }
    }

    private sealed class Lease(AccountRegistrationProtection owner, SourceState source) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release(source);
            }
        }
    }
}
