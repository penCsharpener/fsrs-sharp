using System.Collections.Immutable;
using FsrsSharp.Kit;

namespace FsrsSharp.Models.Fsrs4;

/// <summary>FSRS-4 constants (ts-fsrs <c>models/fsrs-4/constants.ts</c>).</summary>
public static class Fsrs4Constants
{
    public static ModelBounds ModelBounds { get; } = ModelSupport.Bounds(0.01, 36500.0, 1.0, 10.0);

    public static ImmutableArray<double> DefaultWeights { get; } =
    [
        0.4, 0.9, 2.3, 10.9, 4.93, 0.94, 0.86, 0.01, 1.49, 0.14, 0.94, 2.18, 0.05,
        0.34, 1.26, 0.29, 2.61,
    ];

    /// <summary>Per-weight (min, max) bounds, one entry per weight.</summary>
    public static (double Min, double Max)[] ParameterBounds()
    {
        var b = ModelBounds;
        return
        [
            (b["sMin"], b["sMax"]),
            (b["sMin"], b["sMax"]),
            (b["sMin"], b["sMax"]),
            (b["sMin"], b["sMax"]),
            (1.0, 10.0),
            (0.1, 5.0),
            (0.1, 5.0),
            (0.0, 0.5),
            (0.0, 3.0),
            (0.1, 0.8),
            (0.01, 2.5),
            (0.5, 5.0),
            (0.01, 0.2),
            (0.01, 0.9),
            (0.01, 2.0),
            (0.0, 1.0),
            (1.0, 4.0),
        ];
    }
}
