using OpenConquer.Domain.Characters;

namespace OpenConquer.GameServer.World.Presence;

internal sealed class CharacterPresenceDirectory : ICharacterPresenceReader, ICharacterPresenceRegistrar
{
    private readonly Lock _gate = new();
    private readonly Dictionary<uint, Registration> _registrations = [];

    public ICharacterPresenceLease Register(uint characterId)
    {
        ValidateCharacterId(characterId);

        Registration registration = new(this, characterId);
        Registration? displaced;

        lock (_gate)
        {
            _registrations.TryGetValue(characterId, out displaced);
            displaced?.MarkRevoked();
            _registrations[characterId] = registration;
        }

        displaced?.SignalRevocation();
        return registration;
    }

    public bool IsOnline(uint characterId)
    {
        ValidateCharacterId(characterId);

        lock (_gate)
        {
            return _registrations.ContainsKey(characterId);
        }
    }

    private void Release(Registration registration)
    {
        lock (_gate)
        {
            registration.MarkRevoked();

            if (_registrations.TryGetValue(registration.CharacterId, out Registration? current) && ReferenceEquals(current, registration))
            {
                _registrations.Remove(registration.CharacterId);
            }
        }

        registration.SignalRevocation();
    }

    private static void ValidateCharacterId(uint characterId)
    {
        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), characterId, "Presence requires a player character ID.");
        }
    }

    private sealed class Registration : ICharacterPresenceLease
    {
        private readonly CancellationTokenSource _revocation = new();
        private CharacterPresenceDirectory? _directory;
        private int _revoked;

        public Registration(CharacterPresenceDirectory directory, uint characterId)
        {
            _directory = directory;
            CharacterId = characterId;
            RevocationToken = _revocation.Token;
        }

        public uint CharacterId { get; }
        public bool IsRevoked => Volatile.Read(ref _revoked) != 0;
        public CancellationToken RevocationToken { get; }

        public void Dispose()
        {
            CharacterPresenceDirectory? directory = Interlocked.Exchange(ref _directory, null);
            if (directory is null)
            {
                return;
            }

            directory.Release(this);
            _revocation.Dispose();
        }

        public void MarkRevoked() => Volatile.Write(ref _revoked, 1);

        public void SignalRevocation()
        {
            try
            {
                _revocation.Cancel();
            }
            catch (ObjectDisposedException) when (IsRevoked)
            {
            }
        }
    }
}
