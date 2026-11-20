using System.Globalization;

namespace FsrsSharp;

/// <summary>
/// Numeric helpers ported from ts-fsrs <c>help.ts</c>.
/// </summary>
/// <remarks>
/// Upstream disables rounding in <see cref="RoundTo"/> at build time with the
/// <c>TS_FSRS_DISABLE_ROUNDING</c> flag. The C# equivalent is the environment variable
/// <c>FSRS_SHARP_DISABLE_ROUNDING</c>: when it equals <c>"true"</c> (read once, on first use of this
/// type) <see cref="RoundTo"/> returns its input unchanged.
/// </remarks>
public static class FsrsMath
{
    private static readonly bool RoundingDisabled =
        Environment.GetEnvironmentVariable("FSRS_SHARP_DISABLE_ROUNDING") == "true";

    public static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);

    /// <summary>Rounds <paramref name="num"/> to <paramref name="decimals"/> places like <c>Math.round(num * 10**d) / 10**d</c>.</summary>
    public static double RoundTo(double num, int decimals)
    {
        if (RoundingDisabled) return num;
        var factor = Math.Pow(10, decimals);
        return Helpers.JsRound(num * factor) / factor;
    }

    /// <summary>JavaScript <c>+x.toFixed(decimals)</c>.</summary>
    internal static double ToFixed(double value, int decimals) =>
        double.Parse(value.ToString("F" + decimals, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
