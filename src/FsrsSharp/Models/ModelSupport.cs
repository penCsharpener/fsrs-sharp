using System.Globalization;
using FsrsSharp.Kit;

namespace FsrsSharp.Models;

/// <summary>Shared plumbing of the FSRS-3 … FSRS-6 models (upstream repeats it in each <c>model.ts</c>).</summary>
internal static class ModelSupport
{
    public const double MaxSafeInteger = 9007199254740991d;

    /// <summary>Formats a number like a JavaScript template literal does.</summary>
    public static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    public static ModelBounds Bounds(double sMin, double sMax, double dMin, double dMax) =>
        new(new Dictionary<string, double> { ["sMin"] = sMin, ["sMax"] = sMax, ["dMin"] = dMin, ["dMax"] = dMax });

    public static void CheckDeltaT(double t)
    {
        if (t < 0) throw new FsrsValidationException($"Invalid delta_t \"{Num(t)}\"");
    }

    public static void CheckGrade(Rating g)
    {
        if ((int)g < 0 || (int)g > 4) throw new FsrsValidationException($"Invalid grade \"{(int)g}\"");
    }

    public static void CheckDesiredRetention(double desiredRetention, string message)
    {
        if (!double.IsFinite(desiredRetention) || desiredRetention <= 0 || desiredRetention > 1)
            throw new FsrsValidationException(message);
    }

    public static string CountText(IReadOnlyList<double>? weights) => weights is null ? "undefined" : weights.Count.ToString(CultureInfo.InvariantCulture);

    /// <summary>JavaScript <c>x || 0</c> for numbers.</summary>
    public static double OrZero(double value) => double.IsNaN(value) ? 0 : value;

    // ---- raw config value readers (upstream schema checks: isNumberArray, typeof boolean, isSafeInteger) ----

    /// <summary>Returns the value as a list when it is a sequence of finite numbers, else <c>null</c> (upstream <c>isNumberArray</c>).</summary>
    public static IReadOnlyList<double>? AsNumberList(object? value)
    {
        if (value is not IEnumerable<double> numbers) return null;
        var list = value as IReadOnlyList<double> ?? numbers.ToList();
        foreach (var n in list)
            if (!double.IsFinite(n)) return null;
        return list;
    }

    public static bool? AsBool(object? value) => value is bool b ? b : null;

    public static double? AsNumber(object? value) => value switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        short s => s,
        byte b => b,
        decimal m => (double)m,
        _ => null,
    };

    /// <summary>A non-negative safe integer (upstream <c>Number.isSafeInteger(v) &amp;&amp; v >= 0</c>), clamped to <see cref="int.MaxValue"/>.</summary>
    public static int? AsNonNegativeSafeInteger(double? value)
    {
        if (value is not { } v) return null;
        if (!double.IsFinite(v) || Math.Floor(v) != v || Math.Abs(v) > MaxSafeInteger || v < 0) return null;
        return v > int.MaxValue ? int.MaxValue : (int)v;
    }

    public static object? Raw(FieldBag bag, FieldKey key) => bag.TryGetValue(key.Name, out var value) ? value : null;

    /// <summary>A configured model: delegates to the algorithm object's functions.</summary>
    internal sealed class Core(
        object config,
        ModelBounds bounds,
        object algorithm,
        Func<FsrsState?, double, Rating, double?, FsrsState> nextState,
        Func<double, double, double> nextInterval,
        Func<double, double, double> forgettingCurve) : IModelCore<FsrsState>
    {
        public object Config => config;
        public ModelBounds Bounds => bounds;
        public object Algorithm => algorithm;

        public FsrsState Step(ModelStepInput<FsrsState> input) =>
            nextState(input.MemoryState, input.ElapsedDays, input.Rating, input.Retrievability);

        public double NextInterval(FsrsState memoryState, double desiredRetention) =>
            nextInterval(memoryState.Stability, desiredRetention);

        public double ForgettingCurve(FsrsState memoryState, double elapsedDays) =>
            forgettingCurve(elapsedDays, memoryState.Stability);

        public IReadOnlyList<FsrsState> Forward(IReadOnlyList<ModelReview> history, FsrsState? initialState = null)
        {
            var states = new List<FsrsState>(history.Count);
            var memoryState = initialState;
            foreach (var review in history)
            {
                memoryState = Step(new ModelStepInput<FsrsState>(memoryState, review.Rating, review.DeltaT));
                states.Add(memoryState);
            }
            return states;
        }
    }
}
