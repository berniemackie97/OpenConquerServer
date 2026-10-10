using System.Text;
using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.GameData.Tool.Maps;

namespace OpenConquer.GameData.Tool.Tests.Maps;

public sealed class MapCollisionSourceVerifierTests
{
    private static readonly MapTerrainLoadLimits s_terrainLimits = new(
        maximumWidth: 32, maximumHeight: 32, maximumCellsPerTerrain: 1024,
        maximumTotalCells: 4096, maximumExitsPerTerrain: 100,
        maximumArtifactBytes: 1024 * 1024, maximumContainerBytes: 1024 * 1024);

    private static readonly MapCollisionSourceLimits s_collisionLimits = new(
        maximumScenerySubcomponents: 100, maximumObjectListsPerTerrain: 10,
        maximumObjectsPerList: 10, maximumCellsPerList: 100,
        maximumObjectFileBytes: 1024 * 1024, maximumAttachmentsPerTerrain: 100,
        maximumTotalAttachments: 1000, maximumTotalObjectBytes: 1024 * 1024);

    [Fact]
    public async Task Verify_CompleteSource_MatchesCanonicalTerrainAndComposesCollision()
    {
        using Fixture fixture = new();

        MapCollisionSourceReport report = await VerifyAsync(fixture);

        MapCollisionSourceResult result = Assert.Single(report.Terrains);

        Assert.Equal(900001u, result.MapDataId);
        Assert.Equal(1, result.ScenerySubcomponents);
        Assert.Equal(1, result.TerrainObjectGroups);
        Assert.Equal(1, result.ObjectListFiles);
        Assert.Equal(2, result.Attachments);
        Assert.Equal(2, result.EffectiveOverrides);

        Assert.Equal(1, report.TerrainCount);
        Assert.Equal(1, report.TotalGroups);
        Assert.Equal(2, report.TotalAttachments);
        Assert.True(report.TotalObjectBytes > 0);
    }

    [Fact]
    public async Task Verify_SourceAndCanonicalBaseDisagree_IsRejected()
    {
        using Fixture fixture = new();

        fixture.ReplaceCanonicalTerrain(new MapBaseTerrainCell(999, 0, -15));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            VerifyAsync(fixture));

