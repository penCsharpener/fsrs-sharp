namespace FsrsSharp;

/// <summary>Numeric helpers shared by the models, middlewares and the legacy API.</summary>
public static class Helpers
{
    /// <summary>
    /// Rounds like JavaScript's <c>Math.round</c> (half rounds toward +Infinity),
    /// rather than .NET's default banker's rounding or MidpointRounding.AwayFromZero.
    /// </summary>
    public static double JsRound(double x) => Math.Floor(x + 0.5);
}
