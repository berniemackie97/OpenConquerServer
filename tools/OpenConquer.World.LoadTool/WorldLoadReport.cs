using System.Text.Json.Serialization;

namespace OpenConquer.World.LoadTool;

internal sealed record LatencyPercentiles(double P50Microseconds, double P99Microseconds, double MaximumMicroseconds)
{
    public static LatencyPercentiles FromTicks(long[] samples, long frequency)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfLessThan(samples.Length, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(frequency, 1);
        Array.Sort(samples);
        ArgumentOutOfRangeException.ThrowIfNegative(samples[0]);
        double scale = 1_000_000d / frequency;
        // Nearest-rank percentiles: rank = ceiling(p * N), with one-based ranks.
        return new LatencyPercentiles(samples[(int)Math.Ceiling(samples.Length * 0.50) - 1] * scale,
            samples[(int)Math.Ceiling(samples.Length * 0.99) - 1] * scale, samples[^1] * scale);
    }
}

internal sealed record WorldLoadMeasurement(
    int CompletedCommands,
    double ElapsedSeconds,
    double CommandsPerSecond,
    LatencyPercentiles Completion,
    LatencyPercentiles Submission,
    long SubmissionAllocatedBytes,
    double SubmissionBytesPerCommand,
    long ProcessAllocatedBytes,
    double ProcessBytesPerCommand,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);

internal sealed record WorldLoadEnvironment(string Framework, string OperatingSystem, string Architecture,
    int ProcessorCount, int ThreadPoolMinimumWorkers, bool ServerGc);

internal sealed record WorldLoadReport(int SchemaVersion, DateTimeOffset RecordedAtUtc,
    WorldLoadOptions Options, WorldLoadEnvironment Environment, WorldLoadMeasurement Measurement,
    int VerifiedCharacters, ushort FinalY);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WorldLoadReport))]
internal sealed partial class WorldLoadJsonContext : JsonSerializerContext;
