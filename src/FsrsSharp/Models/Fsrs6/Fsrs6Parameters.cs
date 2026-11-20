using FsrsSharp.Models.Fsrs5;

namespace FsrsSharp.Models.Fsrs6;

/// <summary>The FSRS-6 model config (ts-fsrs <c>FSRS6Config</c>).</summary>
public sealed record Fsrs6Config(IReadOnlyList<double> Weights, bool EnableShortTerm, int NumRelearningSteps);

/// <summary>FSRS-6 parameter migrate/clip/check and config validation (ts-fsrs <c>models/fsrs-6/parameters.ts</c>).</summary>
public static class Fsrs6Parameters
{
    public const string ConfigMessage = "Expected FSRS6 config";

    /// <summary>Returns <c>null</c> when <paramref name="value"/> is a valid decay (0.1 to 0.8), else upstream's error.</summary>
    public static string? ValidateDecay(double value) =>
        value >= 0.1 && value <= 0.8 ? null : "Expected decay between 0.1 and 0.8";

    public static double[] Clip(IReadOnlyList<double> parameters, double numRelearningSteps = 0, bool enableShortTerm = true)
    {
        var clip = Fsrs6Constants.ParameterBounds(Fsrs6Constants.W17W18Ceiling, enableShortTerm)
            .Take(parameters.Count).ToArray();

        if (Math.Max(0, numRelearningSteps) > 1 && parameters.Count >= 19)
        {
            // PLS = w11 * D ^ -w12 * [(S + 1) ^ w13 - 1] * e ^ (w14 * (1 - R))
            // PLS * e ^ (num_relearning_steps * w17 * w18) should be <= S
            // => w17 * w18 <= -[ln(w11) + ln(2 ^ w13 - 1) + w14 * 0.3] / num_relearning_steps
            // Clamp w11/w13/w14 first so log() never receives <= 0 (otherwise NaN/-Infinity)
            var w11 = FsrsMath.Clamp(ModelSupport.OrZero(parameters[11]), clip[11].Min, clip[11].Max);
            var w13 = FsrsMath.Clamp(ModelSupport.OrZero(parameters[13]), clip[13].Min, clip[13].Max);
            var w14 = FsrsMath.Clamp(ModelSupport.OrZero(parameters[14]), clip[14].Min, clip[14].Max);
            var value = -(Math.Log(w11) + Math.Log(Math.Pow(2.0, w13) - 1.0) + w14 * 0.3) / numRelearningSteps;

            // sqrt converts the product constraint (w17 * w18 <= value) into per-parameter
            // ceilings. Math.Max guards against NaN.
            var w17W18Ceiling = FsrsMath.Clamp(
                FsrsMath.RoundTo(Math.Sqrt(Math.Max(value, 0)), 8), 0.01, Fsrs6Constants.W17W18Ceiling);
            clip[17] = (clip[17].Min, w17W18Ceiling);
            clip[18] = (clip[18].Min, w17W18Ceiling);
        }

        var result = new double[clip.Length];
        for (var i = 0; i < clip.Length; i++)
            result[i] = FsrsMath.Clamp(ModelSupport.OrZero(parameters[i]), clip[i].Min, clip[i].Max);
        return result;
    }

    /// <summary>Returns <paramref name="parameters"/> unchanged, or throws when they are not 21 in-bound weights.</summary>
    public static IReadOnlyList<double> Check(IReadOnlyList<double> parameters, double numRelearningSteps = 0, bool enableShortTerm = true)
    {
        var clipped = Clip(parameters, numRelearningSteps, enableShortTerm);
        var isValid = parameters.Count == Fsrs6Constants.DefaultWeights.Length;
        for (var i = 0; isValid && i < clipped.Length; i++)
            isValid = clipped[i] == parameters[i];
        if (!isValid) throw new FsrsValidationException("Expected FSRS6 weights within model bounds.");
        return parameters;
    }

    public static IReadOnlyList<double> Migrate(IReadOnlyList<double>? parameters)
    {
        if (parameters is null || parameters.Count == 0) return [.. Fsrs6Constants.DefaultWeights];
        switch (parameters.Count)
        {
            case 21:
                return [.. parameters];
            case 19:
                return [.. parameters, 0.0, Fsrs5Constants.Decay];
            case 17:
            {
                var w = parameters.ToList();
                w[4] = FsrsMath.ToFixed(w[5] * 2.0 + w[4], 8);
                w[5] = FsrsMath.ToFixed(Math.Log(w[5] * 3.0 + 1.0) / 3.0, 8);
                w[6] = FsrsMath.ToFixed(w[6] + 0.5, 8);
                w.AddRange([0.0, 0.0, 0.0, Fsrs5Constants.Decay]);
                return w;
            }
            default:
                throw new FsrsValidationException($"Invalid parameters length \"{parameters.Count}\", expected 17, 19 or 21.");
        }
    }

    /// <summary>
    /// The upstream config schema: weights must be a finite number array, <c>enableShortTerm</c> a
    /// boolean and <c>numRelearningSteps</c> a non-negative safe integer. Throws
    /// <see cref="FsrsValidationException"/> otherwise.
    /// </summary>
    public static Fsrs6Config ParseConfig(object? weights, object? enableShortTerm, object? numRelearningSteps)
    {
        if (ModelSupport.AsNumberList(weights) is { } w
            && ModelSupport.AsBool(enableShortTerm) is { } e
            && ModelSupport.AsNonNegativeSafeInteger(ModelSupport.AsNumber(numRelearningSteps)) is { } n)
            return new Fsrs6Config(w, e, n);
        throw new FsrsValidationException(ConfigMessage);
    }
}
