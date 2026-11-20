using AwesomeAssertions;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs4;
using Xunit;
using static FsrsSharp.Tests.Models.ModelTestHelpers;

namespace FsrsSharp.Tests.Models.Fsrs4;

/// <summary>Port of <c>models/fsrs-4/algorithm.spec.ts</c>.</summary>
public class Fsrs4AlgorithmTests
{
    private static readonly IReadOnlyList<double> Weights = Fsrs4Constants.DefaultWeights;
    private static readonly Fsrs4Algorithm Algorithm = new(Weights, Fsrs4Constants.ModelBounds);

    private static FsrsState RoundState(FsrsState state) => new(ToFixed(state.Stability, 4), ToFixed(state.Difficulty, 4));

    [Fact]
    public void UsesTheFsrs4ForgettingCurve()
    {
        // Expected values source: srs-benchmark/models/fsrs_v4.py Python reference at 70cc4387f573ff20b13ac9c106333a335c8a4cb8.
        new double[] { 0, 1, 2, 3 }.Select(t => Fsrs4Algorithm.ComputeForgettingCurve(t, 1))
            .Should().Equal(1, 0.9, 0.81818182, 0.75);
    }

    [Fact]
    public void CalculatesIntervalsThroughTheFsrs4IntervalModifier()
    {
        // Expected values source: fsrs-rs PR #58 test_next_interval result.
        double[] requestRetentions = [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0];
        var intervals = requestRetentions.Select(r => Algorithm.NextInterval(1, r));

        intervals.Should().Equal(81, 36, 21, 14, 9, 6, 4, 2, 1, 1);
        Algorithm.NextInterval(13.8206, 0.9).Should().Be(14);
        var act = () => Algorithm.NextInterval(13.8206, 0);
        act.Should().Throw<FsrsValidationException>().WithMessage("Requested retention rate should be in the range (0,1]");
    }

    [Fact]
    public void InitializesTheFirstReviewStateFromFsrs4Weights()
    {
        var tooFew = () => new Fsrs4Algorithm(Weights.Take(16).ToArray(), Fsrs4Constants.ModelBounds);
        tooFew.Should().Throw<FsrsValidationException>().WithMessage("FSRS4Algorithm requires exactly 17 weights*");
        GradeList.Select(r => Algorithm.InitStability(r)).Should().Equal(Weights[0], Weights[1], Weights[2], Weights[3]);

        var clamped = new Fsrs4Algorithm(
            [double.NegativeInfinity, double.PositiveInfinity, .. Weights.Skip(2)], Fsrs4Constants.ModelBounds);
        clamped.InitStability(Rating.Again).Should().Be(Fsrs4Constants.ModelBounds["sMin"]);
        clamped.InitStability(Rating.Hard).Should().Be(Fsrs4Constants.ModelBounds["sMax"]);
        CloseArray(
            GradeList.Select(r => Algorithm.InitDifficulty(r)).ToArray(),
            [Weights[4] + 2 * Weights[5], Weights[4] + Weights[5], Weights[4], Weights[4] - Weights[5]]);
    }

    [Fact]
    public void MatchesThePythonReferenceDifficultyUpdateResultsTo4Decimals()
    {
        CloseArray(
            GradeList.Select(r => 5 - Weights[6] * ((int)r - 3)).ToArray(),
            [5 + 2 * Weights[6], 5 + Weights[6], 5, 5 - Weights[6]]);
        CloseArray(GradeList.Select(r => Algorithm.NextDifficulty(5, r)).ToArray(), [6.7021008, 5.8507004, 4.9993, 4.1479001], 4);
    }

    [Fact]
    public void MatchesThePythonReferenceStabilityUpdateResultsTo4Decimals()
    {
        double[] stability = [5, 5, 5, 5];
        double[] difficulty = [1, 2, 3, 4];
        double[] retention = [0.9, 0.8, 0.7, 0.6];
        var recall = GradeList.Select((r, i) => Algorithm.NextRecallStability(difficulty[i], stability[i], retention[i], r)).ToArray();
        var forget = GradeList.Select((_, i) => Algorithm.NextForgetStability(difficulty[i], stability[i], retention[i])).ToArray();
        var nextStability = GradeList.Select((r, i) =>
            Algorithm.NextState(new FsrsState(stability[i], difficulty[i]), 0, r, retention[i]).Stability).ToArray();

        CloseArray(recall, [22.454704, 14.560361, 51.155739, 152.6869], 4);
        CloseArray(forget, [2.074517, 2.272933, 2.526406, 2.824732], 4);
        CloseArray(nextStability, [2.074517, 14.560361, 51.155739, 152.6869], 4);
    }

    [Fact]
    public void MatchesTheFsrsRsPr58SchedulerMemoryStateResults()
    {
        // Expected values source: fsrs-rs PR #58 test_memo_state result.
        double[] rsWeights =
        [
            0.81497127, 1.5411042, 4.007436, 9.045982, 4.9264183, 1.039322,
            0.93803364, 0, 1.5530516, 0.10299722, 0.9981442, 2.210701, 0.018248068,
            0.3422524, 1.3384504, 0.22278537, 2.6646678,
        ];
        var algo = new Fsrs4Algorithm(rsWeights, Fsrs4Constants.ModelBounds);
        (Rating Rating, double DeltaT)[] reviews =
        [
            (Rating.Again, 0), (Rating.Good, 1), (Rating.Good, 3), (Rating.Good, 8), (Rating.Good, 21),
        ];
        FsrsState? state = null;

        foreach (var review in reviews) state = algo.NextState(state, review.DeltaT, review.Rating);

        CloseArray([state!.Stability, state.Difficulty], [51.344814, 7.005062], 4);
        var nextState = algo.NextState(new FsrsState(20.925528, 7.005062), 21, Rating.Good);

        CloseArray([nextState.Stability, nextState.Difficulty], [51.344814, 7.005062], 4);
    }

    [Fact]
    public void MatchesThePythonReferenceDefaultMemoryStateProgression()
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

        states.Select(RoundState).Should().Equal(
            new FsrsState(0.4, 6.81),
            new FsrsState(0.4, 6.7912),
            new FsrsState(2.3254, 6.7726),
            new FsrsState(7.1731, 6.7542),
            new FsrsState(18.3727, 6.7359),
            new FsrsState(44.2147, 6.7179));
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
