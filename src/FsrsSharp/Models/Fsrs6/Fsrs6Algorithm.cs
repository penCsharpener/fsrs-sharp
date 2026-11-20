using FsrsSharp.Kit;

namespace FsrsSharp.Models.Fsrs6;

/// <summary>
/// The FSRS-6 algorithm (ts-fsrs <c>models/fsrs-6/algorithm.ts</c>).
/// See https://github.com/open-spaced-repetition/awesome-fsrs/wiki/The-Algorithm#fsrs-6
/// </summary>
public sealed class Fsrs6Algorithm
{
    private readonly IReadOnlyList<double> _weights;
    private readonly bool _enableShortTerm;
    private readonly double _sMin;
    private readonly double _sMax;
    private double _decayWeight = double.NaN;
    private double _decay;
    private double _factor;

    public Fsrs6Algorithm(IReadOnlyList<double> weights, bool enableShortTerm, ModelBounds bounds)
    {
        if (weights is null || weights.Count != 21)
            throw new FsrsValidationException($"FSRS6Algorithm requires exactly 21 weights, but received {ModelSupport.CountText(weights)}");
        _weights = weights;
        _enableShortTerm = enableShortTerm;
        Bounds = bounds;
        _sMin = bounds["sMin"];
        _sMax = bounds["sMax"];
    }

    public ModelBounds Bounds { get; }

    public IReadOnlyList<double> Weights => _weights;

    /// <summary>Computes <c>(decay, factor)</c> from a decay weight (e.g. <c>0.1542</c>).</summary>
    public static (double Decay, double Factor) ComputeDecayFactor(double decayWeight)
    {
        var decay = -decayWeight;
        var factor = Math.Exp(Math.Pow(decay, -1) * Math.Log(0.9)) - 1.0;
        return (decay, FsrsMath.RoundTo(factor, 8));
    }

    /// <summary>Computes <c>(decay, factor)</c> from a full parameter list (uses <c>w[20]</c>).</summary>
    public static (double Decay, double Factor) ComputeDecayFactor(IReadOnlyList<double> parameters) =>
        ComputeDecayFactor(parameters[20]);

    public static double ComputeForgettingCurve(double decayWeight, double elapsedDays, double stability)
    {
        var (decay, factor) = ComputeDecayFactor(decayWeight);
        return FsrsMath.RoundTo(Math.Pow(1 + factor * elapsedDays / stability, decay), 8);
    }

    public static double ComputeForgettingCurve(IReadOnlyList<double> parameters, double elapsedDays, double stability) =>
        ComputeForgettingCurve(parameters[20], elapsedDays, stability);

    private (double Decay, double Factor) GetDecayFactor()
    {
        var decayWeight = _weights[20];
        // NaN never equals itself, so a NaN weight recomputes every time, like upstream's !==.
        if (decayWeight != _decayWeight)
        {
            var (decay, factor) = ComputeDecayFactor(decayWeight);
            _decayWeight = decayWeight;
            _decay = decay;
            _factor = factor;
        }
        return (_decay, _factor);
    }

    public double ForgettingCurve(double elapsedDays, double stability)
    {
        var (decay, factor) = GetDecayFactor();
        return FsrsMath.RoundTo(Math.Pow(1 + factor * elapsedDays / stability, decay), 8);
    }

    public double InitStability(Rating g) => FsrsMath.Clamp(_weights[(int)g - 1], _sMin, _sMax);

    public double InitDifficulty(Rating g)
    {
        var w = _weights;
        var d = w[4] - Math.Exp(((int)g - 1) * w[5]) + 1;
        return FsrsMath.RoundTo(d, 8);
    }

    public double NextInterval(double s, double desiredRetention)
    {
        ModelSupport.CheckDesiredRetention(desiredRetention, "Desired retention rate should be in the range (0,1]");
        var (decay, factor) = GetDecayFactor();
        return Math.Max(Helpers.JsRound(s / factor * (Math.Pow(desiredRetention, 1 / decay) - 1)), 1);
    }

    /// <summary>See https://github.com/open-spaced-repetition/fsrs4anki/issues/697</summary>
    public double LinearDamping(double deltaD, double oldD) => FsrsMath.RoundTo(deltaD * (10 - oldD) / 9, 8);

    public double NextDifficulty(double d, Rating g)
    {
        var deltaD = -_weights[6] * ((int)g - 3);
        var nextD = d + LinearDamping(deltaD, d);
        return FsrsMath.Clamp(MeanReversion(InitDifficulty(Rating.Easy), nextD), 1, 10);
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

    public double NextShortTermStability(double s, Rating g)
    {
        var w = _weights;
        var sinc = Math.Pow(s, -w[19]) * Math.Exp(w[17] * ((int)g - 3 + w[18]));
        var maskedSinc = g >= Rating.Hard ? Math.Max(sinc, 1.0) : sinc;
        return FsrsMath.RoundTo(FsrsMath.Clamp(s * maskedSinc, _sMin, _sMax), 8);
    }

    /// <summary>The next memory state; <paramref name="memoryState"/> <c>null</c> means a new card.</summary>
    public FsrsState NextState(FsrsState? memoryState, double t, Rating g, double? r = null)
    {
        var d = memoryState?.Difficulty ?? 0;
        var s = memoryState?.Stability ?? 0;
        ModelSupport.CheckDeltaT(t);
        ModelSupport.CheckGrade(g);
        if (g == Rating.Manual) return new FsrsState(s, d);
        if (d == 0 && s == 0)
            return new FsrsState(InitStability(g), FsrsMath.Clamp(InitDifficulty(g), 1, 10));
        if (d < 1 || s < _sMin)
            throw new FsrsValidationException($"Invalid memory state {{ difficulty: {ModelSupport.Num(d)}, stability: {ModelSupport.Num(s)} }}");

        var w = _weights;
        var retrievability = r ?? ForgettingCurve(t, s);
        double newS;
        if (t == 0 && _enableShortTerm)
        {
            newS = NextShortTermStability(s, g);
        }
        else if (g == Rating.Again)
        {
            var sAfterFail = NextForgetStability(d, s, retrievability);
            double w17 = 0, w18 = 0;
            if (_enableShortTerm)
            {
                w17 = w[17];
                w18 = w[18];
            }
            var nextSMin = s / Math.Exp(w17 * w18);
            newS = FsrsMath.Clamp(FsrsMath.RoundTo(nextSMin, 8), _sMin, sAfterFail);
        }
        else
        {
            newS = NextRecallStability(d, s, retrievability, g);
        }

        var newD = NextDifficulty(d, g);
        return new FsrsState(newS, newD);
    }
}
