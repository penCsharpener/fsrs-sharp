using Xunit;
using AwesomeAssertions;
using FsrsSharp.Kit;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs7;

namespace FsrsSharp.Tests.Models.Fsrs7;

/// <summary>Port of ts-fsrs <c>models/fsrs-7/algorithm.spec.ts</c>.</summary>
public class Fsrs7AlgorithmTests
{
    private static readonly IReadOnlyList<double> Defaults = Fsrs7Constants.DefaultWeights;
    private static readonly ModelBounds StandardBounds = Fsrs7Constants.ModelBounds;

    private static ModelBounds Bounds(double sMin, double sMax, double fastMin, double fastMax, double dMin, double dMax) =>
        new(new Dictionary<string, double>
        {
            ["sMin"] = sMin,
            ["sMax"] = sMax,
            ["stabilityFastMin"] = fastMin,
            ["stabilityFastMax"] = fastMax,
            ["dMin"] = dMin,
            ["dMax"] = dMax,
        });

    private static Fsrs7State S(double stability, double fast, double difficulty) => new(stability, fast, difficulty);

    [Fact]
    public void FallsBackToBisectionWhenTheNewtonDerivativeBecomesNonFinite()
    {
        var algorithm = new Fsrs7Algorithm(Defaults, Bounds(0.0001, 36500, double.Epsilon, 36500, 1, 10));
        var state = S(10, double.Epsilon, 5);
        // The fast component's scale overflows: recall stays finite, but its derivative is 0 * Infinity.
        var curve = algorithm.Curve(10, state);
        curve.Retrievability.Should().BeGreaterThan(0);
        curve.Retrievability.Should().BeLessThan(1);
        double.IsNaN(curve.Derivative).Should().BeTrue();

        var interval = algorithm.NextInterval(state, 0.9);
        interval.Should().BeGreaterThan(0);
        interval.Should().BeLessThanOrEqualTo(StandardBounds["sMax"]);
        algorithm.Curve(interval, state).Retrievability.Should().BeApproximately(0.9, 5e-9);
    }

    [Fact]
    public void ClampsNegativeElapsedDaysToZeroLikeFsrsRsInsteadOfReturningNaN()
    {
        var algorithm = new Fsrs7Algorithm(Defaults, StandardBounds);
        var state = S(10, 8, 5);
        var zero = algorithm.Curve(0, state);
        // fsrs-rs applies t.max(0.0) at every scalar entry point, so t < 0 behaves as t = 0.
        foreach (var t in new[] { -1e-9, -0.001, -1, -36500 })
        {
            var curve = algorithm.Curve(t, state);
            curve.Retrievability.Should().Be(zero.Retrievability);
            curve.Derivative.Should().Be(zero.Derivative);
            algorithm.NextState(state, t, Rating.Good).Should().Be(algorithm.NextState(state, 0, Rating.Good));
        }
    }

    [Fact]
    public void MatchesFsrs7MemoryStateProgressionFromFsrsRsAndBenchmark()
    {
        var algorithm = new Fsrs7Algorithm(Defaults, StandardBounds);
        Rating[] ratings = [Rating.Again, Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good];
        double[] intervals = [0, 0, 1, 3, 8, 21];
        var states = new List<Fsrs7State>();
        Fsrs7State? state = null;

        for (var index = 0; index < ratings.Length; index++)
        {
            state = algorithm.NextState(state, intervals[index], ratings[index]);
            states.Add(state);
        }

        static double R4(double v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);
        var rounded = states.Select(s => (D: R4(s.Difficulty), S: R4(s.Stability), F: R4(s.StabilityFast))).ToArray();
        // fsrs-rs c9562d6 and srs-benchmark b4b0254 agree at four decimal places.
        (double D, double S, double F)[] expected =
        [
            (6.1686, 0.1104, 0.0883),
            (6.1092, 0.1104, 0.0883),
            (6.0504, 0.9169, 44.4442),
            (5.9922, 3.5338, 193.5973),
            (5.9346, 10.186, 407.6265),
            (5.8775, 25.9857, 700.5589),
        ];
        rounded.Should().Equal(expected);
    }

