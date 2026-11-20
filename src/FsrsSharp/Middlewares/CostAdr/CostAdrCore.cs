using FsrsSharp.Models;

namespace FsrsSharp.Middlewares;

/// <summary>The Cost-ADR retention evaluator (fsrs-rs <c>cost_adr.rs</c>, policy version 1; ts-fsrs <c>cost-adr/core.ts</c>).</summary>
public static class CostAdrCore
{
    /// <summary>Test hook: called once per <c>EvaluateRetention</c> on the calling thread.</summary>
    [ThreadStatic]
    internal static Action? EvaluationObserver;

    public static double EvaluateRetention(CostAdrPolicy policy, FsrsState memoryState, double costWeight) =>
        EvaluateRetention(policy, memoryState.Stability, memoryState.Difficulty, costWeight);

    public static double EvaluateRetention(CostAdrPolicy policy, double memoryStability, double memoryDifficulty, double costWeight)
    {
        EvaluationObserver?.Invoke();
        var bounds = policy.Bounds;
        var coefficients = policy.Coefficients;
        var stability = FsrsMath.Clamp(memoryStability, bounds.SMin, bounds.SMax);
        var difficulty = FsrsMath.Clamp(memoryDifficulty, bounds.DMin, bounds.DMax);
        var logMin = Math.Log(bounds.SMin);
        var xs = FsrsMath.Clamp((Math.Log(stability) - logMin) / (Math.Log(bounds.SMax) - logMin), 0, 1);
        var xd = FsrsMath.Clamp((difficulty - bounds.DMin) / (bounds.DMax - bounds.DMin), 0, 1);
        var weight = FsrsMath.Clamp(costWeight, policy.CostWeightMin, policy.CostWeightMax);
        var logWeightMin = Log1P(policy.CostWeightMin);
        var z = FsrsMath.Clamp((Log1P(weight) - logWeightMin) / (Log1P(policy.CostWeightMax) - logWeightMin), 0, 1);

        double[] features = [1, xs, xd, xs * xd, xs * xs];
        double Dot(int offset)
        {
            var sum = 0.0;
            for (var i = 0; i < features.Length; i++) sum += coefficients[offset + i] * features[i];
            return sum;
        }

        var value = Dot(0) - Softplus(Dot(5)) * z - Softplus(Dot(10)) * z * z;
        var exp = Math.Exp(-Math.Abs(value));
        var sigmoid = value >= 0 ? 1 / (1 + exp) : exp / (1 + exp);
        return FsrsMath.Clamp(
            policy.RetentionMin + (policy.RetentionMax - policy.RetentionMin) * sigmoid,
            policy.RetentionMin,
            policy.RetentionMax);
    }

    private static double Softplus(double value)
    {
        if (value > 20) return value;
        if (value < -20) return Math.Exp(value);
        return Log1P(Math.Exp(value));
    }

    /// <summary>Accurate <c>ln(1 + x)</c> (JavaScript <c>Math.log1p</c>).</summary>
    internal static double Log1P(double x)
    {
        var u = 1 + x;
        if (u == 1) return x;
        if (double.IsInfinity(u)) return Math.Log(u);
        return Math.Log(u) * x / (u - 1);
    }
}
