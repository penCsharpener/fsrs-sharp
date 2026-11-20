using System.Globalization;
using FsrsSharp.Kit;

namespace FsrsSharp.Middlewares;

/// <summary>One fuzz band: intervals above <see cref="Start"/> (up to <see cref="End"/>) widen the range by <see cref="Factor"/> per day.</summary>
public sealed record FuzzRange(double Start, double End, double Factor);

/// <summary>The inclusive interval range an interval may be fuzzed into.</summary>
public readonly record struct FuzzInterval(double MinInterval, double MaxInterval);

/// <summary>Creates a seeded random sequence (<c>() =&gt; [0, 1)</c>) from a string seed.</summary>
public delegate Func<double> FuzzingRng(string seed);

/// <summary>Fuzzing parameters used by <see cref="Fuzzing.WithFuzzing(double, double, FuzzingConfigInput, string?, TimeProvider?)"/> (upstream <c>FuzzingConfigInput</c>).</summary>
public sealed record FuzzingConfigInput(bool EnableFuzz, double MaximumInterval, bool FractionalDays = false);

/// <summary>Deterministic interval fuzzing (ts-fsrs <c>middlewares/fuzzing/core.ts</c>).</summary>
public static class Fuzzing
{
    /// <summary>The default fuzz bands.</summary>
    public static IReadOnlyList<FuzzRange> FuzzRanges { get; } =
    [
        new(2.5, 7, 0.15),
        new(7, 20, 0.1),
        new(20, double.PositiveInfinity, 0.05),
    ];

    /// <summary>The default rng: FNV-1a 32-bit hash of the seed feeding Mulberry32.</summary>
    public static FuzzingRng Fnv1aMulberry32Rng { get; } = seed => Mulberry32(Fnv1a32(seed));

    public static FuzzInterval GetFuzzRange(
        double interval,
        double elapsedDays,
        double maximumInterval,
        IReadOnlyList<FuzzRange>? fuzzRanges = null)
    {
        fuzzRanges ??= FuzzRanges;
        var delta = 1.0;
        foreach (var range in fuzzRanges)
            delta += range.Factor * Math.Max(Math.Min(interval, range.End) - range.Start, 0);

        var cappedInterval = Math.Min(interval, maximumInterval);
        var minInterval = Math.Max(2, Helpers.JsRound(cappedInterval - delta));
        var maxInterval = Math.Min(Helpers.JsRound(cappedInterval + delta), maximumInterval);
        if (cappedInterval > elapsedDays) minInterval = Math.Max(minInterval, elapsedDays + 1);
        minInterval = Math.Min(minInterval, maxInterval);
        return new FuzzInterval(minInterval, maxInterval);
    }

    /// <summary>Applies deterministic fuzzing, preserving unfuzzed fractional intervals when configured. A null seed uses the current time in milliseconds.</summary>
    public static double WithFuzzing(
        double interval,
        double elapsedDays,
        FuzzingConfigInput config,
        string? seed = null,
        TimeProvider? timeProvider = null)
    {
        if (!config.EnableFuzz || interval < 2.5)
            return config.FractionalDays ? interval : Helpers.JsRound(interval);

        var (minInterval, maxInterval) = GetFuzzRange(interval, elapsedDays, config.MaximumInterval);
        seed ??= (timeProvider ?? TimeProvider.System).GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var fuzzFactor = Fnv1aMulberry32Rng(seed)();
        return Math.Floor(fuzzFactor * (maxInterval - minInterval + 1) + minInterval);
    }

    /// <summary><see cref="WithFuzzing(double, double, FuzzingConfigInput, string?, TimeProvider?)"/> reading a parsed scheduler config (<c>enableFuzz</c>, <c>maximumInterval</c>, <c>fractionalDays</c>).</summary>
    public static double WithFuzzing(double interval, double elapsedDays, FieldBag config, string? seed = null, TimeProvider? timeProvider = null) =>
        WithFuzzing(
            interval,
            elapsedDays,
            new FuzzingConfigInput(
                config.Get(FuzzingMiddleware.EnableFuzz),
                config.Get(FuzzingMiddleware.MaximumInterval),
                config.Get(KitSchemas.FractionalDays)),
            seed,
            timeProvider);

    private static uint Fnv1a32(string str)
    {
        var hash = 0x811c9dc5u;
        foreach (var ch in str)
        {
            hash ^= ch;
            hash = unchecked(hash * 0x01000193u);
        }
        return hash;
    }

    private static Func<double> Mulberry32(uint seed)
    {
        var a = seed;
        return () =>
        {
            unchecked
            {
                a += 0x6d2b79f5u;
                var t = a;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        };
    }
}
