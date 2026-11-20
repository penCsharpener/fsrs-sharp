using AwesomeAssertions;
using Xunit;

namespace FsrsSharp.Tests;

/// <summary>Port of <c>strategies/learning-steps.test.ts</c>. Learning steps are the part of the schedule that
/// is *configuration* rather than algorithm — the one thing a host application routinely changes — so what
/// each step table produces for each grade is pinned exactly, both at the strategy level and once it's wired
/// through <see cref="Fsrs.Repeat"/>.</summary>
public class LearningStepsStrategyTests
{
    private static readonly DateTime Now = TestParameters.Epoch;

    private static IReadOnlyDictionary<Rating, LearningStepInfo> Steps(
        FsrsParametersInput input, State state, int currentStep) =>
        Strategies.DefaultLearningSteps(Defaults.GeneratorParameters(input), state, currentStep);

    [Theory]
    [InlineData("1m", 1)]
    [InlineData("10m", 10)]
    [InlineData("1h", 60)]
    [InlineData("1d", 1440)]
    [InlineData("0m", 0)]
    public void StepUnitToMinutes_ShouldParseEveryUnit(string step, double expected)
    {
        Strategies.StepUnitToMinutes(step).Should().Be(expected);
    }

    [Theory]
    [InlineData("sd")]
    [InlineData("2g")]
    [InlineData("")]
    [InlineData("-1m")]
    public void StepUnitToMinutes_ShouldRejectAMalformedStep(string step)
    {
        var parse = () => Strategies.StepUnitToMinutes(step);

        parse.Should().Throw<FsrsValidationException>();
    }

    [Fact]
    public void LearningSteps_ShouldNotOfferGood_WhenTheNextStepIsZero()
    {
        // A zero-minute step is indistinguishable from "no step", so Good has nowhere to advance to.
        var result = Steps(new FsrsParametersInput { LearningSteps = ["1m", "0m"] }, State.Learning, 0);

        result.Should().NotContainKey(Rating.Good);
    }

    [Theory]
    [InlineData(State.New)]
    [InlineData(State.Learning)]
    public void LearningSteps_ShouldOfferAllThree_OnTheFirstOfTwoSteps(State state)
    {
        var result = Steps(new FsrsParametersInput { LearningSteps = ["1m", "10m"] }, state, 0);

        result.Should().HaveCount(3);
        result[Rating.Again].Should().Be(new LearningStepInfo(1, 0));
        // Hard sits halfway between this step and the next: (1 + 10) / 2, rounded.
        result[Rating.Hard].Should().Be(new LearningStepInfo(6, 0));
        result[Rating.Good].Should().Be(new LearningStepInfo(10, 1));
    }

    [Fact]
    public void LearningSteps_ShouldDropGood_OnTheLastOfTwoSteps()
    {
        var result = Steps(new FsrsParametersInput { LearningSteps = ["1m", "10m"] }, State.Learning, 1);

        result.Should().HaveCount(2);
        result[Rating.Again].Should().Be(new LearningStepInfo(1, 0));
        result[Rating.Hard].Should().Be(new LearningStepInfo(6, 1));
    }

    [Theory]
    [InlineData(State.New)]
    [InlineData(State.Learning)]
    public void LearningSteps_ShouldFallBackToOneAndAHalf_ForASingleStep(State state)
    {
        var result = Steps(new FsrsParametersInput { LearningSteps = ["1m"] }, state, 0);

        result.Should().HaveCount(2);
        result[Rating.Again].Should().Be(new LearningStepInfo(1, 0));
        result[Rating.Hard].Should().Be(new LearningStepInfo(2, 0)); // round(1 * 1.5)
    }

    [Fact]
    public void LearningSteps_ShouldBeExhausted_PastTheLastStep()
    {
        Steps(new FsrsParametersInput { LearningSteps = ["1m"] }, State.Learning, 1).Should().BeEmpty();
    }

    [Fact]
    public void RelearningSteps_ShouldOnlyOfferAgain_FromReview()
    {
        // A card still in Review hasn't lapsed yet; the only way into relearning is Again.
        var result = Steps(new FsrsParametersInput { RelearningSteps = ["10m"] }, State.Review, 0);

        result.Should().HaveCount(1);
        result[Rating.Again].Should().Be(new LearningStepInfo(10, 0));
    }

