using AwesomeAssertions;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs4Dot5;
using Xunit;
using static FsrsSharp.Tests.Models.ModelTestHelpers;

namespace FsrsSharp.Tests.Models.Fsrs4Dot5;

/// <summary>Port of <c>models/fsrs-4dot5/algorithm.spec.ts</c>.</summary>
public class Fsrs4Dot5AlgorithmTests
{
    private static readonly IReadOnlyList<double> Weights = Fsrs4Dot5Constants.DefaultWeights;
    private static readonly Fsrs4Dot5Algorithm Algorithm = new(Weights, Fsrs4Dot5Constants.ModelBounds);

    private static FsrsState RoundState(FsrsState state) => new(ToFixed(state.Stability, 4), ToFixed(state.Difficulty, 4));

    [Fact]
    public void UsesTheFsrs45ForgettingCurveConstants()
    {
        Fsrs4Dot5Constants.Decay.Should().Be(0.5);
        Fsrs4Dot5Constants.Factor.Should().Be(19.0 / 81.0);
        new double[] { 0, 1, 2, 3 }.Select(t => Fsrs4Dot5Algorithm.ComputeForgettingCurve(t, 1))
            .Should().Equal(1, 0.9, 0.82502865, 0.76613088);
        // https://github.com/open-spaced-repetition/fsrs-rs/blob/2c8c951d3f5eb7637d19ddea55ad79f34e56d683/src/model.rs#L253-L263
        (double T, double S)[] cases = [(0, 1), (1, 2), (2, 3), (3, 4), (4, 4), (5, 2)];
        CloseArray(
            cases.Select(c => Fsrs4Dot5Algorithm.ComputeForgettingCurve(c.T, c.S)).ToArray(),
            [1, 0.946059, 0.9299294, 0.9221679, 0.90000004, 0.79394597]);
    }

    [Fact]
    public void CalculatesIntervalsThroughTheFsrs45IntervalModifier()
    {
        Algorithm.NextInterval(13.8206, 0.9).Should().Be(14);
        var act = () => Algorithm.NextInterval(13.8206, 0);
        act.Should().Throw<FsrsValidationException>().WithMessage("Desired retention rate should be in the range (0,1]");
    }

    [Fact]
    public void InitializesTheFirstReviewStateFromFsrs45Weights()
    {
        var tooFew = () => new Fsrs4Dot5Algorithm(Weights.Take(16).ToArray(), Fsrs4Dot5Constants.ModelBounds);
        tooFew.Should().Throw<FsrsValidationException>().WithMessage("FSRS4Dot5Algorithm requires exactly 17 weights*");
        GradeList.Select(r => Algorithm.InitStability(r)).Should().Equal(Weights[0], Weights[1], Weights[2], Weights[3]);

        var clamped = new Fsrs4Dot5Algorithm(
            [double.NegativeInfinity, double.PositiveInfinity, .. Weights.Skip(2)], Fsrs4Dot5Constants.ModelBounds);
        clamped.InitStability(Rating.Again).Should().Be(Fsrs4Dot5Constants.ModelBounds["sMin"]);
        clamped.InitStability(Rating.Hard).Should().Be(Fsrs4Dot5Constants.ModelBounds["sMax"]);
        CloseArray(
            GradeList.Select(r => Algorithm.InitDifficulty(r)).ToArray(),
            [Weights[4] + 2 * Weights[5], Weights[4] + Weights[5], Weights[4], Weights[4] - Weights[5]]);
    }

    [Fact]
    public void MatchesFsrsRsDifficultyUpdateResultsTo4Decimals()
    {
        // https://github.com/open-spaced-repetition/fsrs-rs/blob/2c8c951d3f5eb7637d19ddea55ad79f34e56d683/src/model.rs#L279-L299
        CloseArray(
            GradeList.Select(r => 5 - Weights[6] * ((int)r - 3)).ToArray(),
            [5 + 2 * Weights[6], 5 + Weights[6], 5, 5 - Weights[6]]);
        CloseArray(GradeList.Select(r => Algorithm.NextDifficulty(5, r)).ToArray(), [6.744371, 5.8746934, 5.005016, 4.1353383]);
    }

    [Fact]
    public void MatchesFsrsRsStabilityUpdateResultsTo4Decimals()
    {
        // https://github.com/open-spaced-repetition/fsrs-rs/blob/2c8c951d3f5eb7637d19ddea55ad79f34e56d683/src/model.rs#L301-L331
        double[] stability = [5, 5, 5, 5];
        double[] difficulty = [1, 2, 3, 4];
        double[] retention = [0.9, 0.8, 0.7, 0.6];
        var recall = GradeList.Select((r, i) => Algorithm.NextRecallStability(difficulty[i], stability[i], retention[i], r)).ToArray();
        var forget = GradeList.Select((_, i) => Algorithm.NextForgetStability(difficulty[i], stability[i], retention[i])).ToArray();
        var nextStability = GradeList.Select((r, i) =>
            Algorithm.NextState(new FsrsState(stability[i], difficulty[i]), 0, r, retention[i]).Stability).ToArray();

        CloseArray(recall, [27.980768, 14.916422, 66.45966, 222.94603], 4);
        CloseArray(forget, [1.9482934, 2.161251, 2.4528089, 2.8098207], 4);
        CloseArray(nextStability, [1.9482934, 14.916422, 66.45966, 222.94603], 4);
    }

    [Fact]
    public void MatchesFsrsRsForwardResultTo4Decimals()
    {
        // https://github.com/open-spaced-repetition/fsrs-rs/blob/2c8c951d3f5eb7637d19ddea55ad79f34e56d683/src/model.rs#L317-L337
        Rating[] ratings = [Rating.Again, Rating.Hard, Rating.Good, Rating.Easy, Rating.Again, Rating.Hard];
        double[] intervals = [1, 1, 1, 1, 2, 2];
        var states = ratings.Select((r, i) => Algorithm.NextState(Algorithm.NextState(null, 0, r), intervals[i], r)).ToArray();

        CloseArray(states.Select(s => s.Stability).ToArray(), [0.3273601, 1.9898274, 6.7310715, 22.790451, 0.3884622, 2.4991434], 4);
        CloseArray(states.Select(s => s.Difficulty).ToArray(), [9.284508, 7.2231536, 5.1618, 3.1004462, 9.284508, 7.2231536], 4);
    }

    [Fact]
    public void CapsPostLapseStabilityAtThePreviousStability() =>
        Algorithm.NextState(new FsrsState(0.1, 1), 1, Rating.Again).Stability.Should().Be(0.1);

    [Fact]
    public void MatchesFsrs45MemoryStateProgression()
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
            new FsrsState(0.4872, 7.6214),
            new FsrsState(0.4872, 7.5452),
            new FsrsState(2.4661, 7.4713),
            new FsrsState(7.7127, 7.3997),
            new FsrsState(20.1432, 7.3303),
            new FsrsState(49.2964, 7.2631));
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
