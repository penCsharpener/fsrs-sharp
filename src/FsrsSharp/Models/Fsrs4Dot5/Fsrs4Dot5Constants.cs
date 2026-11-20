using System.Collections.Immutable;
using FsrsSharp.Kit;

namespace FsrsSharp.Models.Fsrs4Dot5;

/// <summary>FSRS-4.5 constants (ts-fsrs <c>models/fsrs-4dot5/constants.ts</c>).</summary>
public static class Fsrs4Dot5Constants
{
    public const double Decay = 0.5;
    public const double Factor = 19.0 / 81.0;
    public const double InitSMax = 100.0;

    public static ModelBounds ModelBounds { get; } = ModelSupport.Bounds(0.01, 36500.0, 1.0, 10.0);

    public static ImmutableArray<double> DefaultWeights { get; } =
    [
        0.4872, 1.4003, 3.7145, 13.8206, 5.1618, 1.2298, 0.8975, 0.031, 1.6474,
        0.1367, 1.0461, 2.1072, 0.0793, 0.3246, 1.587, 0.2272, 2.8755,
    ];

    /// <summary>
    /// Per-weight (min, max) bounds, one entry per weight.
    /// See https://github.com/open-spaced-repetition/srs-benchmark/blob/main/models/fsrs_v4dot5.py
    /// </summary>
    public static (double Min, double Max)[] ParameterBounds()
    {
        var b = ModelBounds;
        return
        [
            (b["sMin"], InitSMax),
            (b["sMin"], InitSMax),
            (b["sMin"], InitSMax),
            (b["sMin"], InitSMax),
            (0.0, b["dMax"]),
            (0.01, 5.0),
            (0.01, 5.0),
            (0.0, 0.8),
            (0.0, 6.0),
            (0.0, 0.8),
            (0.01, 5.0),
            (0.2, 6.0),
            (0.01, 0.4),
            (0.01, 0.9),
            (0.01, 4.0),
            (0.0, 1.0),
            (1.0, 10.0),
        ];
    }
}
