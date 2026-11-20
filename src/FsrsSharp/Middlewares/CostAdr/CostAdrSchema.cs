using FsrsSharp.Kit;

namespace FsrsSharp.Middlewares;

/// <summary>Validation of the Cost-ADR policy and config (ts-fsrs <c>middlewares/cost-adr/schema.ts</c>).</summary>
public static class CostAdrSchema
{
    public const string PolicyMessage = "Expected valid Cost ADR policy";
    public const string BoundsMessage = "Expected valid Cost ADR bounds";
    public const string GoalCostWeightMessage = "Expected finite goalCostWeight in [0, 1024]";

    /// <summary>Returns <c>null</c> when <paramref name="policy"/> is valid, else the error message.</summary>
    public static string? ValidatePolicy(CostAdrPolicy? policy)
    {
        if (policy is null
            || policy.Coefficients is null
            || policy.Coefficients.Count != 15
            || policy.Coefficients.Any(c => !double.IsFinite(c) || c < -64 || c > 64)
            || !double.IsFinite(policy.CostWeightMin)
            || !double.IsFinite(policy.CostWeightMax)
            || policy.CostWeightMin < 0
            || policy.CostWeightMax <= policy.CostWeightMin
            || CostAdrCore.Log1P(policy.CostWeightMax) <= CostAdrCore.Log1P(policy.CostWeightMin)
            || !double.IsFinite(policy.RetentionMin)
            || !double.IsFinite(policy.RetentionMax)
            || policy.RetentionMin <= 0
            || policy.RetentionMax <= policy.RetentionMin
            || policy.RetentionMax >= 1)
        {
            return PolicyMessage;
        }

        var bounds = policy.Bounds;
        if (bounds is null
            || !double.IsFinite(bounds.SMin)
            || !double.IsFinite(bounds.SMax)
            || !double.IsFinite(bounds.DMin)
            || !double.IsFinite(bounds.DMax)
            || bounds.SMin <= 0
            || bounds.SMax <= bounds.SMin
            || Math.Log(bounds.SMax) <= Math.Log(bounds.SMin)
            || bounds.DMax <= bounds.DMin
            || !double.IsFinite(bounds.DMax - bounds.DMin))
        {
            return BoundsMessage;
        }

        return null;
    }

    /// <summary>Validates <paramref name="policy"/> and returns a copy that shares no arrays with the input.</summary>
    /// <exception cref="FsrsValidationException">The policy is invalid.</exception>
    public static CostAdrPolicy ParsePolicy(CostAdrPolicy? policy)
    {
        var error = ValidatePolicy(policy);
        if (error is not null) throw new FsrsValidationException(error);
        var p = policy!;
        return new CostAdrPolicy(
            p.Coefficients.ToArray(),
            p.CostWeightMin,
            p.CostWeightMax,
            p.RetentionMin,
            p.RetentionMax,
            new CostAdrBounds(p.Bounds.SMin, p.Bounds.SMax, p.Bounds.DMin, p.Bounds.DMax));
    }

    /// <summary>Returns <c>null</c> when <paramref name="goalCostWeight"/> is a finite value in [0, 1024].</summary>
    public static string? ValidateGoalCostWeight(double goalCostWeight) =>
        double.IsFinite(goalCostWeight) && goalCostWeight >= 0 && goalCostWeight <= 1024 ? null : GoalCostWeightMessage;

    /// <summary>Parses the <c>costAdrPolicy</c> and <c>goalCostWeight</c> fields of a config bag (like upstream's config schema).</summary>
    public static (CostAdrPolicy Policy, double GoalCostWeight) ParseConfig(FieldBag config)
    {
        var goal = config.TryGet(CostAdrMiddleware.GoalCostWeight, out var g) ? g : double.NaN;
        var goalError = ValidateGoalCostWeight(goal);
        if (goalError is not null) throw new FsrsValidationException(goalError);
        config.TryGet(CostAdrMiddleware.Policy, out var policy);
        return (ParsePolicy(policy), goal);
    }
}
