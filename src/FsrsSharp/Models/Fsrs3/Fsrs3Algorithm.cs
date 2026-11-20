using FsrsSharp.Kit;

namespace FsrsSharp.Models.Fsrs3;

/// <summary>
/// The FSRS-3 algorithm (ts-fsrs <c>models/fsrs-3/algorithm.ts</c>).
/// See https://github.com/open-spaced-repetition/awesome-fsrs/wiki/The-Algorithm#fsrs-3
/// </summary>
public sealed class Fsrs3Algorithm
{
    private readonly IReadOnlyList<double> _weights;
    private readonly double _sMin;
    private readonly double _sMax;
    private readonly double _dMin;
    private readonly double _dMax;

    public Fsrs3Algorithm(IReadOnlyList<double> weights, ModelBounds bounds)
    {
        if (weights is null || weights.Count != 13)
            throw new FsrsValidationException($"FSRS3Algorithm requires exactly 13 weights, but received {ModelSupport.CountText(weights)}");
        _weights = weights;
        Bounds = bounds;
        _sMin = bounds["sMin"];
        _sMax = bounds["sMax"];
        _dMin = bounds["dMin"];
        _dMax = bounds["dMax"];
    }

    public ModelBounds Bounds { get; }

    public IReadOnlyList<double> Weights => _weights;

    /// <summary>The FSRS-3 forgetting curve (static form of upstream's exported <c>forgetting_curve</c>).</summary>
    public static double ComputeForgettingCurve(double elapsedDays, double stability) =>
        FsrsMath.RoundTo(Math.Pow(0.9, elapsedDays / stability), 8);

    public double ForgettingCurve(double elapsedDays, double stability) => ComputeForgettingCurve(elapsedDays, stability);

    public double InitStability(Rating g) =>
        FsrsMath.Clamp(_weights[0] + ((int)g - 1) * _weights[1], _sMin, _sMax);

    public double InitDifficulty(Rating g) =>
        FsrsMath.Clamp(_weights[2] + ((int)g - 3) * _weights[3], _dMin, _dMax);

    public double NextInterval(double s, double desiredRetention)
    {
        ModelSupport.CheckDesiredRetention(desiredRetention, "Desired retention rate should be in the range (0,1]");
        var intervalModifier = Math.Log(desiredRetention) / Math.Log(0.9);
        return FsrsMath.RoundTo(Math.Max(Helpers.JsRound(s * intervalModifier), 1), 8);
    }

    public double NextDifficulty(double d, Rating g)
    {
        var w = _weights;
        var nextD = d + w[4] * ((int)g - 3);
        return FsrsMath.RoundTo(FsrsMath.Clamp(MeanReversion(w[2], nextD), _dMin, _dMax), 8);
    }

    public double MeanReversion(double init, double current)
    {
        var w = _weights;
        return FsrsMath.RoundTo(w[5] * init + (1 - w[5]) * current, 8);
    }

    public double NextRecallStability(double d, double s, double r)
    {
        var w = _weights;
        return FsrsMath.RoundTo(
            FsrsMath.Clamp(
                s * (1 + Math.Exp(w[6]) * (11 - d) * Math.Pow(s, w[7]) * (Math.Exp((1 - r) * w[8]) - 1)),
                _sMin,
                _sMax),
            8);
    }

    public double NextForgetStability(double d, double s, double r)
    {
        var w = _weights;
        return FsrsMath.RoundTo(
            FsrsMath.Clamp(
                w[9] * Math.Pow(d, w[10]) * Math.Pow(s, w[11]) * Math.Exp((1 - r) * w[12]),
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
        var newD = NextDifficulty(d, g);
        var newS = g == Rating.Again
            ? NextForgetStability(newD, s, retrievability)
            : NextRecallStability(newD, s, retrievability);
        return new FsrsState(newS, newD);
    }
}
