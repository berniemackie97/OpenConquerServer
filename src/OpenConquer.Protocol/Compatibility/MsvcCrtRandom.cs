namespace OpenConquer.Protocol.Compatibility;

/// <summary>
/// Reproduces the Microsoft C runtime <c>srand</c>/<c>rand</c> sequence.
/// </summary>
internal struct MsvcCrtRandom(uint seed)
{
    private const uint Multiplier = 0x343FD;
    private const uint Increment = 0x269EC3;
    private const uint ResultMask = 0x7FFF;

    private uint _state = seed;

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