    [Fact]
    public void RoundsInitializedMemoryFieldsBeforeApplyingTheirBounds()
    {
        var weights = Defaults.ToArray();
        weights[0] = 1.234567891;
        var algorithm = new Fsrs7Algorithm(weights, StandardBounds);
        algorithm.NextState(null, 0, Rating.Again).Should().Be(S(1.23456789, 0.98765431, 6.1686));

        var bounds = Bounds(1.000000004, 20, 0.800000004, 16, 7.000000004, 9);
        var bounded = new Fsrs7Algorithm(Defaults, bounds);
        bounded.NextState(null, 0, Rating.Again).Should().Be(S(bounds["sMin"], bounds["stabilityFastMin"], bounds["dMin"]));
    }

    [Fact]
    public void UsesInjectedBoundsForInitializationStateUpdatesCurvesAndIntervals()
    {
        var bounds = Bounds(0.5, 2, 0.1, 0.25, 4, 6);
        var algorithm = new Fsrs7Algorithm(Defaults, bounds);
        algorithm.NextState(null, 0, Rating.Again).Should().Be(S(0.5, 0.25, 6));
        algorithm.NextState(null, 0, Rating.Easy).Should().Be(S(2, 0.25, 4));

        var input = S(100, 100, 10);
        var clamped = S(2, 0.25, 6);
        algorithm.Curve(1, input).Should().Be(algorithm.Curve(1, clamped));
        var zero = S(0, 0, 0);
        var minimum = S(0.5, 0.1, 4);
        algorithm.Curve(0, zero).Should().Be(algorithm.Curve(0, minimum));
        foreach (var retention in new[] { 0.1, 0.65, 0.9, 0.99 })
        {
            algorithm.NextInterval(input, retention).Should().Be(algorithm.NextInterval(clamped, retention));
            algorithm.NextInterval(zero, retention).Should().Be(algorithm.NextInterval(minimum, retention));
        }
        foreach (var rating in new[] { Rating.Again, Rating.Hard, Rating.Good, Rating.Easy })
        {
            var state = algorithm.NextState(input, 1, rating);
            state.Should().Be(algorithm.NextState(clamped, 1, rating));
            state.Stability.Should().BeInRange(bounds["sMin"], bounds["sMax"]);
            state.StabilityFast.Should().BeInRange(bounds["stabilityFastMin"], bounds["stabilityFastMax"]);
            state.Difficulty.Should().BeInRange(bounds["dMin"], bounds["dMax"]);
        }
        algorithm.NextInterval(input, 0.1).Should().BeLessThanOrEqualTo(bounds["sMax"]);
        var subday = new Fsrs7Algorithm(Defaults, Bounds(0.5, 0.5, 0.1, 0.25, 4, 6));
        subday.NextInterval(input, 0.1).Should().BeLessThanOrEqualTo(0.5);
    }

    [Fact]
    public void ExposesRetrievabilityAndItsElapsedDayDerivativeThroughCurve()
    {
        var algorithm = new Fsrs7Algorithm(Defaults, StandardBounds);
        var state = S(10, 8, 5);
        var curve = algorithm.Curve(1, state);
        const double dt = 0.0001;
        var difference = (algorithm.Curve(1 + dt, state).Retrievability - algorithm.Curve(1 - dt, state).Retrievability) / (2 * dt);
        curve.Retrievability.Should().BeGreaterThan(0);
        curve.Retrievability.Should().BeLessThan(1);
        curve.Derivative.Should().BeLessThan(0);
        // toBeCloseTo(x, 8): |a - b| < 5e-9
        Math.Abs(curve.Derivative - difference).Should().BeLessThan(5e-9);
    }

