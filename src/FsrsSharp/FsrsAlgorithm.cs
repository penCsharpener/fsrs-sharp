namespace FsrsSharp;

/// <summary>
/// Ported from ts-fsrs's <c>algorithm.ts</c>. Implements the core DSR (Difficulty,
/// Stability, Retrievability) math shared by FSRS-4.5 through FSRS-6 — everything
/// except turning that math into scheduled dates, which <see cref="Fsrs"/> and the
/// scheduler classes handle.
/// <para/>
/// Unlike ts-fsrs's <c>Proxy</c>-backed <c>parameters</c> setter, this class exposes
/// <see cref="UpdateParameters"/> as an explicit method to recompute derived state
/// (the interval modifier) — reassigning individual fields on the object returned by
/// <see cref="Parameters"/> will NOT recompute anything.
/// </summary>
public class FsrsAlgorithm
{
    private FsrsParameters _param;
    private double _intervalModifier;
    internal string? Seed;

    public FsrsAlgorithm(FsrsParametersInput? parameters = null)
    {
        _param = Defaults.GeneratorParameters(parameters);
        _intervalModifier = CalculateIntervalModifier(_param.RequestRetention);
    }

    /// <summary>The fully-resolved parameters currently in effect (read-only snapshot; see remarks on updating).</summary>
    public FsrsParameters Parameters => _param;

    public double IntervalModifier => _intervalModifier;

    /// <summary>
    /// Replaces the current parameters with <paramref name="overrides"/> merged over FSRS-6
    /// defaults (same semantics as the constructor), recomputing the interval modifier.
    /// </summary>
    public virtual void UpdateParameters(FsrsParametersInput overrides)
    {
        _param = Defaults.GeneratorParameters(overrides);
        _intervalModifier = CalculateIntervalModifier(_param.RequestRetention);
    }

    /// <summary>
    /// The formula used is: I(r,s) = (r^(1/DECAY) - 1) / FACTOR * s
    /// </summary>
    /// <exception cref="FsrsValidationException">requestRetention is not in (0,1].</exception>
    public double CalculateIntervalModifier(double requestRetention)
    {
        if (requestRetention <= 0 || requestRetention > 1)
        {
            throw new FsrsValidationException("Requested retention rate should be in the range (0,1]");
        }

        var (decay, factor) = ComputeDecayFactor(_param.W);
        return Helpers.RoundTo((Math.Pow(requestRetention, 1 / decay) - 1) / factor, 8);
    }

    /// <summary>decay = -w20; factor = e^(ln(0.9)/decay) - 1, so that R(S,S) = 90%.</summary>
    public static (double Decay, double Factor) ComputeDecayFactor(IReadOnlyList<double> w)
    {
        var decay = -w[20];
        var factor = Math.Exp(Math.Pow(decay, -1) * Math.Log(0.9)) - 1.0;
        return (decay, Helpers.RoundTo(factor, 8));
    }

    /// <summary>
    /// R(t,S) = (1 + FACTOR * t / (9*S))^DECAY — the probability of recall after
    /// <paramref name="elapsedDays"/> days, given memory <paramref name="stability"/>.
    /// </summary>
    public double ForgettingCurve(double elapsedDays, double stability)
    {
        var (decay, factor) = ComputeDecayFactor(_param.W);
        return Helpers.RoundTo(Math.Pow(1 + factor * elapsedDays / stability, decay), 8);
    }

    private static void CheckGrade(Rating g)
    {
        if (g is < Rating.Again or > Rating.Easy)
        {
            throw new FsrsValidationException($"Invalid grade \"{g}\"");
        }
    }

    /// <summary>S0(G) = w[G-1], floored at 0.1.</summary>
    public double InitStability(Rating grade)
    {
        CheckGrade(grade);
        return Math.Max(_param.W[(int)grade - 1], 0.1);
    }

    /// <summary>D0(G) = w4 - e^((G-1)*w5) + 1, clamped to [1,10] by the caller.</summary>
    public double InitDifficulty(Rating grade)
    {
        CheckGrade(grade);
        var w = _param.W;
        var d = w[4] - Math.Exp(((int)grade - 1) * w[5]) + 1;
        return Helpers.RoundTo(d, 8);
    }

    /// <summary>
    /// If fuzzing is disabled or the interval is below 2.5 days, returns it unchanged
    /// (rounded); otherwise returns a pseudo-random interval within the fuzz range,
    /// deterministic for the current <see cref="Seed"/>.
    /// </summary>
    public int ApplyFuzz(double ivl, double elapsedDays)
    {
        if (!_param.EnableFuzz || ivl < 2.5) return (int)Helpers.JsRound(ivl);

        var generator = new Alea(Seed);
        var fuzzFactor = generator.Next();
        var (minIvl, maxIvl) = Helpers.GetFuzzRange(ivl, elapsedDays, _param.MaximumInterval);
        return (int)Math.Floor(fuzzFactor * (maxIvl - minIvl + 1) + minIvl);
    }

    /// <summary>Stability converted to a whole number of days until the next review, fuzzed and capped at MaximumInterval.</summary>
    public int NextInterval(double stability, double elapsedDays)
    {
        var newInterval = Math.Min(Math.Max(1, Helpers.JsRound(stability * _intervalModifier)), _param.MaximumInterval);
        return ApplyFuzz(newInterval, elapsedDays);
    }

