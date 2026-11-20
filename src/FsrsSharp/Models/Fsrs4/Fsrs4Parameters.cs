namespace FsrsSharp.Models.Fsrs4;

/// <summary>The FSRS-4 model config (ts-fsrs <c>FSRS4Config</c>).</summary>
public sealed record Fsrs4Config(IReadOnlyList<double> Weights);

/// <summary>FSRS-4 parameter migrate/clip/check and config validation (ts-fsrs <c>models/fsrs-4/parameters.ts</c>).</summary>
public static class Fsrs4Parameters
{
    public const string ConfigMessage = "Expected FSRS4 config";

    public static double[] Clip(IReadOnlyList<double> parameters)
    {
        var clip = Fsrs4Constants.ParameterBounds().Take(parameters.Count).ToArray();
        var result = new double[clip.Length];
        for (var i = 0; i < clip.Length; i++)
            result[i] = FsrsMath.Clamp(parameters[i], clip[i].Min, clip[i].Max);
        return result;
    }

    /// <summary>Returns <paramref name="parameters"/> unchanged, or throws when they are not 17 in-bound weights.</summary>
    public static IReadOnlyList<double> Check(IReadOnlyList<double> parameters)
    {
        var clipped = Clip(parameters);
        var isValid = parameters.Count == Fsrs4Constants.DefaultWeights.Length;
        for (var i = 0; isValid && i < clipped.Length; i++)
            isValid = clipped[i] == parameters[i];
        if (!isValid) throw new FsrsValidationException("Expected FSRS4 weights within model bounds.");
        return parameters;
    }

    public static IReadOnlyList<double> Migrate(IReadOnlyList<double>? parameters)
    {
        if (parameters is null || parameters.Count == 0) return [.. Fsrs4Constants.DefaultWeights];
        return parameters.Take(Fsrs4Constants.DefaultWeights.Length).ToArray();
    }

    /// <summary>The upstream config schema: weights must be a finite number array.</summary>
    public static Fsrs4Config ParseConfig(object? weights) =>
        ModelSupport.AsNumberList(weights) is { } w ? new Fsrs4Config(w) : throw new FsrsValidationException(ConfigMessage);
}
