namespace OpenConquer.Domain.World;

/// <summary>Tile distance as the 5517 client computes it: round to nearest, halves up.</summary>
/// <remarks>Map_CalculateRoundedTileDistanceByAdjustmentMode (0x61E5F0), mode 1.</remarks>
public static class MapDistance
{
    public static int RoundedTileDistance(int fromX, int fromY, int toX, int toY)
    {
        long dx = (long)fromX - toX;
        long dy = (long)fromY - toY;
        long squared = dx * dx + dy * dy;
        if (squared == 0)
        {
            return 0;
        }

        long low = (long)Math.Sqrt(squared);
        while (low * low > squared)
        {
            low--;
        }
        while ((low + 1) * (low + 1) <= squared)
        {
            low++;
        }
        long lowSquared = low * low;
        long highSquared = (low + 1) * (low + 1);
        return checked((int)(highSquared - lowSquared <= 2 * (squared - lowSquared) ? low + 1 : low));
    }
}
