using System.Collections.Immutable;
using FsrsSharp.Kit;

namespace FsrsSharp.Models.Fsrs6;

/// <summary>FSRS-6 constants (ts-fsrs <c>models/fsrs-6/constants.ts</c>).</summary>
public static class Fsrs6Constants
{
    public const double Decay = 0.1542;
    public const double W17W18Ceiling = 2.0;
    public const double InitSMax = 100.0;

    public static ModelBounds ModelBounds { get; } = ModelSupport.Bounds(0.001, 36500.0, 1.0, 10.0);

    public static ImmutableArray<double> DefaultWeights { get; } =
    [
        0.212, 1.2931, 2.3065, 8.2956, 6.4133, 0.8334, 3.0194, 0.001, 1.8722, 0.1666, 0.796,
        1.4835, 0.0614, 0.2629, 1.6483, 0.6014, 1.8729, 0.5425, 0.0912, 0.0658, Decay,
    ];

    /// <summary>Per-weight (min, max) bounds, one entry per weight.</summary>
    public static (double Min, double Max)[] ParameterBounds(double w17W18Ceiling, bool enableShortTerm = true)
    {
        var b = ModelBounds;
        return
        [
            (b["sMin"], InitSMax),
            (b["sMin"], InitSMax),
            (b["sMin"], InitSMax),
            (b["sMin"], InitSMax),
            (b["dMin"], b["dMax"]),
            (0.001, 4.0),
            (0.001, 4.0),
            (0.001, 0.75),
            (0.0, 4.5),
            (0.0, 0.8),
            (0.001, 3.5),
            (0.001, 5.0),
            (0.001, 0.25),
            (0.001, 0.9),
            (0.0, 4.0),
            (0.0, 1.0),
            (1.0, 6.0),
            (0.0, w17W18Ceiling),
            (0.0, w17W18Ceiling),
            (enableShortTerm ? 0.01 : 0.0, 0.8),
            (0.1, 0.8),
        ];
    }
}
