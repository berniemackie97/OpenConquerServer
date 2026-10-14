using OpenConquer.Application.World;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.World.Execution;
using OpenConquer.GameServer.World.Visibility;

namespace OpenConquer.GameServer.World.Sessions;

/// <summary>
/// Transfers a completed existing-character bootstrap into exactly one authoritative world session.
/// </summary>
internal sealed class WorldSessionAdmissionProcessor(WorldRuntime runtime, WorldVisibilityService? visibility = null)
{
    private readonly WorldRuntime _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    private readonly WorldVisibilityService? _visibility = visibility;

    public async ValueTask<WorldSession> ProcessAsync(ExistingCharacterBootstrapCompleteConnection bootstrap, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);

        ExistingCharacterGameConnection? connection = bootstrap.TakeConnection();
        WorldCharacterOwnership? ownership = null;
        WorldSession? session = null;
        TaskCompletionSource admission = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool tracked = false;

        try
        {
            _runtime.TrackAdmission(admission.Task);
            tracked = true;
            cancellationToken.ThrowIfCancellationRequested();
            connection.ThrowIfRevoked();

            WorldMapContent map = bootstrap.Map;

            if (!_runtime.TryGetMap(map.MapId, out WorldMapContent? authoritativeMap) || !ReferenceEquals(authoritativeMap, map))
            {
                throw new InvalidDataException($"Completed bootstrap map {map.MapId} is not the world runtime's canonical map snapshot.");
            }

            ownership = await _runtime.AdmitCharacterAsync(bootstrap.Profile, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            connection.ThrowIfRevoked();

            session = new WorldSession(_runtime, connection, bootstrap, ownership.Value, _visibility);
            _runtime.TrackSession(ownership.Value, session.Completion);
            connection = null;
            ownership = null;

            session.Activate();
            cancellationToken.ThrowIfCancellationRequested();
            session.ThrowIfInactive();
            session.EnterVisibility();

            return session;
        }
        catch (Exception processingFailure)
        {
            List<Exception> failures = [processingFailure];

            if (session is not null)
            {
                try
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            else
            {
                if (connection is not null)
                {
                    try
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }

                if (ownership is WorldCharacterOwnership reservation)
                {
                    try
                    {
                        await _runtime.ReleaseCharacterAsync(reservation).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);

                        try
                        {
                            await _runtime.StopPartitionExecutionAsync().ConfigureAwait(false);
                        }
                        catch (Exception stopFailure)
                        {
                            failures.Add(stopFailure);
                        }
                    }
                }
            }

            if (failures.Count > 1)
            {
                throw new AggregateException("World-session admission failed and cleanup also failed.", failures);
            }

            throw;
        }
        finally
        {
            if (tracked)
            {
                admission.TrySetResult();
                _runtime.UntrackAdmission(admission.Task);
            }
        }
    }
}
