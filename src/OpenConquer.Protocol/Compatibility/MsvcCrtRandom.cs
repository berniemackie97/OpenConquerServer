namespace OpenConquer.Protocol.Compatibility;

/// <summary>
/// Reproduces the Microsoft C runtime <c>srand</c>/<c>rand</c>
/// sequence used by Conquer Online 5517 protocol compatibility code.
/// </summary>
/// <remarks>
/// This is a deterministic compatibility primitive, not a cryptographically
/// secure random-number generator.
/// </remarks>
internal struct MsvcCrtRandom
{
    private const uint Multiplier = 0x343FD;
    private const uint Increment = 0x269EC3;
    private const uint ResultMask = 0x7FFF;

    private uint _state;

    public MsvcCrtRandom(uint seed)
    {
        _state = seed;
    }

    public int Next()
    {
        _state = unchecked((_state * Multiplier) + Increment);

        return (int)((_state >> 16) & ResultMask);
    }

    public byte NextByte()
    {
        return (byte)Next();
    }

    public void FillBytes(Span<byte> destination)
    {
        for (int index = 0; index < destination.Length; index++)
        {
            destination[index] = NextByte();
        }
    }
}
