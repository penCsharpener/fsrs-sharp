using System.Collections.Immutable;

namespace FsrsSharp.Middlewares;

/// <summary>Stability and difficulty bounds of a <see cref="CostAdrPolicy"/>.</summary>
public sealed record CostAdrBounds(double SMin, double SMax, double DMin, double DMax);

/// <summary>
/// Retention parameters from fsrs-rs <c>CostAdrPolicy</c> (upstream <c>CostAdrPolicy</c>).
/// <see cref="Coefficients"/> holds exactly 15 finite coefficients, each in [-64, 64].
/// </summary>
public sealed record CostAdrPolicy(
    IReadOnlyList<double> Coefficients,
    double CostWeightMin,
    double CostWeightMax,
    double RetentionMin,
    double RetentionMax,
    CostAdrBounds Bounds);

/// <summary>Cost-ADR defaults (ts-fsrs <c>middlewares/cost-adr/constants.ts</c>).</summary>
public static class CostAdrConstants
{
    /// <summary>Default used by fsrs-rs <c>examples/cost_adr.rs</c>, <c>ExampleConfig::default()</c>.</summary>
    public const double DefaultGoalCostWeight = 64;

    /// <summary>fsrs-rs 710391ed7466ee279dbdcff5f9cef3713caf42d3, <c>cost_adr.rs</c> <c>default_initial()</c>.</summary>
    public static CostAdrPolicy DefaultPolicy { get; } = new(
        ImmutableArray.Create(
            -0.202, 9.14, -0.0978, 0.226, -5.31, -7.44, 24.1, -0.375, 1.81, -22.9,
            -5.82, 22.3, 1.72, -1.99, -19.4),
        CostWeightMin: 0,
        CostWeightMax: 1024,
        RetentionMin: 0.3,
        RetentionMax: 0.995,
        Bounds: new CostAdrBounds(SMin: 0.0001, SMax: 36500, DMin: 1, DMax: 10));
}
