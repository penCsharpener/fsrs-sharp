using System.Collections.ObjectModel;
using FsrsSharp.Kit;

namespace FsrsSharp.Models.Fsrs7;

/// <summary>FSRS-7 constants (ts-fsrs <c>models/fsrs-7/constants.ts</c>).</summary>
public static class Fsrs7Constants
{
    public const double InitSMax = 100.0;

    /// <summary>Smallest elapsed time the interval solver considers: one second, in days.</summary>
    public const double MinT = 1.0 / 86400;

    public static readonly double LogMinT = Math.Log(MinT);
    public const double DrMin = 0.0001;
    public const double DrMax = 0.9999;

    /// <summary>
    /// Memory-state bounds with the upstream key names: <c>sMin</c>, <c>sMax</c>,
    /// <c>stabilityFastMin</c>, <c>stabilityFastMax</c>, <c>dMin</c>, <c>dMax</c>.
    /// </summary>
    public static ModelBounds ModelBounds { get; } = new(new Dictionary<string, double>
    {
        ["sMin"] = 0.0001,
        ["sMax"] = 36500,
        ["stabilityFastMin"] = 0.0001,
        ["stabilityFastMax"] = 36500,
        ["dMin"] = 1,
        ["dMax"] = 10,
    });

    /// <summary>
    /// Fresh box bounds in weight-index order (fsrs-rs parameter_clipper_v7.rs); the clipper also
    /// enforces cross-parameter constraints. A new array is returned on every call.
    /// </summary>
    public static (double Min, double Max)[] ParameterBounds()
    {
        const double sMin = 0.0001, dMin = 1, dMax = 10;
        return
        [
            (sMin, InitSMax / 2),
            (sMin, InitSMax),
            (sMin, InitSMax),
            (sMin, InitSMax),
            (dMin, dMax),
            (0.001, 4),
            (0.1, 4),
            (0, 4),
            (0, 1.2),
            (0.3, 3),
            (0.01, 1.5),
            (0.1, 1),
            (0, 3.5),
            (0, 1),
            (1, 7),
            (0, 4),
            (0, 2),
            (0.5, 6),
            (0.001, 1.5),
            (0.001, 1),
            (0, 5),
            (0, 1),
            (1, 7),
            (0.01, 0.25),
            (0.01, 0.95),
            (0.2, 0.85),
            // base2 floor follows srs-benchmark (_CLIP_LO[26] = 0.5); fsrs-rs currently folds
            // the box clamp into the monotonicity check and loses this floor.
            (0.5, 0.99),
            (0.01, 1),
            (0.1, 1),
            (0, 0.9),
            (0.1, 1.1),
            (0, 1),
            (0, 0.6),
            (0, 0.6),
        ];
    }

    private static readonly double[] DefaultWeightValues =
    [
        0.1104, 2.2395, 3.9221, 11.7841, 6.1686, 0.6457, 3.6807, 1.9795, 0, 1.3826,
        0.7024, 0.5999, 0.8146, 0.6398, 1, 1.3207, 0.6707, 3.8668, 0.4416, 0.0934,
        1.8631, 0.6162, 1.0869, 0.1567, 0.0801, 0.2421, 0.9464, 0.1433, 0.7145, 0,
        0.5667, 0.3734, 0.5333, 0.3048,
    ];

    /// <summary>fsrs-rs c9562d6 inference_v7.rs (also srs-benchmark FSRS7.init_w). Read-only.</summary>
    public static IReadOnlyList<double> DefaultWeights { get; } = new ReadOnlyCollection<double>(DefaultWeightValues);
}
