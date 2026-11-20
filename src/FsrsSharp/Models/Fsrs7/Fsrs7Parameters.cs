namespace FsrsSharp.Models.Fsrs7;

/// <summary>FSRS-7 weight migration, clipping and checking (ts-fsrs <c>models/fsrs-7/parameters.ts</c>).</summary>
public static class Fsrs7Parameters
{
    private static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);

    /// <summary>FSRS-7's fast trace is intrinsic; clipping is independent of learning steps.</summary>
    public static double[] Clip(IReadOnlyList<double> parameters)
    {
        var clipped = parameters.ToArray();
        if (clipped.Length < Fsrs7Constants.DefaultWeights.Count) return clipped;
        var bounds = Fsrs7Constants.ParameterBounds();
        for (var index = 0; index < bounds.Length; index++)
        {
            var (min, max) = bounds[index];
            // Box clamp first, then the cross-parameter monotonicity, matching srs-benchmark.
            var low = index > 0 && index < 4
                ? Math.Max(min, clipped[index - 1])
                : index == 26
                    ? Math.Max(min, clipped[25])
                    : min;
            clipped[index] = Clamp(double.IsFinite(clipped[index]) ? clipped[index] : low, low, max);
        }
        return clipped;
    }

    /// <summary>Returns <paramref name="parameters"/> unchanged, or throws when they are not within the model bounds.</summary>
    public static IReadOnlyList<double> Check(IReadOnlyList<double> parameters)
    {
        if (parameters.Count != Fsrs7Constants.DefaultWeights.Count || !ClipEquals(parameters))
            throw new FsrsValidationException("Expected FSRS7 weights within model bounds.");
        return parameters;
    }

    private static bool ClipEquals(IReadOnlyList<double> parameters)
    {
        var clipped = Clip(parameters);
        for (var i = 0; i < clipped.Length; i++)
        {
            if (clipped[i] != parameters[i]) return false;
        }
        return true;
    }

    /// <summary>Previous FSRS parameter layouts cannot be converted into the dual-trace model.</summary>
    public static double[] Migrate(IReadOnlyList<double>? parameters = null)
    {
        if (parameters is null || parameters.Count == 0) return Fsrs7Constants.DefaultWeights.ToArray();
        if (parameters.Count != Fsrs7Constants.DefaultWeights.Count)
        {
            throw new FsrsValidationException(
                $"Invalid parameters length \"{parameters.Count}\", expected 34. FSRS-7 requires its own weights; replay review history to migrate memory states.");
        }
        return parameters.ToArray();
    }
}
