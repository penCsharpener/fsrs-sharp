using FsrsSharp.Kit;

namespace FsrsSharp.Models.Fsrs7;

/// <summary>Retrievability and derivative of the FSRS-7 dual-trace curve plus its components.</summary>
/// <param name="Retrievability">Mixed recall probability, rescaled into [1e-5, 1 - 1e-5].</param>
/// <param name="Derivative">Elapsed-day derivative of <paramref name="Retrievability"/>.</param>
/// <param name="Fast">Raw recall of the fast trace.</param>
/// <param name="Slow">Raw recall of the slow trace.</param>
/// <param name="FastWeight">Unnormalised mixture weight of the fast trace.</param>
/// <param name="SlowWeight">Unnormalised mixture weight of the slow trace.</param>
/// <param name="Total">Sum of both weights.</param>
public readonly record struct Fsrs7Curve(
    double Retrievability,
    double Derivative,
    double Fast,
    double Slow,
    double FastWeight,
    double SlowWeight,
    double Total);

/// <summary>Dual-trace FSRS-7, ported from fsrs-rs model_v7.rs. Times are fractional days; callers validate inputs.</summary>
public sealed class Fsrs7Algorithm
{
    private readonly double[] _weights;
    private readonly ModelBounds _bounds;
    private readonly double _sMin, _sMax, _fastMin, _fastMax, _dMin, _dMax;
    private readonly double _logSMax;

    public Fsrs7Algorithm(IReadOnlyList<double> weights, ModelBounds bounds)
    {
        if (weights is null || weights.Count != 34 || !weights.All(double.IsFinite))
            throw new FsrsValidationException("FSRS7Algorithm requires exactly 34 finite weights");
        _weights = weights.ToArray();
        _bounds = bounds;
        _sMin = bounds["sMin"];
        _sMax = bounds["sMax"];
        _fastMin = bounds["stabilityFastMin"];
        _fastMax = bounds["stabilityFastMax"];
        _dMin = bounds["dMin"];
        _dMax = bounds["dMax"];
        _logSMax = Math.Log(_sMax);
    }

    public ModelBounds Bounds => _bounds;

    // --- shared math -------------------------------------------------------------------------

