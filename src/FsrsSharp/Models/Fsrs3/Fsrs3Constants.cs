using System.Collections.Immutable;
using FsrsSharp.Kit;

namespace FsrsSharp.Models.Fsrs3;

/// <summary>FSRS-3 constants (ts-fsrs <c>models/fsrs-3/constants.ts</c>).</summary>
public static class Fsrs3Constants
{
    public static ModelBounds ModelBounds { get; } = ModelSupport.Bounds(0.01, 36500.0, 1.0, 10.0);

    public static ImmutableArray<double> DefaultWeights { get; } =
    [
        0.9605, 1.7234, 4.8527, -1.1917, -1.2956, 0.0573, 1.7352, -0.1673, 1.065,
        1.8907, -0.3832, 0.5867, 1.0721,
    ];

    /// <summary>
    /// Per-weight (min, max) bounds, one entry per weight.
    /// See https://github.com/open-spaced-repetition/srs-benchmark/blob/main/models/fsrs_v3.py
    /// </summary>
    public static (double Min, double Max)[] ParameterBounds()
    {
        var b = ModelBounds;
        return
        [
            (0.1, 10.0),
            (0.1, 5.0),
            (b["dMin"], b["dMax"]),
            (-5.0, -0.1),
            (-5.0, -0.1),
            (0.05, 0.5),
            (0.0, 2.0),
            (-0.8, -0.15),
            (0.01, 1.5),
            (0.5, 5.0),
            (-2.0, -0.01),
            (0.01, 0.9),
            (0.01, 2.0),
        ];
    }
}
