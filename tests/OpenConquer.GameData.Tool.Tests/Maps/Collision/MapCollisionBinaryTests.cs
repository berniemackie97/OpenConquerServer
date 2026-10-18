using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.GameData.Tool.Maps.Collision;
using OpenConquer.GameData.Tool.Maps.Terrain;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Tests.Maps.Collision;

public sealed class MapCollisionBinaryTests
{
    private static readonly MapCollisionLoadLimits s_limits = new(
        maximumOverridesPerTerrain: 100, maximumTotalOverrides: 1000,
        maximumArtifactBytes: 1024 * 1024);

    [Fact]
    public void Fingerprint_MatchesCanonicalBaseTerrainFooter()
    {
        using TemporaryDirectory directory = new();
        MapBaseTerrain terrain = CreateTerrain();
        string path = Path.Combine(directory.Path, "900001.ocbt");

        MapBaseTerrainBinaryWriter.Write(path, terrain);

        byte[] artifact = File.ReadAllBytes(path);
        byte[] fingerprint = MapBaseTerrainFingerprint.Compute(terrain, TestContext.Current.CancellationToken);

        Assert.Equal(32, fingerprint.Length);
        Assert.Equal(artifact[^32..], fingerprint);
        Assert.Equal("eb84206b6321ad52c02b33ac4f49bfe06bca1e909efc1681ab7dc1b1f207d5de",
            Convert.ToHexStringLower(fingerprint));
    }

