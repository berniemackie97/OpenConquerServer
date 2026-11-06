using System.Globalization;

namespace OpenConquer.World.LoadTool;

internal sealed record WorldLoadOptions(
    int Sessions = 128,
    int Maps = 8,
    int Workers = 4,
    int Steps = 512,
    int Warmup = 64,
    int QueueCapacity = 1024,
    int TurnBudget = 64,
    int TimeoutSeconds = 120)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Sessions, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Sessions, 8192);
        ArgumentOutOfRangeException.ThrowIfLessThan(Maps, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Maps, Math.Min(Sessions, 256));
        ArgumentOutOfRangeException.ThrowIfLessThan(Workers, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Workers, 128);
        ArgumentOutOfRangeException.ThrowIfLessThan(Steps, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((long)Sessions * Steps, 8_000_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(Warmup, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((long)Sessions * Warmup, 8_000_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(QueueCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(QueueCapacity, 65_536);
        ArgumentOutOfRangeException.ThrowIfLessThan(TurnBudget, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(TurnBudget, 65_536);
        ArgumentOutOfRangeException.ThrowIfLessThan(TimeoutSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(TimeoutSeconds, 600);
    }

    public static WorldLoadOptions Parse(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length % 2 != 0)
        {
            throw new ArgumentException("Options require name/value pairs.", nameof(arguments));
        }

        WorldLoadOptions result = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Length; index += 2)
        {
            string name = arguments[index];
            if (!seen.Add(name) || !int.TryParse(arguments[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                throw new ArgumentException("Duplicate option or invalid integer.", nameof(arguments));
            }

            result = name switch
            {
                "--sessions" => result with { Sessions = value },
                "--maps" => result with { Maps = value },
                "--workers" => result with { Workers = value },
                "--steps" => result with { Steps = value },
                "--warmup" => result with { Warmup = value },
                "--queue-capacity" => result with { QueueCapacity = value },
                "--turn-budget" => result with { TurnBudget = value },
                "--timeout-seconds" => result with { TimeoutSeconds = value },
                _ => throw new ArgumentException("Unknown option.", nameof(arguments)),
            };
        }

        result.Validate();
        return result;
    }
}
