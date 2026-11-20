using AwesomeAssertions;

namespace FsrsSharp.Tests.Models;

/// <summary>Helpers shared by the model spec ports.</summary>
internal static class ModelTestHelpers
{
    /// <summary>The four grades in order (srs-kit <c>grades</c>).</summary>
    public static readonly Rating[] GradeList = [Rating.Again, Rating.Hard, Rating.Good, Rating.Easy];

    /// <summary>vitest's <c>toBeCloseTo(expected, precision)</c>: |actual - expected| &lt; 10^-precision / 2.</summary>
    public static void CloseTo(double actual, double expected, int precision = 2) =>
        actual.Should().BeApproximately(expected, Math.Pow(10, -precision) / 2 - 1e-15);

    /// <summary>Port of the specs' <c>expectCloseArray</c> helper.</summary>
    public static void CloseArray(IReadOnlyList<double> actual, IReadOnlyList<double> expected, int precision = 6)
    {
        actual.Should().HaveCount(expected.Count);
        for (var i = 0; i < expected.Count; i++) CloseTo(actual[i], expected[i], precision);
    }

    /// <summary>JavaScript <c>Number(x.toFixed(digits))</c> for the magnitudes the specs use.</summary>
    public static double ToFixed(double value, int digits) => Math.Round(value, digits, MidpointRounding.AwayFromZero);
}
