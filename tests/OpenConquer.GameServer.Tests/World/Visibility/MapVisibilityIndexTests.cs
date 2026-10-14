using OpenConquer.GameServer.World.Visibility;

namespace OpenConquer.GameServer.Tests.World.Visibility;

public sealed class MapVisibilityIndexTests
{
    [Fact]
    public void BoundaryIsNativeChebyshevEighteenNotEuclidean()
    {
        MapVisibilityIndex index = new();
        Assert.Empty(index.Enter(1, 100, 100).Entered);
        Assert.Equal(new uint[] { 1 }, index.Enter(2, 118, 118).Entered);
        Assert.Equal(new uint[] { 2 }, index.Enter(3, 119, 100).Entered);
        Assert.True(index.IsObserved(1, 2));
        Assert.True(index.IsObserved(2, 1));
        Assert.False(index.IsObserved(1, 3));
    }

    [Fact]
    public void EntryAndMovementUpdateBothSidesWithoutDuplicateSpawns()
    {
        MapVisibilityIndex index = new();
        index.Enter(1, 100, 100);
        index.Enter(2, 120, 100);
        VisibilityChange entering = index.Move(2, 118, 100);
        Assert.Equal(new uint[] { 1 }, entering.Entered);
        Assert.Empty(entering.Retained);
        Assert.True(index.IsObserved(1, 2));
        Assert.True(index.IsObserved(2, 1));
        VisibilityChange retained = index.Move(2, 117, 100);
        Assert.Empty(retained.Entered);
        Assert.Equal(new uint[] { 1 }, retained.Retained);
    }

    [Fact]
    public void DepartureEvictsBothMembershipsAndReentrySpawnsExactlyOnce()
    {
        MapVisibilityIndex index = new();
        index.Enter(1, 100, 100);
        index.Enter(2, 99, 100);
        VisibilityChange departed = index.Move(1, 140, 100);
        Assert.Equal(new uint[] { 2 }, departed.Departed);
        Assert.False(index.IsObserved(1, 2));
        Assert.False(index.IsObserved(2, 1));
        Assert.Equal(new uint[] { 2 }, index.Move(1, 110, 100).Entered);
        Assert.Empty(index.Move(1, 110, 100).Entered);
    }

    [Fact]
    public void LeavingDisconnectsOnlyCurrentObserversAndAllowsIdentityReuse()
    {
        MapVisibilityIndex index = new();
        index.Enter(1, 100, 100);
        index.Enter(2, 99, 100);
        index.Enter(3, 200, 200);
        Assert.Equal(new uint[] { 1 }, index.Leave(2));
        Assert.False(index.IsObserved(1, 2));
        Assert.Empty(index.Leave(2));
        Assert.Equal(new uint[] { 1 }, index.Enter(2, 99, 99).Entered);
        Assert.Equal(3, index.Count);
    }

    [Fact]
    public void ObserverSnapshotChangesOnlyWithSymmetricMembership()
    {
        MapVisibilityIndex index = new();
        index.Enter(7, 100, 100);
        index.Enter(8, 118, 118);
        index.Enter(9, 99, 100);
        index.Enter(10, 150, 150);

        Assert.Equal(new uint[] { 8, 9 }, index.Observers(7));
        index.Move(9, 150, 150);
        Assert.Equal(new uint[] { 8 }, index.Observers(7));
        index.Leave(8);
        Assert.Empty(index.Observers(7));
        Assert.Empty(index.Observers(8));
        Assert.Equal(4 - 1, index.Count);
    }

    [Fact]
    public void QueryCanOnlySeeCurrentlyKnownPeers()
    {
        MapVisibilityIndex index = new();
        index.Enter(1, 100, 100);
        index.Enter(2, 118, 118);
        index.Enter(3, 120, 120);
        Assert.True(index.IsObserved(1, 2));
        Assert.False(index.IsObserved(1, 3));
        Assert.False(index.IsObserved(1, 1));
        index.Leave(2);
        Assert.False(index.IsObserved(1, 2));
    }

    [Fact]
    public void SparseCrossCellMovesDoNotAccumulateStaleMembers()
    {
        MapVisibilityIndex index = new();
        index.Enter(1, 20, 20);
        index.Enter(2, 21, 21);
        for (ushort x = 40; x < 2_000; x += 19)
        {
            index.Move(2, x, 21);
        }
        Assert.False(index.IsObserved(1, 2));
        Assert.Empty(index.Leave(2));
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void DeterministicInterleavedMovesPreserveExactSymmetricInterest()
    {
        MapVisibilityIndex index = new();
        Random random = new(5517);
        Dictionary<uint, (ushort X, ushort Y)> positions = [];
        for (uint id = 1; id <= 48; id++)
        {
            ushort x = (ushort)random.Next(50, 151);
            ushort y = (ushort)random.Next(50, 151);
            index.Enter(id, x, y);
            positions.Add(id, (x, y));
        }
        for (int turn = 0; turn < 240; turn++)
        {
            uint id = (uint)random.Next(1, 49);
            ushort x = (ushort)random.Next(50, 151);
            ushort y = (ushort)random.Next(50, 151);
            index.Move(id, x, y);
            positions[id] = (x, y);
            foreach (var a in positions)
            {
                foreach (var b in positions)
                {
                    bool expected = a.Key != b.Key &&
                        Math.Abs(a.Value.X - b.Value.X) <= MapVisibilityIndex.Radius &&
                        Math.Abs(a.Value.Y - b.Value.Y) <= MapVisibilityIndex.Radius;
                    Assert.Equal(expected, index.IsObserved(a.Key, b.Key));
                }
            }
        }
    }

    [Fact]
    public void DuplicateIdentityAndZeroIdentityFailClosed()
    {
        MapVisibilityIndex index = new();
        Assert.Throws<InvalidOperationException>(() => index.Enter(0, 1, 1));
        index.Enter(1, 1, 1);
        Assert.Throws<InvalidOperationException>(() => index.Enter(1, 2, 2));
        Assert.Equal(1, index.Count);
    }
}
