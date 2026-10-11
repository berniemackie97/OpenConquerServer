namespace OpenConquer.GameData.Tool.Formats.Maps;

/// <summary>
/// The base grid and exit-marker sections of a DMap file. Later sections are not consumed.
/// </summary>
internal sealed class DMapBaseGrid
{
    private readonly DMapBaseCell[] _cells;
    private readonly DMapExitMarker[] _exits;

    internal DMapBaseGrid(int width, int height, DMapBaseCell[] cells, DMapExitMarker[] exits)
    {
        Width = width;
        Height = height;
        _cells = cells;
        _exits = exits;
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<DMapBaseCell> Cells => Array.AsReadOnly(_cells);
    public IReadOnlyList<DMapExitMarker> Exits => Array.AsReadOnly(_exits);
}
