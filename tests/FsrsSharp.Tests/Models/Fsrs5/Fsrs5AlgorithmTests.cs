using AwesomeAssertions;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs5;
using Xunit;

namespace FsrsSharp.Tests.Models.Fsrs5;

/// <summary>Port of <c>models/fsrs-5/algorithm.spec.ts</c>.</summary>
public class Fsrs5AlgorithmTests
{
    private static readonly Fsrs5Algorithm Algorithm = new(Fsrs5Constants.DefaultWeights, true, Fsrs5Constants.ModelBounds);

    [Fact]
    public void UsesTheFsrs5ForgettingCurveConstants()
    {
        Fsrs5Constants.Decay.Should().Be(0.5);
        Fsrs5Constants.Factor.Should().Be(19.0 / 81.0);
        Fsrs5Algorithm.ComputeForgettingCurve(8, 8.2956).Should().Be(0.90306221);
    }

    [Fact]
    public void RejectsAnInvalidWeightCount()
    {
        var act = () => new Fsrs5Algorithm([], true, Fsrs5Constants.ModelBounds);
        act.Should().Throw<FsrsValidationException>().WithMessage("FSRS5Algorithm requires exactly 19 weights*");
    }

    [Fact]
    public void CalculatesIntervalsThroughTheFsrs5IntervalModifier()
    {
        Algorithm.NextInterval(15.69105, 0.9).Should().Be(16);
        Algorithm.NextInterval(15.69105, 0.8).Should().Be(38);
        var act = () => Algorithm.NextInterval(1, 0);
        act.Should().Throw<FsrsValidationException>().WithMessage("Requested retention rate should be in the range (0,1]");
    }

    [Fact]
    public void InitializesTheFirstReviewStateFromFsrs5Weights()
    {
        Rating[] ratings = [Rating.Again, Rating.Hard, Rating.Good, Rating.Easy];
        var states = ratings.Select(r => Algorithm.NextState(null, 0, r)).ToArray();

        states.Select(s => s.Stability).Should().Equal(0.40255, 1.18385, 3.173, 15.69105);
        states.Select(s => s.Difficulty).Should().Equal(7.1949, 6.48830527, 5.28243442, 3.22450159);

        var clamped = new Fsrs5Algorithm(
            [double.NegativeInfinity, double.PositiveInfinity, .. Fsrs5Constants.DefaultWeights.Skip(2)],
            true,
            Fsrs5Constants.ModelBounds);
        clamped.InitStability(Rating.Again).Should().Be(Fsrs5Constants.ModelBounds["sMin"]);
        clamped.InitStability(Rating.Hard).Should().Be(Fsrs5Constants.ModelBounds["sMax"]);
    }

    [Fact]
    public void ValidatesStateInputsAndHandlesEveryReviewRating()
    {
        var state = new FsrsState(10, 5);
        var bounds = Fsrs5Constants.ModelBounds;

        var negativeT = () => Algorithm.NextState(state, -1, Rating.Good);
        negativeT.Should().Throw<FsrsValidationException>().WithMessage("Invalid delta_t \"-1\"");
        var gradeLow = () => Algorithm.NextState(state, 1, (Rating)(-1));
        gradeLow.Should().Throw<FsrsValidationException>().WithMessage("Invalid grade \"-1\"");
        var gradeHigh = () => Algorithm.NextState(state, 1, (Rating)5);
        gradeHigh.Should().Throw<FsrsValidationException>().WithMessage("Invalid grade \"5\"");
        Algorithm.NextState(state, 1, Rating.Manual).Should().Be(state);
        var badState = () => Algorithm.NextState(new FsrsState(10, 0.5), 1, Rating.Good);
        badState.Should().Throw<FsrsValidationException>().WithMessage("Invalid memory state*");

        foreach (var rating in new[] { Rating.Hard, Rating.Easy })
        {
            var next = Algorithm.NextState(state, 1, rating, 0.9);
            next.Difficulty.Should().BeGreaterThanOrEqualTo(bounds["dMin"]);
            next.Stability.Should().BeGreaterThanOrEqualTo(bounds["sMin"]);
        }

        var withoutShortTerm = new Fsrs5Algorithm(Fsrs5Constants.DefaultWeights, false, bounds);
        foreach (var candidate in new[] { Algorithm, withoutShortTerm })
        {
            var next = candidate.NextState(state, 1, Rating.Again, 0.9);
            next.Stability.Should().BeGreaterThanOrEqualTo(bounds["sMin"]);
        }
    }

    [Theory]
    [InlineData(true, 48.26549438, 7.10441712)]
    [InlineData(false, 48.065163, 7.10441712)]
    public void MatchesV471MemoryStateProgression(bool enableShortTerm, double expectedStability, double expectedDifficulty)
    {
        var algorithm = new Fsrs5Algorithm(Fsrs5Constants.DefaultWeights, enableShortTerm, Fsrs5Constants.ModelBounds);
        Rating[] ratings = [Rating.Again, Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good];
        double[] intervals = [0, 0, 1, 3, 8, 21];
        FsrsState? state = null;

        for (var i = 0; i < ratings.Length; i++)
            state = algorithm.NextState(state, intervals[i], ratings[i]);

        state!.Stability.Should().BeApproximately(expectedStability, 0.5e-4);
        state.Difficulty.Should().BeApproximately(expectedDifficulty, 0.5e-4);
    }
}
