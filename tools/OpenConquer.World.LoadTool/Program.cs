using System.Text.Json;

namespace OpenConquer.World.LoadTool;

internal static class Program
{
    private const string Usage = "World.LoadTool [--sessions N] [--maps M] [--workers N] [--steps N] [--warmup N] [--queue-capacity N] [--turn-budget N] [--timeout-seconds N]";

    public static async Task<int> Main(string[] args)
    {
        if (args is ["--help"])
        {
            Console.WriteLine(Usage);
            return 0;
        }

        WorldLoadOptions options;
        try
        {
            options = WorldLoadOptions.Parse(args);
        }
        catch (ArgumentException)
        {
            // Invalid command-line input maps to a usage failure; never print raw exception text.
            Console.Error.WriteLine(Usage);
            return 2;
        }

        try
        {
            WorldLoadReport report = await new WorldLoadRunner(TimeProvider.System).RunAsync(options).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(report, WorldLoadJsonContext.Default.WorldLoadReport));
            return 0;
        }
        catch (OperationCanceledException)
        {
            // A timed-out trial must not emit a partial success report.
            Console.Error.WriteLine("Load trial exceeded its cancellation deadline.");
            return 3;
        }
        catch (InvalidOperationException)
        {
            // Queue refusal, runtime failure or a mismatched snapshot invalidates the full trial.
            Console.Error.WriteLine("Load trial failed runtime or position validation.");
            return 4;
        }
    }
}