    /// <summary>See https://github.com/open-spaced-repetition/fsrs4anki/issues/697</summary>
    public double LinearDamping(double deltaD, double oldD) => Helpers.RoundTo(deltaD * (10 - oldD) / 9, 8);

    /// <summary>
    /// delta_d = -w6*(G-3); next_d = D + LinearDamping(delta_d, D);
    /// D'(D,R) = w7 * D0(Easy) + (1-w7) * next_d, clamped to [1,10].
    /// </summary>
    public double NextDifficulty(double d, Rating grade)
    {
        CheckGrade(grade);
        var w = _param.W;
        var deltaD = -w[6] * ((int)grade - 3);
        var nextD = d + LinearDamping(deltaD, d);
        return Helpers.Clamp(MeanReversion(InitDifficulty(Rating.Easy), nextD), 1, 10);
    }

    /// <summary>w7 * init + (1-w7) * current.</summary>
    public double MeanReversion(double init, double current) => Helpers.RoundTo(_param.W[7] * init + (1 - _param.W[7]) * current, 8);

    /// <summary>
    /// Stability update after a successful recall (grade Hard/Good/Easy):
    /// S'r(D,S,R,G) = S * (e^w8 * (11-D) * S^-w9 * (e^(w10*(1-R)) - 1) * hard_penalty * easy_bonus + 1)
    /// </summary>
    public double NextRecallStability(double d, double s, double r, Rating grade)
    {
        CheckGrade(grade);
        var w = _param.W;
        var hardPenalty = grade == Rating.Hard ? w[15] : 1;
        var easyBound = grade == Rating.Easy ? w[16] : 1;
        var value = s * (1 + Math.Exp(w[8]) * (11 - d) * Math.Pow(s, -w[9]) * (Math.Exp((1 - r) * w[10]) - 1) * hardPenalty * easyBound);
        return Helpers.RoundTo(Helpers.Clamp(value, Constants.SMin, Constants.SMax), 8);
    }

    /// <summary>
    /// Stability update after a lapse (grade Again):
    /// S'f(D,S,R) = w11 * D^-w12 * ((S+1)^w13 - 1) * e^(w14*(1-R))
    /// </summary>
    public double NextForgetStability(double d, double s, double r)
    {
        var w = _param.W;
        var value = w[11] * Math.Pow(d, -w[12]) * (Math.Pow(s + 1, w[13]) - 1) * Math.Exp((1 - r) * w[14]);
        return Helpers.RoundTo(Helpers.Clamp(value, Constants.SMin, Constants.SMax), 8);
    }

    /// <summary>
    /// Same-day (t=0) stability update, new in FSRS-6:
    /// S's(S,G) = S * e^(w17*(G-3+w18)) * S^-w19
    /// </summary>
    public double NextShortTermStability(double s, Rating grade)
    {
        CheckGrade(grade);
        var w = _param.W;
        var sinc = Math.Pow(s, -w[19]) * Math.Exp(w[17] * ((int)grade - 3 + w[18]));
        var maskedSinc = grade >= Rating.Hard ? Math.Max(sinc, 1.0) : sinc;
        return Helpers.RoundTo(Helpers.Clamp(s * maskedSinc, Constants.SMin, Constants.SMax), 8);
    }

    /// <summary>
    /// Computes the next (difficulty, stability) memory state given the current one (or
    /// null for a brand-new card), the elapsed time <paramref name="t"/> in days, and the
    /// <paramref name="grade"/> given. <paramref name="r"/> (retrievability) is computed
    /// from <see cref="ForgettingCurve"/> if not supplied.
    /// </summary>
    public FsrsMemoryState NextState(FsrsMemoryState? memoryState, double t, Rating grade, double? r = null)
    {
        var state = memoryState ?? new FsrsMemoryState(0, 0);
        var s = state.Stability;
        var d = state.Difficulty;
        if (t < 0)
        {
            throw new FsrsValidationException($"Invalid delta_t \"{t}\"");
        }

        if (grade is < Rating.Manual or > Rating.Easy)
        {
            throw new FsrsValidationException($"Invalid grade \"{grade}\"");
        }

        if (d == 0 && s == 0)
        {
            return new FsrsMemoryState(InitStability(grade), Helpers.Clamp(InitDifficulty(grade), 1, 10));
        }

        if (grade == Rating.Manual)
        {
            return new FsrsMemoryState(s, d);
        }

        if (d < 1 || s < Constants.SMin)
        {
            throw new FsrsValidationException($"Invalid memory state {{ difficulty: {d}, stability: {s} }}");
        }

        var w = _param.W;
        var retrievability = r ?? ForgettingCurve(t, s);
        double newS;
        if (t == 0 && _param.EnableShortTerm)
        {
            newS = NextShortTermStability(s, grade);
        }
        else if (grade == Rating.Again)
        {
            var sAfterFail = NextForgetStability(d, s, retrievability);
            double w17 = 0, w18 = 0;
            if (_param.EnableShortTerm)
            {
                w17 = w[17];
                w18 = w[18];
            }

            var nextSMin = s / Math.Exp(w17 * w18);
            newS = Helpers.Clamp(Helpers.RoundTo(nextSMin, 8), Constants.SMin, sAfterFail);
        }
        else
        {
            newS = NextRecallStability(d, s, retrievability, grade);
        }

        var newD = NextDifficulty(d, grade);
        return new FsrsMemoryState(newS, newD);
    }
}
