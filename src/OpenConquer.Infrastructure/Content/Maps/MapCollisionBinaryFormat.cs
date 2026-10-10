namespace OpenConquer.Infrastructure.Content.Maps;

public static class MapCollisionBinaryFormat
{
    public const ushort FormatVersion = 1;
    public const string FileExtension = ".ocmc";
    public const int HeaderLength = 56;
    public const int OverrideLength = 10;
    public const int HashLength = 32;
    public const int MinimumLength = HeaderLength + HashLength;
}
