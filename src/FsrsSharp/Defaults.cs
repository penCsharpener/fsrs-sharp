namespace FsrsSharp;

/// <summary>
/// Parameter migration/validation and card creation helpers, ported from
/// ts-fsrs's <c>default.ts</c>.
/// </summary>
public static class Defaults
{
    /// <summary>
    /// Clamps every parameter into its valid range (see <see cref="Constants.ClampParameters"/>),
    /// additionally tightening w17/w18's ceiling when there is more than one relearning step so
    /// that repeated lapses can never make short-term stability increase (see the ts-fsrs comment
    /// this reproduces for the derivation).
    /// </summary>
    public static double[] ClipParameters(IReadOnlyList<double> parameters, int numRelearningSteps, bool enableShortTerm = Constants.DefaultEnableShortTerm)
    {
        var clip = Constants.ClampParameters(Constants.W17W18Ceiling, enableShortTerm);
        var len = Math.Min(clip.Length, parameters.Count);
        clip = clip[..len];

        if (Math.Max(0, numRelearningSteps) > 1)
        {
            var w11 = Helpers.Clamp(At(parameters, 11), clip[11].Min, clip[11].Max);
            var w13 = Helpers.Clamp(At(parameters, 13), clip[13].Min, clip[13].Max);
            var w14 = Helpers.Clamp(At(parameters, 14), clip[14].Min, clip[14].Max);
            var value = -(Math.Log(w11) + Math.Log(Math.Pow(2.0, w13) - 1.0) + w14 * 0.3) / numRelearningSteps;

            var w17W18Ceiling = Helpers.Clamp(Helpers.RoundTo(Math.Sqrt(Math.Max(value, 0)), 8), 0.01, Constants.W17W18Ceiling);
            if (clip.Length > 17) clip[17] = (clip[17].Min, w17W18Ceiling);
            if (clip.Length > 18) clip[18] = (clip[18].Min, w17W18Ceiling);
        }

        var result = new double[len];
        for (var i = 0; i < len; i++)
        {
            result[i] = Helpers.Clamp(At(parameters, i), clip[i].Min, clip[i].Max);
        }

        return result;

        // ts-fsrs reads each weight as `parameters[index] || 0`, which quietly coerces both a missing
        // index *and* NaN to 0 before clamping. Without the NaN half, one bad value in an imported weight
        // set survives the clamp and poisons every interval computed from it.
        static double At(IReadOnlyList<double> p, int i)
        {
            if (i >= p.Count) return 0;
            return double.IsNaN(p[i]) ? 0 : p[i];
        }
    }

    /// <summary>Throws if any parameter is non-finite, or the vector isn't a recognized FSRS-4/5/6 length (17/19/21).</summary>
    public static double[] CheckParameters(IReadOnlyList<double> parameters)
    {
        foreach (var p in parameters)
        {
            if (!double.IsFinite(p))
            {
                throw new FsrsValidationException($"Non-finite or NaN value in parameters {string.Join(", ", parameters)}");
            }
        }

        if (parameters.Count is not (17 or 19 or 21))
        {
            throw new FsrsValidationException(
                $"Invalid parameter length: {parameters.Count}. Must be 17, 19 or 21 for FSRSv4, 5 and 6 respectively.");
        }

        return parameters as double[] ?? parameters.ToArray();
    }

    /// <summary>
    /// Upgrades a 17- or 19-length parameter vector (FSRS-4.5 / FSRS-5) to the current
    /// 21-length FSRS-6 shape, clamping along the way. A null/omitted vector returns the
    /// FSRS-6 defaults.
    /// </summary>
    public static double[] MigrateParameters(IReadOnlyList<double>? parameters, int numRelearningSteps = 0, bool enableShortTerm = Constants.DefaultEnableShortTerm)
    {
        if (parameters is null)
        {
            return (double[])Constants.DefaultW.Clone();
        }

        switch (parameters.Count)
        {
            case 21:
                return ClipParameters(parameters, numRelearningSteps, enableShortTerm);
            case 19:
                return ClipParameters(parameters, numRelearningSteps, enableShortTerm)
                    .Concat(new[] { 0.0, Constants.Fsrs5DefaultDecay })
                    .ToArray();
            case 17:
            {
                var w = ClipParameters(parameters, numRelearningSteps, enableShortTerm);
                w[4] = Math.Round((w[5] * 2.0 + w[4]) * 1e8, MidpointRounding.AwayFromZero) / 1e8;
                w[5] = Math.Round(Math.Log(w[5] * 3.0 + 1.0) / 3.0 * 1e8, MidpointRounding.AwayFromZero) / 1e8;
                w[6] = Math.Round((w[6] + 0.5) * 1e8, MidpointRounding.AwayFromZero) / 1e8;
                return w.Concat(new[] { 0.0, 0.0, 0.0, Constants.Fsrs5DefaultDecay }).ToArray();
            }
            default:
                // To throw, use CheckParameters explicitly first.
                return (double[])Constants.DefaultW.Clone();
        }
    }

    /// <summary>
    /// Builds a complete, validated <see cref="FsrsParameters"/> from a partial override,
    /// filling in FSRS-6 defaults and migrating/clamping <see cref="FsrsParameters.W"/>.
    /// This is the recommended way to construct parameters (rather than <c>new FsrsParameters()</c>
    /// directly), matching ts-fsrs's <c>generatorParameters()</c>.
    /// </summary>
    public static FsrsParameters GeneratorParameters(FsrsParametersInput? overrides = null)
    {
        overrides ??= new FsrsParametersInput();

        var learningSteps = overrides.LearningSteps ?? (string[])Constants.DefaultLearningSteps.Clone();
        var relearningSteps = overrides.RelearningSteps ?? (string[])Constants.DefaultRelearningSteps.Clone();
        var enableShortTerm = overrides.EnableShortTerm ?? Constants.DefaultEnableShortTerm;
        var w = MigrateParameters(overrides.W, relearningSteps.Length, enableShortTerm);

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
        ElapsedDays = 0,
        ScheduledDays = 0,
        Reps = 0,
        Lapses = 0,
        LearningSteps = 0,
        State = State.New,
        LastReview = null,
    };
}

/// <summary>
/// A partial, all-nullable set of FSRS parameter overrides — "unset" (null) means
/// "use the FSRS-6 default", mirroring ts-fsrs's <c>Partial&lt;FSRSParameters&gt;</c>.
/// Pass this to <see cref="Defaults.GeneratorParameters"/> or <see cref="Fsrs(FsrsParametersInput?)"/>.
/// </summary>
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