    [Fact]
    public void Write_KnownCollision_ProducesExactVersionOneBytes()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "900001.ocmc");

        MapCollisionBinaryWriter.Write(path, CreateCollision(), s_limits, TestContext.Current.CancellationToken);

        const string expected =
            "4f434d4301000000a1bb0d00020000000100000001000000"
            + "eb84206b6321ad52c02b33ac4f49bfe06bca1e909efc1681ab7dc1b1f207d5de"
            + "01000000280000001b00"
            + "5cb9fd82ca55106bb5df0aedf4b51f4d2ea98acfeff3c50e1c38692f312be0ec";

        Assert.Equal(Convert.FromHexString(expected), File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Read_ValidCollision_RoundTripsFinalEffectiveCells()
    {
        using TemporaryDirectory directory = new();

        MapTerrainCollision original = CreateCollision();
        string path = Write(directory.Path, original);

        MapTerrainCollision loaded = await MapCollisionBinaryReader.ReadAsync(
            path, original.BaseTerrain, s_limits, TestContext.Current.CancellationToken);

        Assert.Equal(original.MapDataId, loaded.MapDataId);
        Assert.Equal(original.Overrides, loaded.Overrides);
        Assert.Equal(new MapBaseTerrainCell(12, 0, -15), loaded.GetEffectiveCell(0, 0));
        Assert.Equal(new MapBaseTerrainCell(40, 0, 27), loaded.GetEffectiveCell(1, 0));
    }

    [Fact]
    public async Task Read_ZeroOverrides_IsValidAndRetainsBaseTerrain()
    {
        using TemporaryDirectory directory = new();

        MapBaseTerrain baseTerrain = CreateTerrain();
        MapTerrainCollision collision = new(baseTerrain, []);
        string path = Write(directory.Path, collision);

        MapTerrainCollision loaded = await MapCollisionBinaryReader.ReadAsync(
            path, baseTerrain, s_limits, TestContext.Current.CancellationToken);

        Assert.Equal(MapCollisionBinaryFormat.MinimumLength, new FileInfo(path).Length);
        Assert.Equal(0, loaded.OverrideCount);
        Assert.Equal(baseTerrain.GetCell(0, 0), loaded.GetEffectiveCell(0, 0));
        Assert.Equal(baseTerrain.GetCell(1, 0), loaded.GetEffectiveCell(1, 0));
    }

    [Fact]
    public async Task Read_DifferentBaseTerrain_IsRejected()
    {
        using TemporaryDirectory directory = new();

        MapTerrainCollision collision = CreateCollision();
        string path = Write(directory.Path, collision);

        MapBaseTerrain different = new(900001, 2, 1,
        [
            new MapBaseTerrainCell(13, 0, -15),
            new MapBaseTerrainCell(24, 1, 32)
        ],
        [
            new MapTerrainExit(1, 0, 7)
        ]);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            _ = await MapCollisionBinaryReader.ReadAsync(path, different, s_limits, TestContext.Current.CancellationToken);
        });

        Assert.Contains("different canonical base-terrain content", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_ChangedPayloadWithoutMatchingChecksum_IsRejected()
    {
        using TemporaryDirectory directory = new();
        string path = Write(directory.Path, CreateCollision());

        byte[] payload = File.ReadAllBytes(path);
        payload[60] ^= 1;
        File.WriteAllBytes(path, payload);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            _ = await MapCollisionBinaryReader.ReadAsync(path, CreateTerrain(), s_limits, TestContext.Current.CancellationToken);
        });

        Assert.Contains("SHA-256 integrity check", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_UnsupportedVersionWithValidChecksum_IsRejected()
    {
        using TemporaryDirectory directory = new();
        string path = Write(directory.Path, CreateCollision());

        byte[] payload = File.ReadAllBytes(path);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), 2);
        RewriteWithValidChecksum(path, payload);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            _ = await MapCollisionBinaryReader.ReadAsync(path, CreateTerrain(), s_limits, TestContext.Current.CancellationToken);
        });

        Assert.Contains("unsupported format header", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_InvalidRecordCountWithValidChecksum_IsRejected()
    {
        using TemporaryDirectory directory = new();
        string path = Write(directory.Path, CreateCollision());

        byte[] payload = File.ReadAllBytes(path);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20), 2);
        RewriteWithValidChecksum(path, payload);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            _ = await MapCollisionBinaryReader.ReadAsync(path, CreateTerrain(), s_limits, TestContext.Current.CancellationToken);
        });

        Assert.Contains("length", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_RedundantOverrideWithValidChecksum_IsRejected()
    {
        using TemporaryDirectory directory = new();
        string path = Write(directory.Path, CreateCollision());

        byte[] payload = File.ReadAllBytes(path);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(60), 24);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(62), 1);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(64), 32);
        RewriteWithValidChecksum(path, payload);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            _ = await MapCollisionBinaryReader.ReadAsync(path, CreateTerrain(), s_limits, TestContext.Current.CancellationToken);
        });

        Assert.Contains("redundant override", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_DuplicateIndicesWithValidChecksum_AreRejected()
    {
        using TemporaryDirectory directory = new();

        MapBaseTerrain terrain = CreateTerrain();
        MapTerrainCollision collision = new(terrain,
        [
            new MapTerrainCollisionOverride(0, new MapBaseTerrainCell(30, 1, 10)),
            new MapTerrainCollisionOverride(1, new MapBaseTerrainCell(40, 0, 27))
        ]);

        string path = Write(directory.Path, collision);
        byte[] payload = File.ReadAllBytes(path);

        BinaryPrimitives.WriteUInt32LittleEndian(
            payload.AsSpan(MapCollisionBinaryFormat.HeaderLength + MapCollisionBinaryFormat.OverrideLength), 0);

        RewriteWithValidChecksum(path, payload);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            _ = await MapCollisionBinaryReader.ReadAsync(path, terrain, s_limits, TestContext.Current.CancellationToken);
        });

        Assert.Contains("unordered override", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_ArtifactExceedingFileLimit_IsRejected()
    {
        using TemporaryDirectory directory = new();
        string path = Write(directory.Path, CreateCollision());

        MapCollisionLoadLimits limits = new(100, 1000, MapCollisionBinaryFormat.MinimumLength);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            _ = await MapCollisionBinaryReader.ReadAsync(path, CreateTerrain(), limits, TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public async Task Read_TruncatedArtifact_IsRejected()
    {
        using TemporaryDirectory directory = new();
        string path = Write(directory.Path, CreateCollision());

        byte[] payload = File.ReadAllBytes(path);
        File.WriteAllBytes(path, payload[..^1]);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            _ = await MapCollisionBinaryReader.ReadAsync(path, CreateTerrain(), s_limits, TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public async Task Publish_TwoRuns_ProduceIdenticalArtifacts()
    {
        using TemporaryDirectory directory = new();

        MapTerrainCollision populated = CreateCollision();
        MapTerrainCollision empty = new(new MapBaseTerrain(900002, 1, 1,
        [
            new MapBaseTerrainCell(1, 0, 0)
        ], []), []);

        string first = Path.Combine(directory.Path, "first");
        string second = Path.Combine(directory.Path, "second");

        await MapCollisionContentGenerator.PublishAsync(
            [empty, populated], first, s_limits, TestContext.Current.CancellationToken);

        await MapCollisionContentGenerator.PublishAsync(
            [populated, empty], second, s_limits, TestContext.Current.CancellationToken);

        string[] expectedFiles = ["900001.ocmc", "900002.ocmc"];

        Assert.Equal(expectedFiles, Directory.GetFiles(first).Select(Path.GetFileName).Order().ToArray());
        Assert.Equal(expectedFiles, Directory.GetFiles(second).Select(Path.GetFileName).Order().ToArray());

        foreach (string filename in expectedFiles)
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(first, filename)),
                File.ReadAllBytes(Path.Combine(second, filename)));
        }
    }

    [Fact]
    public async Task Publish_FailedValidation_DoesNotCreateOutput()
    {
        using TemporaryDirectory directory = new();
        string destination = Path.Combine(directory.Path, "collision");

        MapCollisionLoadLimits limits = new(100, 1000, MapCollisionBinaryFormat.MinimumLength);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            MapCollisionContentGenerator.PublishAsync(
                [CreateCollision()], destination, limits, TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task Publish_ExistingDirectory_IsNotReplaced()
    {
        using TemporaryDirectory directory = new();
        string destination = Path.Combine(directory.Path, "collision");
        Directory.CreateDirectory(destination);

        string sentinel = Path.Combine(destination, "keep.txt");
        File.WriteAllText(sentinel, "existing");

        await Assert.ThrowsAsync<IOException>(() =>
            MapCollisionContentGenerator.PublishAsync(
                [CreateCollision()], destination, s_limits, TestContext.Current.CancellationToken));

        Assert.Equal("existing", File.ReadAllText(sentinel));
    }

    [Fact]
    public async Task Publish_DuplicateTerrainIdentity_IsRejected()
    {
        using TemporaryDirectory directory = new();
        string destination = Path.Combine(directory.Path, "collision");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            MapCollisionContentGenerator.PublishAsync(
                [CreateCollision(), CreateCollision()], destination,
                s_limits, TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(destination));
    }

    private static MapBaseTerrain CreateTerrain() => new(900001, 2, 1,
    [
        new MapBaseTerrainCell(12, 0, -15),
        new MapBaseTerrainCell(24, 1, 32)
    ],
    [
        new MapTerrainExit(1, 0, 7)
    ]);

    private static MapTerrainCollision CreateCollision() => new(CreateTerrain(),
    [
        new MapTerrainCollisionOverride(1, new MapBaseTerrainCell(40, 0, 27))
    ]);

    private static string Write(string directory, MapTerrainCollision collision)
    {
        string path = Path.Combine(directory, $"{collision.MapDataId}.ocmc");
        MapCollisionBinaryWriter.Write(path, collision, s_limits);
        return path;
    }

    private static void RewriteWithValidChecksum(string path, byte[] payload)
    {
        byte[] hash = SHA256.HashData(payload.AsSpan(0, payload.Length - MapCollisionBinaryFormat.HashLength));
        hash.CopyTo(payload, payload.Length - MapCollisionBinaryFormat.HashLength);
        File.WriteAllBytes(path, payload);
    }
}
