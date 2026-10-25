using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Bootstrap.Missions;

/// <summary>
/// Checks the mission catalog the 5517 client sends once per login, when it handles ANSWER_OK. The frames arrive back to
/// back with nothing between them, their MissionIds keep ascending across frames, and every frame except the last is full.
/// The server keeps no mission state and the client waits for no reply, so an accepted catalog is discarded.
/// </summary>
internal sealed class ExistingCharacterMissionCatalogReport
{
    /// <summary>
    /// The 5517 client's <c>ini/Questinfo.ini</c> declares 224 distinct MissionIds, which it sends as frames of 80, 80 and 64.
    /// </summary>
    public const int MaximumFrameCount = 3;

    private int _frameCount;
    private int _lastMissionId;
    private bool _isComplete;

    public void Accept(GameMissionCatalogReport frame)
    {
        if (_isComplete)
        {
            throw new InvalidDataException("The client sent a second mission catalog during login.");
        }

        if (_frameCount == MaximumFrameCount)
        {
            throw new InvalidDataException($"The mission catalog exceeds {MaximumFrameCount} frames.");
        }

        if (frame.FirstMissionId <= _lastMissionId)
        {
            throw new InvalidDataException("The mission catalog frames do not continue in ascending MissionId order.");
        }

        _frameCount++;
        _lastMissionId = frame.LastMissionId;
        _isComplete = !frame.IsFull;
    }

    /// <summary>
    /// Records that the client sent another packet, which ends a catalog it had started.
    /// </summary>
    public void EndBurst()
    {
        if (_frameCount != 0)
        {
            _isComplete = true;
        }
    }
}
