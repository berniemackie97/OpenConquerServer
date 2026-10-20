using OpenConquer.Application.Characters.Location;
using OpenConquer.Application.Characters.Login.Profile;

namespace OpenConquer.GameServer.Login.Bootstrap;

/// <summary>
/// Establishes the durable character-row writer generation only after account-session ownership
/// has been acquired, then rehydrates and validates the authoritative persisted character state.
/// Neither a rotation nor a read confers account-session or world ownership.
/// </summary>
internal sealed class FencedCharacterAdmission(
    ICharacterLocationFenceStore fence,
    ICharacterLocationObservationStore observer,
    ICharacterLoginProfileRepository profiles)
{
    private readonly ICharacterLocationFenceStore _fence = fence ?? throw new ArgumentNullException(nameof(fence));
    private readonly ICharacterLocationObservationStore _observer = observer ?? throw new ArgumentNullException(nameof(observer));
    private readonly ICharacterLoginProfileRepository _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

    public async ValueTask<(CharacterLoginProfile Profile, CharacterLocationWriteAuthority Authority)> AcquireAsync(
        CharacterLoginProfile original, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        cancellationToken.ThrowIfCancellationRequested();

        uint accountId = original.Identity.AccountId;
        uint characterId = original.Identity.CharacterId;
        CharacterLocationWriteAuthority authority = await _fence.AcquireAsync(accountId, characterId, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("The character's durable writer generation could not be acquired.");

        cancellationToken.ThrowIfCancellationRequested();
        CharacterLoginProfile rehydrated = await _profiles.FindByAccountIdAsync(accountId, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("The character disappeared during fenced login.");

        if (rehydrated.Identity.AccountId != accountId || rehydrated.Identity.CharacterId != characterId)
        {
            throw new InvalidDataException("The rehydrated character does not match the authenticated identity.");
        }

        await VerifyAsync(rehydrated, authority, cancellationToken).ConfigureAwait(false);
        return (rehydrated, authority);
    }

    /// <summary>
    /// Checks exact generation and revision as well as coordinates. A later acquirer or writer
    /// invalidates the admission; a successful check is not a distributed session lease.
    /// </summary>
    public async ValueTask VerifyAsync(CharacterLoginProfile profile, CharacterLocationWriteAuthority authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(authority);
        cancellationToken.ThrowIfCancellationRequested();

        if (profile.Identity.AccountId != authority.AccountId || profile.Identity.CharacterId != authority.CharacterId)
        {
            throw new InvalidDataException("The fenced admission identity changed.");
        }

        PersistedCharacterLocation? observed = await _observer.ObserveAsync(
            authority.AccountId, authority.CharacterId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (observed is null
            || observed.WriterToken != authority.Token
            || observed.Revision != authority.Snapshot.Revision
            || observed.LastOperationId != authority.Snapshot.LastOperationId
            || !Matches(observed.Location, authority.Snapshot.Location)
            || !Matches(profile.Location, observed.Location))
        {
            throw new InvalidDataException("The durable character location changed during login; admission is refused.");
        }
    }

    private static bool Matches(CharacterLocation left, CharacterLocation right) =>
        left.MapId == right.MapId && left.X == right.X && left.Y == right.Y;
}