    [Fact]
    public void RelearningSteps_ShouldOfferAgainAndHard_ForASingleStep()
    {
        var result = Steps(new FsrsParametersInput { RelearningSteps = ["10m"] }, State.Relearning, 0);

        result.Should().HaveCount(2);
        result[Rating.Again].Should().Be(new LearningStepInfo(10, 0));
        result[Rating.Hard].Should().Be(new LearningStepInfo(15, 0)); // round(10 * 1.5)
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void RelearningSteps_ShouldBeExhausted_PastTheLastStep(int currentStep)
    {
        Steps(new FsrsParametersInput { RelearningSteps = ["10m"] }, State.Relearning, currentStep)
            .Should().BeEmpty();
    }

    [Fact]
    public void RelearningSteps_ShouldWalkTwoSteps()
    {
        var input = new FsrsParametersInput { RelearningSteps = ["10m", "20m"] };

        var fromReview = Steps(input, State.Review, 0);
        fromReview.Should().HaveCount(1);
        fromReview[Rating.Again].Should().Be(new LearningStepInfo(10, 0));

        var first = Steps(input, State.Relearning, 0);
        first.Should().HaveCount(3);
        first[Rating.Again].Should().Be(new LearningStepInfo(10, 0));
        first[Rating.Hard].Should().Be(new LearningStepInfo(15, 0));
        first[Rating.Good].Should().Be(new LearningStepInfo(20, 1));

        var second = Steps(input, State.Relearning, 1);
        second.Should().HaveCount(2);
        second[Rating.Again].Should().Be(new LearningStepInfo(10, 0));
        second[Rating.Hard].Should().Be(new LearningStepInfo(15, 1));

        Steps(input, State.Relearning, 2).Should().BeEmpty();
    }

    [Fact]
    public void FirstLearningStep_ShouldScheduleInMinutes_ExceptForEasy()
    {
        var f = new Fsrs(new FsrsParametersInput
        {
            LearningSteps = ["1m", "10m"],
            RelearningSteps = ["10m"],
        });
        var card = Defaults.CreateEmptyCard(Now);

        var record = f.Repeat(card, Now);

        record[Rating.Again].Card.LearningSteps.Should().Be(0);
        (record[Rating.Again].Card.Due - Now).Should().Be(TimeSpan.FromMinutes(1));

        record[Rating.Hard].Card.LearningSteps.Should().Be(0);
        (record[Rating.Hard].Card.Due - Now).Should().Be(TimeSpan.FromMinutes(6));

        record[Rating.Good].Card.LearningSteps.Should().Be(1);
        (record[Rating.Good].Card.Due - Now).Should().Be(TimeSpan.FromMinutes(10));

        // Easy skips the steps entirely and gets a real interval, derived from w3.
        record[Rating.Easy].Card.LearningSteps.Should().Be(0);
        Helpers.DateDiffInDays(Now, record[Rating.Easy].Card.Due)
            .Should().Be((int)Math.Floor(Constants.DefaultW[3]));
    }

    private static readonly LearningStepsStrategy HardcodedSteps = (_, _, currentStep) => currentStep >= 1
        ? new Dictionary<Rating, LearningStepInfo>
        {
            [Rating.Again] = new(5, 1),
            [Rating.Hard] = new(10, 1),
        }
        : new Dictionary<Rating, LearningStepInfo>
        {
            [Rating.Again] = new(1, 1),
            [Rating.Hard] = new(5, 1),
            [Rating.Good] = new(10, 1),
        };

    [Fact]
    public void CustomLearningStepsStrategy_ShouldReplaceTheDefault()
    {
        var f = new Fsrs().UseLearningStepsStrategy(HardcodedSteps);
        var now = Now;
        var card = Defaults.CreateEmptyCard(now);

        var record = f.Repeat(card, now);
        (record[Rating.Again].Card.Due - now).Should().Be(TimeSpan.FromMinutes(1));
        (record[Rating.Hard].Card.Due - now).Should().Be(TimeSpan.FromMinutes(5));
        (record[Rating.Good].Card.Due - now).Should().Be(TimeSpan.FromMinutes(10));
        record[Rating.Easy].Card.LearningSteps.Should().Be(0);

        card = record[Rating.Good].Card;
        now = card.Due;
        record = f.Repeat(card, now);
        (record[Rating.Again].Card.Due - now).Should().Be(TimeSpan.FromMinutes(5));
        (record[Rating.Hard].Card.Due - now).Should().Be(TimeSpan.FromMinutes(10));
        // Good has no step left in the hardcoded table, so it graduates to a day-scale interval.
        record[Rating.Good].Card.LearningSteps.Should().Be(0);
        (record[Rating.Good].Card.Due - now).Should().BeGreaterThanOrEqualTo(TimeSpan.FromDays(1));
        (record[Rating.Easy].Card.Due - now).Should().BeGreaterThanOrEqualTo(TimeSpan.FromDays(1));
    }

    [Fact]
    public void CustomLearningStepsStrategy_ShouldReportScheduledDays_WhenAStepExceedsADay()
    {
        var f = new Fsrs().UseLearningStepsStrategy((_, _, _) => new Dictionary<Rating, LearningStepInfo>
        {
            [Rating.Again] = new(5, 1),
            [Rating.Hard] = new(1440, 1),
            [Rating.Good] = new(1440 * 3, 1),
        });

        var record = f.Repeat(Defaults.CreateEmptyCard(Now), Now);

        (record[Rating.Again].Card.Due - Now).Should().Be(TimeSpan.FromMinutes(5));
        record[Rating.Again].Card.ScheduledDays.Should().Be(0);

        (record[Rating.Hard].Card.Due - Now).Should().Be(TimeSpan.FromDays(1));
        record[Rating.Hard].Card.ScheduledDays.Should().Be(1);

        (record[Rating.Good].Card.Due - Now).Should().Be(TimeSpan.FromDays(3));
        record[Rating.Good].Card.ScheduledDays.Should().Be(3);
    }

    [Fact]
    public void UseLearningStepsStrategy_ShouldRestoreTheDefault_WhenPassedNull()
    {
        var f = new Fsrs().UseLearningStepsStrategy(HardcodedSteps);
        f.Repeat(Defaults.CreateEmptyCard(Now), Now)[Rating.Hard].Card.Due
            .Should().Be(Now.AddMinutes(5));

        f.UseLearningStepsStrategy(null);

        f.Repeat(Defaults.CreateEmptyCard(Now), Now)[Rating.Hard].Card.Due
            .Should().Be(Now.AddMinutes(6));
    }
}
