using System.Collections.Immutable;
using FsrsSharp.Kit;

namespace FsrsSharp.Models.Fsrs5;

/// <summary>FSRS-5 constants (ts-fsrs <c>models/fsrs-5/constants.ts</c>).</summary>
public static class Fsrs5Constants
{
    public const double Decay = 0.5;
    public const double Factor = 19.0 / 81.0;
    public const double W17W18Ceiling = 2.0;
    public const double InitSMax = 100.0;

    public static ModelBounds ModelBounds { get; } = ModelSupport.Bounds(0.01, 36500.0, 1.0, 10.0);

    public static ImmutableArray<double> DefaultWeights { get; } =
    [
        0.40255, 1.18385, 3.173, 15.69105, 7.1949, 0.5345, 1.4604, 0.0046, 1.54575,
        0.1192, 1.01925, 1.9395, 0.11, 0.29605, 2.2698, 0.2315, 2.9898, 0.51655,
        0.6621,
    ];

    /// <summary>Per-weight (min, max) bounds, one entry per weight.</summary>
    public static (double Min, double Max)[] ParameterBounds()
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
            (0.0, W17W18Ceiling),
            (0.0, W17W18Ceiling),
        ];
    }
}
