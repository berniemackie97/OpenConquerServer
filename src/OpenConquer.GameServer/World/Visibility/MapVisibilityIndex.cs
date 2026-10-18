namespace OpenConquer.GameServer.World.Visibility;

/// <summary>Per-map, symmetric, generation-independent interest graph. The hosting service fences owners.</summary>
internal sealed class MapVisibilityIndex
{
    public const int Radius = 18; // Native Map_IsTilePairOutsideVisibilityRange at 0x61ED75.
    private readonly Dictionary<uint, Entry> _entries = [];
    private readonly Dictionary<long, HashSet<uint>> _cells = [];

    public int Count => _entries.Count;

    public VisibilityChange Enter(uint id, ushort x, ushort y)
    {
        if (id == 0 || _entries.ContainsKey(id))
        {
            throw new InvalidOperationException("A visibility identity must enter a map exactly once.");
        }
        Entry entry = new(x, y);
        _entries.Add(id, entry);
        AddCell(id, entry.Cell);
        uint[] nearby = Nearby(id, x, y);
        foreach (uint other in nearby)
        {
            entry.Visible.Add(other);
            _entries[other].Visible.Add(id);
        }
        return new VisibilityChange(nearby, [], []);
    }

    public VisibilityChange Move(uint id, ushort x, ushort y)
    {
        if (!_entries.TryGetValue(id, out Entry? entry))
        {
            return VisibilityChange.Empty;
        }
        long newCell = Cell(x, y);
        if (entry.Cell != newCell)
        {
            RemoveCell(id, entry.Cell);
            AddCell(id, newCell);
            entry.Cell = newCell;
        }
        entry.X = x;
        entry.Y = y;

        HashSet<uint> nearby = [.. Nearby(id, x, y)];
        uint[] entered = [.. nearby.Where(other => !entry.Visible.Contains(other)).Order()];
        uint[] retained = [.. nearby.Where(entry.Visible.Contains).Order()];
        uint[] departed = [.. entry.Visible.Where(other => !nearby.Contains(other)).Order()];
        foreach (uint other in entered)
        {
            entry.Visible.Add(other);
            _entries[other].Visible.Add(id);
        }
        foreach (uint other in departed)
        {
            entry.Visible.Remove(other);
            _entries[other].Visible.Remove(id);
        }
        return new VisibilityChange(entered, retained, departed);
    }

    public uint[] Leave(uint id)
    {
        if (!_entries.Remove(id, out Entry? entry))
        {
            return [];
        }
        RemoveCell(id, entry.Cell);
        foreach (uint other in entry.Visible)
        {
            _entries[other].Visible.Remove(id);
        }
        return [.. entry.Visible.Order()];
    }

    public bool IsObserved(uint viewer, uint subject) =>
        _entries.TryGetValue(viewer, out Entry? entry) && entry.Visible.Contains(subject);

    /// <summary>
    /// Returns a copy of the characters that can currently see this one, in a stable order.
    /// </summary>
    public uint[] Observers(uint subject) =>
        _entries.TryGetValue(subject, out Entry? entry) ? [.. entry.Visible.Order()] : [];

    private uint[] Nearby(uint id, ushort x, ushort y)
    {
        List<uint> found = [];
        int cx = x / Radius, cy = y / Radius;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (cx + dx < 0 || cy + dy < 0 || !_cells.TryGetValue(Key(cx + dx, cy + dy), out HashSet<uint>? cell))
                {
                    continue;
                }
                foreach (uint other in cell)
                {
                    if (other != id && _entries.TryGetValue(other, out Entry? candidate)
                        && Math.Abs((int)candidate.X - x) <= Radius && Math.Abs((int)candidate.Y - y) <= Radius)
                    {
                        found.Add(other);
                    }
                }
            }
        }
        found.Sort();
        return [.. found];
    }

    private void AddCell(uint id, long cell)
    {
        if (!_cells.TryGetValue(cell, out HashSet<uint>? occupants))
        {
            occupants = [];
            _cells.Add(cell, occupants);
        }
        occupants.Add(id);
    }

    private void RemoveCell(uint id, long cell)
    {
        HashSet<uint> occupants = _cells[cell];
        occupants.Remove(id);
        if (occupants.Count == 0)
        {
            _cells.Remove(cell);
        }
    }

    private static long Cell(ushort x, ushort y) => Key(x / Radius, y / Radius);
    private static long Key(int x, int y) => ((long)x << 32) | (uint)y;

    private sealed class Entry(ushort x, ushort y)
    {
        public ushort X { get; set; } = x;
        public ushort Y { get; set; } = y;
        public long Cell { get; set; } = MapVisibilityIndex.Cell(x, y);
        public HashSet<uint> Visible { get; } = [];
    }
}

internal readonly record struct VisibilityChange(uint[] Entered, uint[] Retained, uint[] Departed)
{
    public static VisibilityChange Empty => new([], [], []);
}
