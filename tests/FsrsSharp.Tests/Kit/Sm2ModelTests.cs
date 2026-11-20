using AwesomeAssertions;
using FsrsSharp.Kit;
using Xunit;

namespace FsrsSharp.Tests.Kit;

/// <summary>Port of srs-kit <c>model/sm2.spec.ts</c> against the test helper model (also validates the helper).</summary>
public class Sm2ModelTests
{
    private static readonly IModelCore<Sm2State> Core = Sm2Model.Instance.Create(Sm2Model.DefaultConfig);

    private static Sm2State Step(Sm2State? state, Rating rating, double elapsed) =>
        Core.Step(new ModelStepInput<Sm2State>(state, rating, elapsed));

    [Fact]
    public void ExposesModelDefinition()
    {
        Sm2Model.Instance.Name.Should().Be("sm2");
        Sm2Model.Instance.ConfigKeys.Should().Equal(Sm2Model.Weights);
        Core.Config.Should().BeEquivalentTo(new Sm2Config(Sm2Model.DefaultWeights));
        Core.Bounds["iMin"].Should().Be(0.01);
    }

    [Fact]
    public void InitializesWithFirstIntervalAndDefaultEaseFactor()
    {
        var state = Step(null, Rating.Good, 0);
        state.Interval.Should().Be(Sm2Model.DefaultWeights[0]);
        state.ReviewStep.Should().Be(1);
        state.EaseFactor.Should().BeGreaterThan(0);
    }

    [Fact]
    public void UsesSecondIntervalOnSecondSuccessfulReview()
    {
        var second = Step(Step(null, Rating.Good, 0), Rating.Good, 1);
        second.Interval.Should().Be(Sm2Model.DefaultWeights[1]);
        second.ReviewStep.Should().Be(2);
    }

    [Fact]
    public void MultipliesIntervalByEaseFactorFromThirdReviewOnward()
    {
        var second = Step(Step(null, Rating.Good, 0), Rating.Good, 1);
        var third = Step(second, Rating.Good, 6);
        third.Interval.Should().Be(second.Interval * second.EaseFactor);
        third.ReviewStep.Should().Be(3);
    }

    [Fact]
    public void ResetsReviewStepToOneOnAgain()
    {
        var second = Step(Step(null, Rating.Good, 0), Rating.Good, 1);
        var lapse = Step(second, Rating.Again, 6);
        lapse.ReviewStep.Should().Be(1);
        lapse.Interval.Should().Be(Sm2Model.DefaultWeights[0]);
    }

    [Fact]
    public void ClampsEaseFactorAndIntervalWithinBounds()
    {
        var state = Step(null, Rating.Again, 0);
        for (var i = 0; i < 50; i++) state = Step(state, Rating.Again, 0);
        state.EaseFactor.Should().Be(Sm2Model.Bounds["eMin"]);

        state = Step(null, Rating.Easy, 0);
        for (var i = 0; i < 100; i++) state = Step(state, Rating.Easy, state.Interval);
        state.Interval.Should().BeLessThanOrEqualTo(Sm2Model.Bounds["iMax"]);
    }

    [Fact]
    public void ComputesForgettingCurveAndNextInterval()
    {
        var state = new Sm2State(10, 2.5, 3);
        Core.ForgettingCurve(state, 10).Should().BeApproximately(0.9, 1e-8);
        Core.ForgettingCurve(state, 0).Should().BeApproximately(1.0, 1e-8);
        Core.NextInterval(state, 0.9).Should().Be(10);
    }

    [Fact]
    public void ReplaysHistoryViaForward()
    {
        var states = Core.Forward([new(Rating.Good, 0), new(Rating.Good, 1), new(Rating.Good, 6)]);
        states.Select(s => s.ReviewStep).Should().Equal(1, 2, 3);
        states[2].Should().Be(Step(Step(Step(null, Rating.Good, 0), Rating.Good, 1), Rating.Good, 6));
    }

    [Fact]
    public void ValidatesConfigAndProvidesDefaultMemoryState()
    {
        var act = () => Sm2Model.Instance.Create(new FieldBag.Builder { { Sm2Model.Weights, (IReadOnlyList<double>)[1, 2, 3] } }.ToFieldBag());
        act.Should().Throw<FsrsValidationException>();
        Sm2Model.Instance.DefaultMemoryState(Sm2Model.DefaultConfig).Should().Be(new Sm2State(0, Sm2Model.DefaultWeights[2], 0));
    }
}