    [Fact]
    public void ExposesTheFastAndSlowTraceComponentsBehindTheMixture()
    {
        var algorithm = new Fsrs7Algorithm(Defaults, StandardBounds);
        var state = S(10, 8, 5);

        // Both traces start fully recalled; only the mixture carries the epsilon rescale.
        var zero = algorithm.Curve(0, state);
        zero.Fast.Should().Be(1);
        zero.Slow.Should().Be(1);

        foreach (var t in new[] { 0.01, 1, 10 })
        {
            var c = algorithm.Curve(t, state);
            // The fast trace decays first, which is what bends the mixture early on.
            c.Fast.Should().BeLessThan(c.Slow);
            // The mixture is a convex combination, so it sits between the components.
            c.Retrievability.Should().BeGreaterThan(c.Fast);
            c.Retrievability.Should().BeLessThan(c.Slow);
        }
    }

    [Fact]
    public void ReportsMixtureWeightsThatRecombineTheComponentsIntoRetrievability()
    {
        var algorithm = new Fsrs7Algorithm(Defaults, StandardBounds);
        var state = S(10, 8, 5);
        var reference = algorithm.Curve(1, state);

        // Weights come from the memory state alone, so time does not move them.
        foreach (var t in new[] { 0, 0.01, 10, 100 })
        {
            var c = algorithm.Curve(t, state);
            c.FastWeight.Should().Be(reference.FastWeight);
            c.SlowWeight.Should().Be(reference.SlowWeight);
            c.Total.Should().Be(reference.FastWeight + reference.SlowWeight);
            // Undo the epsilon rescale and the blend reproduces retrievability.
            var blended = (c.FastWeight * c.Fast + c.SlowWeight * c.Slow) / c.Total;
            Math.Abs(blended * (1 - 2e-5) + 1e-5 - c.Retrievability).Should().BeLessThan(5e-13);
        }
    }
}

/// <summary>The <c>public FSRS-7 curve</c> block of <c>algorithm.spec.ts</c>.</summary>
public class Fsrs7PublicCurveTests
{
    private static readonly IReadOnlyList<double> Defaults = Fsrs7Constants.DefaultWeights;
    private static readonly Fsrs7State State = new(10, 8, 5);

    // The upstream test spies on the instance method to prove independence; here we assert the
    // static function evaluates on its own.
    [Fact]
    public void EvaluatesIndependentlyOfTheAlgorithmInstanceMethod() =>
        Fsrs7Algorithm.Curve(Defaults, 1, State).Retrievability.Should().BeGreaterThan(0);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(0.01)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(36500)]
    public void MatchesTheAlgorithmAtDays(double days)
    {
        var algorithm = new Fsrs7Algorithm(Defaults, Fsrs7Constants.ModelBounds);
        Fsrs7Algorithm.Curve(Defaults, days, State).Should().Be(algorithm.Curve(days, State));
    }

    [Fact]
    public void HonorsCustomWeightsAndExposesAConsistentDerivativeAndMixture()
    {
        var weights = Defaults.Select((w, i) => i == 27 ? w * 2 : w).ToArray();
        var result = Fsrs7Algorithm.Curve(weights, 1, State);
        result.Should().NotBe(Fsrs7Algorithm.Curve(Defaults, 1, State));
        const double dt = 0.0001;
        var difference = (Fsrs7Algorithm.Curve(weights, 1 + dt, State).Retrievability
            - Fsrs7Algorithm.Curve(weights, 1 - dt, State).Retrievability) / (2 * dt);
        Math.Abs(result.Derivative - difference).Should().BeLessThan(5e-9);
        var blended = (result.FastWeight * result.Fast + result.SlowWeight * result.Slow)
            / (result.FastWeight + result.SlowWeight) * (1 - 2e-5) + 1e-5;
        Math.Abs(result.Retrievability - blended).Should().BeLessThan(5e-13);
        Fsrs7Algorithm.Curve(weights, -1, State).Should().Be(Fsrs7Algorithm.Curve(weights, 0, State));
    }

    [Fact]
    public void LeavesWeightValidationToTheCaller()
    {
        foreach (var weights in new IReadOnlyList<double>[] { [], Enumerable.Repeat(double.NaN, 34).ToArray() })
        {
            var act = () => Fsrs7Algorithm.Curve(weights, 1, State);
            act.Should().NotThrow();
        }
    }
}
