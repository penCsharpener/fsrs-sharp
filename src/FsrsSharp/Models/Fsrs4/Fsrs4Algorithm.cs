using FsrsSharp.Kit;

namespace FsrsSharp.Models.Fsrs4;

/// <summary>
/// The FSRS-4 algorithm (ts-fsrs <c>models/fsrs-4/algorithm.ts</c>).
/// See https://github.com/open-spaced-repetition/awesome-fsrs/wiki/The-Algorithm#fsrs-v4
/// </summary>
public sealed class Fsrs4Algorithm
{
    private readonly IReadOnlyList<double> _weights;
    private readonly double _sMin;
    private readonly double _sMax;
    private readonly double _dMin;
    private readonly double _dMax;

    public Fsrs4Algorithm(IReadOnlyList<double> weights, ModelBounds bounds)
    {
        if (weights is null || weights.Count != 17)
            throw new FsrsValidationException($"FSRS4Algorithm requires exactly 17 weights, but received {ModelSupport.CountText(weights)}");
        _weights = weights;
        Bounds = bounds;
        _sMin = bounds["sMin"];
        _sMax = bounds["sMax"];
        _dMin = bounds["dMin"];
        _dMax = bounds["dMax"];
    }

    public ModelBounds Bounds { get; }

    public IReadOnlyList<double> Weights => _weights;

    /// <summary>The FSRS-4 forgetting curve (static form of upstream's exported <c>forgetting_curve</c>).</summary>
    public static double ComputeForgettingCurve(double elapsedDays, double stability) =>
        FsrsMath.RoundTo(Math.Pow(1 + elapsedDays / (9 * stability), -1), 8);

    public double ForgettingCurve(double elapsedDays, double stability) => ComputeForgettingCurve(elapsedDays, stability);

    public double InitStability(Rating g) => FsrsMath.Clamp(_weights[(int)g - 1], _sMin, _sMax);

    public double InitDifficulty(Rating g)
    {
        var w = _weights;
        var d = w[4] - ((int)g - 3) * w[5];
        return FsrsMath.Clamp(FsrsMath.RoundTo(d, 8), _dMin, _dMax);
    }

    public double NextInterval(double s, double desiredRetention)
    {
        ModelSupport.CheckDesiredRetention(desiredRetention, "Requested retention rate should be in the range (0,1]");
        var intervalModifier = FsrsMath.RoundTo(9 * (1 / desiredRetention - 1), 8);
        return Math.Max(Helpers.JsRound(s * intervalModifier), 1);
    }

    public double NextDifficulty(double d, Rating g)
    {
        var nextD = d - _weights[6] * ((int)g - 3);
        return FsrsMath.Clamp(MeanReversion(_weights[4], nextD), _dMin, _dMax);
    }

    public double MeanReversion(double init, double current)
    {
        var w = _weights;
        return FsrsMath.RoundTo(w[7] * init + (1 - w[7]) * current, 8);
    }

    public double NextRecallStability(double d, double s, double r, Rating g)
    {
        var w = _weights;
        var hardPenalty = Rating.Hard == g ? w[15] : 1;
        var easyBound = Rating.Easy == g ? w[16] : 1;
        return FsrsMath.RoundTo(
            FsrsMath.Clamp(
                s * (1 + Math.Exp(w[8]) * (11 - d) * Math.Pow(s, -w[9]) * (Math.Exp((1 - r) * w[10]) - 1) * hardPenalty * easyBound),
                _sMin,
                _sMax),
            8);
    }

    public double NextForgetStability(double d, double s, double r)
    {
        var w = _weights;
        return FsrsMath.RoundTo(
            FsrsMath.Clamp(
                w[11] * Math.Pow(d, -w[12]) * (Math.Pow(s + 1, w[13]) - 1) * Math.Exp((1 - r) * w[14]),
                _sMin,
                _sMax),
            8);
    }

    /// <summary>The next memory state; <paramref name="memoryState"/> <c>null</c> means a new card.</summary>
    public FsrsState NextState(FsrsState? memoryState, double t, Rating g, double? r = null)
    {
        var d = memoryState?.Difficulty ?? 0;
        var s = memoryState?.Stability ?? 0;
        ModelSupport.CheckDeltaT(t);
        ModelSupport.CheckGrade(g);
        if (g == Rating.Manual) return new FsrsState(s, d);
        if (d == 0 && s == 0) return new FsrsState(InitStability(g), InitDifficulty(g));
        if (d < _dMin || s < _sMin)
            throw new FsrsValidationException($"Invalid memory state {{ difficulty: {ModelSupport.Num(d)}, stability: {ModelSupport.Num(s)} }}");

        var retrievability = r ?? ForgettingCurve(t, s);
        var newS = g == Rating.Again
            ? NextForgetStability(d, s, retrievability)
            : NextRecallStability(d, s, retrievability, g);
        return new FsrsState(newS, NextDifficulty(d, g));
    }
}
