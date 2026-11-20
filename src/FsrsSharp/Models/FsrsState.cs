namespace FsrsSharp.Models;

/// <summary>The FSRS memory state shared by FSRS-3 … FSRS-6 (ts-fsrs <c>FSRSState</c>).</summary>
public record FsrsState(double Stability, double Difficulty)
{
    /// <summary>Returns <c>null</c> when both values are finite, else upstream's error message.</summary>
    public static string? Validate(FsrsState state) =>
        double.IsFinite(state.Stability) && double.IsFinite(state.Difficulty) ? null : "Expected FSRS memory state";
}

/// <summary>FSRS-7 memory state: adds the fast-trace stability (ts-fsrs <c>FSRS7State</c>).</summary>
public sealed record Fsrs7State(double Stability, double StabilityFast, double Difficulty) : FsrsState(Stability, Difficulty)
{
    public static string? Validate(Fsrs7State state) =>
        double.IsFinite(state.Stability) && double.IsFinite(state.StabilityFast) && double.IsFinite(state.Difficulty)
            ? null
            : "Expected FSRS7 memory state with finite stability, stabilityFast and difficulty";
}