        Assert.Contains("MapDataId 900001", exception.Message, StringComparison.Ordinal);
        Assert.Contains("differs at tile", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_MissingObjectList_IsRejected()
    {
        using Fixture fixture = new();

        File.Delete(fixture.ObjectListPath);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            VerifyAsync(fixture));

        Assert.Contains("MapDataId 900001", exception.Message, StringComparison.Ordinal);
        Assert.Contains("could not be resolved", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_ObjectListPathEscapesRoot_IsRejected()
    {
        using Fixture fixture = new("../escape.obj");

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            VerifyAsync(fixture));

        Assert.Contains("MapDataId 900001", exception.Message, StringComparison.Ordinal);
        Assert.Contains("invalid segment", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_TruncatedScenery_IsRejected()
    {
        using Fixture fixture = new(includeScenery: false);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            VerifyAsync(fixture));

        Assert.Contains("MapDataId 900001", exception.Message, StringComparison.Ordinal);
        Assert.Contains("truncated", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_AttachmentLimit_IsEnforced()
    {
        using Fixture fixture = new();

        MapCollisionSourceLimits limits = new(
            maximumScenerySubcomponents: 100, maximumObjectListsPerTerrain: 10,
            maximumObjectsPerList: 10, maximumCellsPerList: 100,
            maximumObjectFileBytes: 1024 * 1024, maximumAttachmentsPerTerrain: 1,
            maximumTotalAttachments: 1000, maximumTotalObjectBytes: 1024 * 1024);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            VerifyAsync(fixture, limits));

        Assert.Contains("attachment count", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_SameSources_ProducesDeterministicResults()
    {
        using Fixture fixture = new();

        MapCollisionSourceReport first = await VerifyAsync(fixture);
        MapCollisionSourceReport second = await VerifyAsync(fixture);

        Assert.Equal(first.Terrains, second.Terrains);
        Assert.Equal(first.TotalAttachments, second.TotalAttachments);
        Assert.Equal(first.TotalObjectBytes, second.TotalObjectBytes);
    }

    [Fact]
    public async Task Verify_Cancellation_IsPropagated()
    {
        using Fixture fixture = new();
        using CancellationTokenSource cancellation = new();

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MapCollisionSourceVerifier.VerifyAsync(fixture.ClientRoot, fixture.DefinitionsPath,
                fixture.TerrainDirectory, s_terrainLimits, s_collisionLimits, cancellation.Token));
    }

    private static Task<MapCollisionSourceReport> VerifyAsync(Fixture fixture,
        MapCollisionSourceLimits? limits = null)
    {
        return MapCollisionSourceVerifier.VerifyAsync(fixture.ClientRoot, fixture.DefinitionsPath,
            fixture.TerrainDirectory, s_terrainLimits, limits ?? s_collisionLimits,
            TestContext.Current.CancellationToken);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _workspace;

        public Fixture(string objectListReference = "map/scene/house.obj", bool includeScenery = true)
        {
            _workspace = Path.Combine(Path.GetTempPath(), $"openconquer-collision-source-{Guid.NewGuid():N}");

            ClientRoot = Path.Combine(_workspace, "client");
            TerrainDirectory = Path.Combine(_workspace, "terrains");
            DefinitionsPath = Path.Combine(_workspace, "map-definitions.json");

            string indexDirectory = Path.Combine(ClientRoot, "Ini");
            string mapDirectory = Path.Combine(ClientRoot, "Map");
            string sceneDirectory = Path.Combine(mapDirectory, "Scene");

            Directory.CreateDirectory(indexDirectory);
            Directory.CreateDirectory(sceneDirectory);
            Directory.CreateDirectory(TerrainDirectory);

            WriteIndex(Path.Combine(indexDirectory, "GameMap.dat"));
            File.WriteAllBytes(Path.Combine(mapDirectory, "TEST.DMap"),
                CreateMap(objectListReference, includeScenery));

            ObjectListPath = Path.Combine(sceneDirectory, "HOUSE.OBJ");
            File.WriteAllBytes(ObjectListPath, CreateObjectList());

            File.WriteAllText(DefinitionsPath,
                """{"formatVersion":1,"maps":[{"mapId":900001,"mapDataId":900001,"flags":0}]}""");

            ReplaceCanonicalTerrain(new MapBaseTerrainCell(12, 0, -15));
        }

        public string ClientRoot { get; }
        public string TerrainDirectory { get; }
        public string DefinitionsPath { get; }
        public string ObjectListPath { get; }

        public void ReplaceCanonicalTerrain(MapBaseTerrainCell firstCell)
        {
            MapBaseTerrain terrain = new(900001, 2, 1,
            [
                firstCell,
                new MapBaseTerrainCell(24, 1, 32)
            ],
            [
                new MapTerrainExit(1, 0, 7)
            ]);

            MapBaseTerrainBinaryWriter.Write(Path.Combine(TerrainDirectory, "900001.ocbt"), terrain);
        }

        private static void WriteIndex(string path)
        {
            byte[] reference = Encoding.ASCII.GetBytes("map/test.dmap");

            using FileStream stream = new(path, FileMode.CreateNew);
            using BinaryWriter writer = new(stream, Encoding.ASCII);

            writer.Write(1u);
            writer.Write(900001u);
            writer.Write(checked((uint)reference.Length));
            writer.Write(reference);
            writer.Write(0u);
        }

        private static byte[] CreateMap(string objectListReference, bool includeScenery)
        {
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);

            writer.Write(0u);
            writer.Write(0u);
            writer.Write(new byte[0x104]);

            writer.Write(2u);
            writer.Write(1u);

            writer.Write((ushort)0);
            writer.Write((ushort)12);
            writer.Write((short)-15);

            writer.Write((ushort)1);
            writer.Write((ushort)24);
            writer.Write((short)32);

            writer.Write(740u);

            writer.Write(1u);
            writer.Write(1);
            writer.Write(0);
            writer.Write(7u);

            if (includeScenery)
            {
                writer.Write(1);
                writer.Write(0x01);

                WriteFixedString(writer, objectListReference, 0x104);

                writer.Write(1);
                writer.Write(0);
            }

            writer.Flush();
            return stream.ToArray();
        }

        private static byte[] CreateObjectList()
        {
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);

            writer.Write(1);
            writer.Write(new byte[0x140]);

            writer.Write(0);
            writer.Write(0);
            writer.Write(0);

            writer.Write(2);
            writer.Write(1);

            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);

            writer.Write(1);
            writer.Write(30);
            writer.Write(10);

            writer.Write(0);
            writer.Write(40);
            writer.Write(-5);

            writer.Flush();
            return stream.ToArray();
        }

        private static void WriteFixedString(BinaryWriter writer, string value, int length)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);

            if (bytes.Length >= length)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            writer.Write(bytes);
            writer.Write(new byte[length - bytes.Length]);
        }

        public void Dispose()
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }
}
