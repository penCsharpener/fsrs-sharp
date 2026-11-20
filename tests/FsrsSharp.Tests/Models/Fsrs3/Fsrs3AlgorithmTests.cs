using System.Globalization;
using AwesomeAssertions;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs3;
using Xunit;
using static FsrsSharp.Tests.Models.ModelTestHelpers;

namespace FsrsSharp.Tests.Models.Fsrs3;

/// <summary>Port of <c>models/fsrs-3/algorithm.spec.ts</c>.</summary>
public class Fsrs3AlgorithmTests
{
    private static readonly IReadOnlyList<double> Weights = Fsrs3Constants.DefaultWeights;
    private static readonly Fsrs3Algorithm Algorithm = new(Weights, Fsrs3Constants.ModelBounds);

    private static (string Stability, string Difficulty) FormatState(FsrsState state) =>
        (state.Stability.ToString("F8", CultureInfo.InvariantCulture), state.Difficulty.ToString("F8", CultureInfo.InvariantCulture));

    [Fact]
    public void UsesTheFsrs3ForgettingCurve()
    {
        // Expected values source: srs-benchmark/models/fsrs_v3.py Python reference at 70cc4387f573ff20b13ac9c106333a335c8a4cb8.
        new double[] { 0, 1, 2, 3 }.Select(t => Fsrs3Algorithm.ComputeForgettingCurve(t, 1))
            .Should().Equal(1, 0.9, 0.81, 0.729);
    }

    [Fact]
    public void CalculatesIntervalsThroughTheFsrs3IntervalModifier()
    {
        double[] requestRetentions = [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0];
        var intervals = requestRetentions.Select(r => Algorithm.NextInterval(1, r));

        intervals.Should().Equal(22, 15, 11, 9, 7, 5, 3, 2, 1, 1);
        Algorithm.NextInterval(6.1307, 0.9).Should().Be(6);
        var act = () => Algorithm.NextInterval(6.1307, 0);
        act.Should().Throw<FsrsValidationException>().WithMessage("Desired retention rate should be in the range (0,1]");
    }

    [Fact]
    public void InitializesTheFirstReviewStateFromFsrs3Weights()
    {
        var tooFew = () => new Fsrs3Algorithm(Weights.Take(12).ToArray(), Fsrs3Constants.ModelBounds);
        tooFew.Should().Throw<FsrsValidationException>().WithMessage("FSRS3Algorithm requires exactly 13 weights*");
        CloseArray(
            GradeList.Select(r => Algorithm.InitStability(r)).ToArray(),
            [Weights[0], Weights[0] + Weights[1], Weights[0] + 2 * Weights[1], Weights[0] + 3 * Weights[1]]);

        var lowerClamped = new Fsrs3Algorithm([double.NegativeInfinity, .. Weights.Skip(1)], Fsrs3Constants.ModelBounds);
        var upperClamped = new Fsrs3Algorithm([double.PositiveInfinity, .. Weights.Skip(1)], Fsrs3Constants.ModelBounds);
        lowerClamped.InitStability(Rating.Again).Should().Be(Fsrs3Constants.ModelBounds["sMin"]);
        upperClamped.InitStability(Rating.Again).Should().Be(Fsrs3Constants.ModelBounds["sMax"]);
        CloseArray(
            GradeList.Select(r => Algorithm.InitDifficulty(r)).ToArray(),
            [Weights[2] - 2 * Weights[3], Weights[2] - Weights[3], Weights[2], Weights[2] + Weights[3]]);
    }

    [Fact]
    public void MatchesThePythonReferenceDifficultyUpdateResultsTo4Decimals()
    {
        CloseArray(
            GradeList.Select(r => 5 + Weights[4] * ((int)r - 3)).ToArray(),
            [5 - 2 * Weights[4], 5 - Weights[4], 5, 5 + Weights[4]]);
        CloseArray(
            GradeList.Select(r => Algorithm.NextDifficulty(5, r)).ToArray(),
            [7.43428395, 6.21292183, 4.99155971, 3.77019759],
            4);
    }

    [Fact]
    public void MatchesThePythonReferenceStabilityUpdateResultsTo4Decimals()
    {
        double[] stability = [5, 5, 5, 5];
        double[] difficulty = [1, 2, 3, 4];
        double[] retention = [0.9, 0.8, 0.7, 0.6];
        var recall = GradeList.Select((_, i) => Algorithm.NextRecallStability(difficulty[i], stability[i], retention[i])).ToArray();
        var forget = GradeList.Select((_, i) => Algorithm.NextForgetStability(difficulty[i], stability[i], retention[i])).ToArray();
        var nextStability = GradeList.Select((r, i) =>
            Algorithm.NextState(new FsrsState(stability[i], difficulty[i]), 0, r, retention[i]).Stability).ToArray();

        CloseArray(recall, [29.33890984, 51.27167832, 70.2236555, 85.52142222], 4);
        CloseArray(forget, [5.41089414, 4.61822464, 4.4010631, 4.38774907], 4);
        CloseArray(nextStability, [3.2899155, 44.15189718, 69.35813996, 99.00878817], 4);
    }

    [Fact]
    public void MatchesThePythonReferenceDefaultMemoryStateProgressionTo8Decimals()
    {
        Rating[] ratings = [Rating.Again, Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good];
        double[] intervals = [0, 0, 1, 3, 8, 21];
        var states = new List<FsrsState>();
        FsrsState? state = null;

        for (var i = 0; i < ratings.Length; i++)
        {
            state = Algorithm.NextState(state, intervals[i], ratings[i]);
            states.Add(state);
        }

        states.Select(FormatState).Should().Equal(
            ("0.96050000", "7.23610000"),
            ("0.96050000", "7.09953118"),
            ("3.54519473", "6.97078775"),
            ("9.96540903", "6.84942132"),
            ("24.76429562", "6.73500919"),
            ("58.96783581", "6.62715287"));
    }

    [Fact]
    public void ValidatesNextStateInputs()
    {
        Algorithm.NextState(new FsrsState(5, 5), 1, Rating.Manual).Should().Be(new FsrsState(5, 5));
        var negative = () => Algorithm.NextState(null, -1, Rating.Good);
        negative.Should().Throw<FsrsValidationException>().WithMessage("Invalid delta_t \"-1\"");
        var badGrade = () => Algorithm.NextState(null, 0, (Rating)5);
        badGrade.Should().Throw<FsrsValidationException>().WithMessage("Invalid grade \"5\"");
        var badState = () => Algorithm.NextState(new FsrsState(0.1, 0.5), 1, Rating.Good);
        badState.Should().Throw<FsrsValidationException>().WithMessage("Invalid memory state { difficulty: 0.5, stability: 0.1 }");
    }
}