    private static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);

    private static double RoundTo(double num, int decimals)
    {
        var factor = Math.Pow(10, decimals);
        return Helpers.JsRound(num * factor) / factor;
    }

    /// <summary>JavaScript <c>Math.pow</c>: differs from .NET for NaN exponents and |base| == 1 with infinite exponents.</summary>
    private static double Pow(double x, double y)
    {
        if (double.IsNaN(y)) return double.NaN;
        if (double.IsInfinity(y) && Math.Abs(x) == 1) return double.NaN;
        return Math.Pow(x, y);
    }

    private readonly record struct FastParams(double Decay, double Scale);

    private readonly record struct CurveParams(FastParams Fast, double Decay, double Scale, double Weight1, double Weight2, double Total);

    private static FastParams PrepareFast(double[] w, double s)
    {
        var decay = -Clamp(w[23] * Pow(s, w[33] - 0.3), 0.01, 0.95);
        var factor = Math.Exp(Math.Min(Math.Log(w[25]) / decay, 60)) - 1;
        var scale = factor / s;
        return new FastParams(decay, scale);
    }

    private static (double Recall, double Derivative) FastComponent(double t, FastParams p)
    {
        // Negative elapsed time would drive `base` below zero and make Math.pow return NaN.
        var days = Math.Max(t, 0);
        var @base = 1 + p.Scale * days;
        return (Pow(@base, p.Decay), p.Decay * Pow(@base, p.Decay - 1) * p.Scale);
    }

    /// <summary>Clamp the state and prepare time-independent values once per curve or interval solve.</summary>
    private static CurveParams PrepareCurve(double[] w, Fsrs7State state,
        double sMin, double sMax, double fastMin, double fastMax, double dMin, double dMax)
    {
        var s = Clamp(state.Stability, sMin, sMax);
        var sFast = Clamp(state.StabilityFast, fastMin, fastMax);
        var d = Clamp(state.Difficulty, dMin, dMax);
        var fast = PrepareFast(w, sFast);
        var decay = -Clamp(w[24], 0.01, 0.95);
        var scale = (Pow(w[26], 1 / decay) - 1) * Math.Exp((d - 5) * (w[32] - 0.3)) / s;
        var weight1 = w[27] * Pow(sFast, -w[29]);
        var weight2 = w[28] * Pow(s, w[30]) * Math.Exp((d - 5) * (w[31] - 0.5));
        return new CurveParams(fast, decay, scale, weight1, weight2, weight1 + weight2);
    }

    private static Fsrs7Curve ComputeCurve(double t, CurveParams p)
    {
        var days = Math.Max(t, 0);
        var fast = FastComponent(days, p.Fast);
        var @base = 1 + p.Scale * days;
        var slow = Pow(@base, p.Decay);
        var derivative = p.Decay * Pow(@base, p.Decay - 1) * p.Scale;
        return new Fsrs7Curve(
            (p.Weight1 * fast.Recall + p.Weight2 * slow) / p.Total * (1 - 2e-5) + 1e-5,
            (p.Weight1 * fast.Derivative + p.Weight2 * derivative) / p.Total * (1 - 2e-5),
            fast.Recall,
            slow,
            p.Weight1,
            p.Weight2,
            p.Total);
    }

    /// <summary>
    /// Evaluates the FSRS-7 dual-trace curve using the standard model bounds. Does not validate
    /// weights; callers must supply valid FSRS-7 weights (missing indices evaluate as NaN, like JS).
    /// </summary>
    public static Fsrs7Curve Curve(IReadOnlyList<double> weights, double elapsedDays, Fsrs7State state)
    {
        var w = new double[Math.Max(weights.Count, 34)];
        Array.Fill(w, double.NaN);
        for (var i = 0; i < weights.Count; i++) w[i] = weights[i];
        var b = Fsrs7Constants.ModelBounds;
        return ComputeCurve(elapsedDays,
            PrepareCurve(w, state, b["sMin"], b["sMax"], b["stabilityFastMin"], b["stabilityFastMax"], b["dMin"], b["dMax"]));
    }

    private CurveParams Prepare(Fsrs7State state) =>
        PrepareCurve(_weights, state, _sMin, _sMax, _fastMin, _fastMax, _dMin, _dMax);

    // --- algorithm ---------------------------------------------------------------------------

    public double InitStability(Rating grade) => Clamp(_weights[(int)grade - 1], _sMin, _sMax);

    public double InitDifficulty(Rating grade) => _weights[4] - Math.Exp(_weights[5] * ((int)grade - 1)) + 1;

    /// <summary>
    /// Retrievability and its elapsed-day derivative, plus the raw trace recalls and the
    /// unnormalised weights mixing them.
    /// </summary>
    public Fsrs7Curve Curve(double t, Fsrs7State state) => ComputeCurve(t, Prepare(state));

    /// <summary>Raw fractional days for retention in (0,1); rates &gt;= 0.9999 yield zero.</summary>
    public double NextInterval(Fsrs7State state, double desiredRetention)
    {
        if (!double.IsFinite(desiredRetention) || desiredRetention <= 0 || desiredRetention >= 1)
            throw new FsrsValidationException("Desired retention rate should be in the range (0,1)");
        var target = Clamp(desiredRetention, Fsrs7Constants.DrMin, Fsrs7Constants.DrMax);
        if (target >= Fsrs7Constants.DrMax) return 0;

        var s = Clamp(state.Stability, _sMin, _sMax);
        var sFast = Clamp(state.StabilityFast, _fastMin, _fastMax);
        var maxStability = Math.Max(s, sFast);
        var p = Prepare(state);
        var logT = Math.Log(maxStability);
        for (var i = 0; i < 7; i++)
        {
            logT = Clamp(logT, Fsrs7Constants.LogMinT, _logSMax);
            var t = Clamp(Math.Exp(logT), Fsrs7Constants.MinT, _sMax);
            var c = ComputeCurve(t, p);
            logT -= Clamp((c.Retrievability - target) / Math.Min(c.Derivative * t, -1e-12), -4, 4);
            if (!double.IsFinite(logT)) break;
        }
        var interval = Clamp(Math.Exp(logT), 0, _sMax);
        if (Math.Abs(ComputeCurve(interval, p).Retrievability - target) <= 1e-3) return interval;

        // Rust's scalar inference fallback handles saturated or unconverged Newton steps.
        double low = 0;
        var high = Math.Min(Math.Max(maxStability, 1), _sMax);
        while (ComputeCurve(high, p).Retrievability > target && high < _sMax)
        {
            high = Math.Min(high * 2, _sMax);
        }
        for (var i = 0; i < 50; i++)
        {
            var mid = (low + high) / 2;
            if (ComputeCurve(mid, p).Retrievability > target) low = mid;
            else high = mid;
        }
        return (low + high) / 2;
    }

    private double NextStability(double s, double d, double r, Rating grade, int start)
    {
        var w = _weights;
        var fail = w[start + 3] * (Pow(s + 1, w[start + 4]) - 1) * Math.Exp((1 - r) * w[start + 5]);
        var pls = Math.Min(s, fail);
        if (grade == Rating.Again) return pls;
        var hardPenalty = grade == Rating.Hard ? w[start + 6] : 1;
        var easyBonus = grade == Rating.Easy ? w[start + 7] : 1;
        var increase =
            Math.Exp(w[start] - 1.5) *
                (11 - d) *
                Pow(s, -w[start + 1]) *
                (Math.Exp((1 - r) * w[start + 2]) - 1) *
                hardPenalty *
                easyBonus +
            1;
        return Math.Max(pls, s * increase);
    }

    /// <param name="memoryState">The state before the review; <c>null</c> for a brand-new card.</param>
    /// <param name="retrievability">Pre-computed mixed retrievability (must be in (0,1)), if known.</param>
    public Fsrs7State NextState(Fsrs7State? memoryState, double elapsedDays, Rating rating, double? retrievability = null)
    {
        if (rating < Rating.Manual || rating > Rating.Easy)
            throw new FsrsValidationException($"Invalid grade \"{(int)rating}\"");
        var state = memoryState ?? new Fsrs7State(0, 0, 0);
        if (rating == Rating.Manual)
            return new Fsrs7State(state.Stability, state.StabilityFast, state.Difficulty);
        var grade = rating;
        if (state.Stability == 0 && state.StabilityFast == 0 && state.Difficulty == 0)
        {
            var stability0 = InitStability(grade);
            return new Fsrs7State(
                Clamp(RoundTo(stability0, 8), _sMin, _sMax),
                Clamp(RoundTo(stability0 * 0.8, 8), _fastMin, _fastMax),
                Clamp(RoundTo(InitDifficulty(grade), 8), _dMin, _dMax));
        }
        if (state.Stability <= 0 || state.StabilityFast <= 0)
            throw new FsrsValidationException("Invalid FSRS7 memory state: both stabilities must be positive");
        if (retrievability is { } rv && (!double.IsFinite(rv) || rv <= 0 || rv >= 1))
            throw new FsrsValidationException("Retrievability should be in the range (0,1)");

        var s = Clamp(state.Stability, _sMin, _sMax);
        var sFast = Clamp(state.StabilityFast, _fastMin, _fastMax);
        var d = Clamp(state.Difficulty, _dMin, _dMax);
        var r = retrievability ?? ComputeCurve(elapsedDays, Prepare(state)).Retrievability;
        var stability = NextStability(s, d, r, grade, 7);
        var stabilityFast = NextStability(sFast, d, FastComponent(elapsedDays, PrepareFast(_weights, sFast)).Recall, grade, 15);
        if (grade == Rating.Again) stabilityFast = Math.Min(stabilityFast, stability * 0.8);
        var deltaD = -_weights[6] * ((int)grade - 3);
        if (grade == Rating.Again) deltaD *= r + 0.1;
        var difficulty = 0.01 * InitDifficulty(Rating.Easy) + 0.99 * (d + (10 - d) * deltaD / 9);
        return new Fsrs7State(
            Clamp(RoundTo(stability, 8), _sMin, _sMax),
            Clamp(RoundTo(stabilityFast, 8), _fastMin, _fastMax),
            Clamp(RoundTo(difficulty, 8), _dMin, _dMax));
    }
}
