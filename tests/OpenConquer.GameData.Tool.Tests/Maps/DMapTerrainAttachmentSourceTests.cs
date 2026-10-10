using System.Text;
using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.GameData.Tool.Formats.Maps;
using OpenConquer.GameData.Tool.Maps;

namespace OpenConquer.GameData.Tool.Tests.Maps;

public sealed class DMapTerrainAttachmentSourceTests
{
    [Fact]
    public void Enumerate_UsesGroupAnchorObjectOffsetAndReverseGridCoordinates()
    {
        DMapScenerySection scenery = CreateScenery();
        TerrainObjectList objects = CreateObjectList();

        MapTerrainAttachment[] attachments = DMapTerrainAttachmentSource
            .Enumerate(scenery, path =>
            {
                Assert.Equal("map/scene/example.obj", path);
                return objects;
            })
            .ToArray();

        Assert.Equal(2, attachments.Length);

        Assert.Equal(new MapTerrainAttachment(2, 1, 30, 1, 10), attachments[0]);
        Assert.Equal(new MapTerrainAttachment(1, 1, 40, 0, -5), attachments[1]);
    }

    [Fact]
    public void Enumerate_ProducesAttachmentsUsableByCanonicalComposer()
    {
        DMapScenerySection scenery = CreateScenery();
        TerrainObjectList objects = CreateObjectList();

        MapBaseTerrainCell[] cells =
            Enumerable.Repeat(new MapBaseTerrainCell(10, 0, 100), 6).ToArray();

        MapBaseTerrain terrain = new(1000, 3, 2, cells, []);

        MapTerrainCollision collision = MapTerrainCollisionComposer.Compose(
            terrain,
            DMapTerrainAttachmentSource.Enumerate(scenery, _ => objects),
            100);

        Assert.Equal(2, collision.OverrideCount);
        Assert.Equal(new MapBaseTerrainCell(30, 1, 110), collision.GetEffectiveCell(2, 1));
        Assert.Equal(new MapBaseTerrainCell(40, 0, 95), collision.GetEffectiveCell(1, 1));
        Assert.Equal(new MapBaseTerrainCell(10, 0, 100), collision.GetEffectiveCell(0, 0));
    }

    [Fact]
    public void Enumerate_MissingObjectList_IsRejected()
    {
        DMapScenerySection scenery = CreateScenery();

        Assert.Throws<InvalidDataException>(() =>
            DMapTerrainAttachmentSource.Enumerate(scenery, _ => null!).ToArray());
    }

    private static DMapScenerySection CreateScenery()
    {
        using MemoryStream stream = new();

        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(1);
            writer.Write(0x01);

            byte[] path = Encoding.ASCII.GetBytes("map/scene/example.obj");
            writer.Write(path);
            writer.Write(new byte[0x104 - path.Length]);

            writer.Write(1);
            writer.Write(1);
        }

        stream.Position = 0;

        return DMapScenerySectionReader.Read(stream, 10);
    }

    private static TerrainObjectList CreateObjectList()
    {
        using MemoryStream stream = new();

        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(1);
            writer.Write(new byte[0x140]);

            writer.Write(0);
            writer.Write(0);
            writer.Write(30);

            writer.Write(2);
            writer.Write(1);

            writer.Write(0);
            writer.Write(1);
            writer.Write(0);
            writer.Write(0);

            writer.Write(1);
            writer.Write(30);
            writer.Write(10);

            writer.Write(0);
            writer.Write(40);
            writer.Write(-5);
        }

        stream.Position = 0;

        return TerrainObjectListReader.Read(stream, 10, 100);
    }
}
