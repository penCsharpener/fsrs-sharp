using FsrsSharp.Middlewares;
using FsrsSharp.Models.Fsrs6;

namespace FsrsSharp.Legacy;

/// <summary>
/// Ported from ts-fsrs's <c>legacy/default.ts</c>. Weight clipping and migration moved to
/// <see cref="Fsrs6Parameters"/> in v6.0.0.
/// </summary>
public static class Defaults
{
    /// <summary>Throws if any parameter is non-finite, or the vector isn't a recognized FSRS-4/5/6 length (17/19/21).</summary>
    public static double[] CheckParameters(IReadOnlyList<double> parameters)
    {
        foreach (var p in parameters)
        {
            if (!double.IsFinite(p))
            {
                throw new FsrsValidationException($"Non-finite or NaN value in parameters {string.Join(",", parameters.Select(JsNumber))}");
            }
        }

        if (parameters.Count is not (17 or 19 or 21))
        {
            throw new FsrsValidationException(
                $"Invalid parameter length: {parameters.Count}. Must be 17, 19 or 21 for FSRSv4, 5 and 6 respectively.");
        }

        return parameters as double[] ?? parameters.ToArray();
    }

    private static string JsNumber(double d) =>
        double.IsNaN(d) ? "NaN"
        : double.IsPositiveInfinity(d) ? "Infinity"
        : double.IsNegativeInfinity(d) ? "-Infinity"
        : d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds a complete <see cref="FsrsParameters"/> from a partial override, filling in defaults and
    /// migrating <see cref="FsrsParameters.W"/> to the 21-weight FSRS-6 layout (without clipping; the
    /// <see cref="Fsrs"/> constructor clips). Matches ts-fsrs's <c>generatorParameters()</c>.
    /// </summary>
    public static FsrsParameters GeneratorParameters(FsrsParametersInput? overrides = null)
    {
        overrides ??= new FsrsParametersInput();

        var learningSteps = overrides.LearningSteps ?? LearningStepsMiddleware.DefaultLearningSteps.ToArray();
        var relearningSteps = overrides.RelearningSteps ?? LearningStepsMiddleware.DefaultRelearningSteps.ToArray();
        var enableShortTerm = overrides.EnableShortTerm ?? Constants.DefaultEnableShortTerm;
        var w = Fsrs6Parameters.Migrate(overrides.W).ToArray();

        return new FsrsParameters
        {
            RequestRetention = overrides.RequestRetention is > 0 ? overrides.RequestRetention.Value : Constants.DefaultRequestRetention,
            MaximumInterval = overrides.MaximumInterval is > 0 ? overrides.MaximumInterval.Value : Constants.DefaultMaximumInterval,
            W = w,
            EnableFuzz = overrides.EnableFuzz ?? Constants.DefaultEnableFuzz,
            EnableShortTerm = enableShortTerm,
            LearningSteps = learningSteps,
            RelearningSteps = relearningSteps,
        };
    }

    /// <summary>Creates a brand-new <see cref="State.New"/> card due immediately (or at <paramref name="now"/>).</summary>
    public static Card CreateEmptyCard(DateTime? now = null) => new()
    {
        Due = now ?? DateTime.UtcNow,
        Stability = 0,
        Difficulty = 0,
        ScheduledDays = 0,
        Reps = 0,
        Lapses = 0,
        LearningSteps = 0,
        State = State.New,
        LastReview = null,
    };
}

/// <summary>Partial overrides for <see cref="Defaults.GeneratorParameters"/> and the <see cref="Fsrs"/> constructor.</summary>
public sealed class FsrsParametersInput
{
    public double? RequestRetention { get; set; }
    public int? MaximumInterval { get; set; }
    public double[]? W { get; set; }
    public bool? EnableFuzz { get; set; }
    public bool? EnableShortTerm { get; set; }
    public string[]? LearningSteps { get; set; }
    public string[]? RelearningSteps { get; set; }
}
