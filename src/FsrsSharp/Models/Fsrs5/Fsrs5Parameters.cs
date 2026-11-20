namespace FsrsSharp.Models.Fsrs5;

/// <summary>The FSRS-5 model config (ts-fsrs <c>FSRS5Config</c>).</summary>
public sealed record Fsrs5Config(IReadOnlyList<double> Weights, bool EnableShortTerm);

/// <summary>FSRS-5 parameter migrate/clip/check and config validation (ts-fsrs <c>models/fsrs-5/parameters.ts</c>).</summary>
public static class Fsrs5Parameters
{
    public const string ConfigMessage = "Expected FSRS5 config";

    public static double[] Clip(IReadOnlyList<double> parameters)
    {
        var clip = Fsrs5Constants.ParameterBounds().Take(parameters.Count).ToArray();
        var result = new double[clip.Length];
        for (var i = 0; i < clip.Length; i++)
            result[i] = FsrsMath.Clamp(ModelSupport.OrZero(parameters[i]), clip[i].Min, clip[i].Max);
        return result;
    }

    /// <summary>Returns <paramref name="parameters"/> unchanged, or throws when they are not 19 in-bound weights.</summary>
    public static IReadOnlyList<double> Check(IReadOnlyList<double> parameters)
    {
        var clipped = Clip(parameters);
        var isValid = parameters.Count == Fsrs5Constants.DefaultWeights.Length;
        for (var i = 0; isValid && i < clipped.Length; i++)
            isValid = clipped[i] == parameters[i];
        if (!isValid) throw new FsrsValidationException("Expected FSRS5 weights within model bounds.");
        return parameters;
    }

    public static IReadOnlyList<double> Migrate(IReadOnlyList<double>? parameters)
    {
        if (parameters is null || parameters.Count == 0) return [.. Fsrs5Constants.DefaultWeights];
        if (parameters.Count > 19) return parameters.Take(19).ToArray();
        switch (parameters.Count)
        {
            case 19:
                return [.. parameters];
            case 17:
            {
                var w = parameters.ToList();
                w[4] = FsrsMath.RoundTo(w[5] * 2.0 + w[4], 8);
                w[5] = FsrsMath.RoundTo(Math.Log(w[5] * 3.0 + 1.0) / 3.0, 8);
                w[6] = FsrsMath.RoundTo(w[6] + 0.5, 8);
                w.AddRange([0.0, 0.0]);
                return w;
            }
            default:
                throw new FsrsValidationException($"Invalid parameters length \"{parameters.Count}\", expected 17 or 19.");
        }
    }

    /// <summary>The upstream config schema: weights must be a finite number array and <c>enableShortTerm</c> a boolean.</summary>
    public static Fsrs5Config ParseConfig(object? weights, object? enableShortTerm)
    {
        if (ModelSupport.AsNumberList(weights) is { } w && ModelSupport.AsBool(enableShortTerm) is { } e)
            return new Fsrs5Config(w, e);
        throw new FsrsValidationException(ConfigMessage);
    }
}
