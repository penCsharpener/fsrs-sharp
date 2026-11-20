namespace FsrsSharp.Models.Fsrs3;

/// <summary>The FSRS-3 model config (ts-fsrs <c>FSRS3Config</c>).</summary>
public sealed record Fsrs3Config(IReadOnlyList<double> Weights);

/// <summary>FSRS-3 parameter migrate/clip/check and config validation (ts-fsrs <c>models/fsrs-3/parameters.ts</c>).</summary>
public static class Fsrs3Parameters
{
    public const string ConfigMessage = "Expected FSRS3 config";

    public static double[] Clip(IReadOnlyList<double> parameters)
    {
        var clip = Fsrs3Constants.ParameterBounds().Take(parameters.Count).ToArray();
        var result = new double[clip.Length];
        for (var i = 0; i < clip.Length; i++)
            result[i] = FsrsMath.Clamp(parameters[i], clip[i].Min, clip[i].Max);
        return result;
    }

    /// <summary>Returns <paramref name="parameters"/> unchanged, or throws when they are not 13 in-bound weights.</summary>
    public static IReadOnlyList<double> Check(IReadOnlyList<double> parameters)
    {
        var clipped = Clip(parameters);
        var isValid = parameters.Count == Fsrs3Constants.DefaultWeights.Length;
        for (var i = 0; isValid && i < clipped.Length; i++)
            isValid = clipped[i] == parameters[i];
        if (!isValid) throw new FsrsValidationException("Expected FSRS3 weights within model bounds.");
        return parameters;
    }

    public static IReadOnlyList<double> Migrate(IReadOnlyList<double>? parameters)
    {
        if (parameters is null || parameters.Count == 0) return [.. Fsrs3Constants.DefaultWeights];
        return parameters.Take(Fsrs3Constants.DefaultWeights.Length).ToArray();
    }

    /// <summary>The upstream config schema: weights must be a finite number array.</summary>
    public static Fsrs3Config ParseConfig(object? weights) =>
        ModelSupport.AsNumberList(weights) is { } w ? new Fsrs3Config(w) : throw new FsrsValidationException(ConfigMessage);
}
