namespace FsrsSharp;

/// <summary>
/// Constants ported from ts-fsrs's <c>constant.ts</c>, including the FSRS-6
/// default parameter vector <see cref="DefaultW"/>.
/// </summary>
public static class Constants
{
    public const double DefaultRequestRetention = 0.9;
    public const int DefaultMaximumInterval = 36500;
    public const bool DefaultEnableFuzz = false;
    public const bool DefaultEnableShortTerm = true;

    /// <summary>New→Learning, Learning→Learning steps.</summary>
    public static readonly string[] DefaultLearningSteps = { "1m", "10m" };

    /// <summary>Relearning→Relearning steps.</summary>
    public static readonly string[] DefaultRelearningSteps = { "10m" };

    public const string FsrsVersion = "FsrsSharp using FSRS-6.0";

    public const double SMin = 0.001;
    public const double SMax = 36500.0;
    public const double InitSMax = 100.0;

    /// <summary>The fixed decay used by FSRS-4.5 and FSRS-5, before it became trainable (w20) in FSRS-6.</summary>
    public const double Fsrs5DefaultDecay = 0.5;
    public const double Fsrs6DefaultDecay = 0.1542;

    /// <summary>The 21 FSRS-6 default parameters (trained by Open Spaced Repetition on their benchmark dataset).</summary>
    public static readonly double[] DefaultW =
    {
        0.212, 1.2931, 2.3065, 8.2956, 6.4133, 0.8334, 3.0194, 0.001, 1.8722,
        0.1666, 0.796, 1.4835, 0.0614, 0.2629, 1.6483, 0.6014, 1.8729, 0.5425,
        0.0912, 0.0658, Fsrs6DefaultDecay,
    };

    public const double W17W18Ceiling = 2.0;

    /// <summary>
    /// The valid [min, max] range for each of the (up to) 21 parameters, used to clamp
    /// optimized/imported parameter sets into a numerically safe range.
    /// </summary>
    public static (double Min, double Max)[] ClampParameters(double w17W18Ceiling, bool enableShortTerm = DefaultEnableShortTerm) => new[]
    {
        (SMin, InitSMax),          // 0: initial stability (Again)
        (SMin, InitSMax),          // 1: initial stability (Hard)
        (SMin, InitSMax),          // 2: initial stability (Good)
        (SMin, InitSMax),          // 3: initial stability (Easy)
        (1.0, 10.0),               // 4: initial difficulty (Good)
        (0.001, 4.0),              // 5: initial difficulty (multiplier)
        (0.001, 4.0),              // 6: difficulty (multiplier)
        (0.001, 0.75),             // 7: difficulty (multiplier)
        (0.0, 4.5),                // 8: stability (exponent)
        (0.0, 0.8),                // 9: stability (negative power)
        (0.001, 3.5),              // 10: stability (exponent)
        (0.001, 5.0),              // 11: fail stability (multiplier)
        (0.001, 0.25),             // 12: fail stability (negative power)
        (0.001, 0.9),              // 13: fail stability (power)
        (0.0, 4.0),                // 14: fail stability (exponent)
        (0.0, 1.0),                // 15: stability (multiplier for Hard)
        (1.0, 6.0),                // 16: stability (multiplier for Easy)
        (0.0, w17W18Ceiling),      // 17: short-term stability (exponent)
        (0.0, w17W18Ceiling),      // 18: short-term stability (exponent)
        (enableShortTerm ? 0.01 : 0.0, 0.8), // 19: short-term last-stability (exponent)
        (0.1, 0.8),                // 20: decay
    };
}
