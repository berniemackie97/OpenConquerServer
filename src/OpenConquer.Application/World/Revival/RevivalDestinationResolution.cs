namespace OpenConquer.Application.World.Revival;

/// <summary>Why the immutable, evidence-qualified catalog can or cannot resolve a normal revival.</summary>
public enum RevivalDestinationStatus
{
    UnavailableWorldMap,
    Resolved,
    MissingSourceEvidence,
    MissingRebornMap,
    UnavailableTargetMap,
    MissingTargetEvidence,
    MissingTargetPortal,
    InvalidTargetPortal,
}

/// <summary>Only a Resolved result carries a usable world-map destination.</summary>
public readonly record struct RevivalDestinationResolution(
    RevivalDestinationStatus Status, uint MapId = 0, ushort X = 0, ushort Y = 0)
{
    public bool IsResolved => Status == RevivalDestinationStatus.Resolved;
}
