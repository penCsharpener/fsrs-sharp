using Xunit;
using AwesomeAssertions;
using FsrsSharp.Middlewares;

namespace FsrsSharp.Tests.Middlewares.LearningSteps;

/// <summary>Port of ts-fsrs <c>middlewares/learning-steps/core.spec.ts</c>.</summary>
public class LearningStepsCoreTests
{
    private static LearningStepsConfig Config(string[]? learningSteps = null, string[]? relearningSteps = null) =>
        new(learningSteps ?? [], relearningSteps ?? []);

    private static LearningStepsResult Expected(params (Rating Grade, double Minutes, int NextStep)[] entries)
    {
        var result = new LearningStepsResult();
        foreach (var (grade, minutes, nextStep) in entries) result[grade] = new LearningStepSchedule(minutes, nextStep);
        return result;
    }

    [Theory]
    [InlineData("1m", 1)]
    [InlineData("1h", 60)]
    [InlineData("1d", 1440)]
    [InlineData("1.5m", 1.5)]
    [InlineData("1.5h", 90)]
    [InlineData("0.25d", 360)]
    [InlineData(".5h", 30)]
    [InlineData("1e2m", 100)]
    public void ConvertsStepToMinutes(string step, double expected) =>
        LearningStepsCore.ConvertStepUnitToMinutes(step).Should().Be(expected);

    [Theory]
    [InlineData("m")]
    [InlineData("sd")]
    [InlineData("1..5m")]
    [InlineData("-1m")]
    [InlineData("2g")]
    public void RejectsInvalidStep(string step) =>
        FluentActions.Invoking(() => LearningStepsCore.ConvertStepUnitToMinutes(step)).Should().Throw<FsrsValidationException>();

    [Fact]
    public void RejectsNullAndNonFiniteNumericValues()
    {
        FluentActions.Invoking(() => LearningStepsCore.ConvertStepUnitToMinutes(null!)).Should().Throw<FsrsValidationException>();
        FluentActions.Invoking(() => LearningStepsCore.ConvertStepUnitToMinutes(new string('9', 400) + "m"))
            .Should().Throw<FsrsValidationException>();
    }

    [Theory]
    [InlineData("1e308d")]
    [InlineData("1e308m")]
    public void RejectsArithmeticOverflow(string step) =>
        FluentActions.Invoking(() => LearningStepsCore.ConvertStepUnitToMinutes(step)).Should().Throw<FsrsValidationException>();

    [Fact]
    public void CalculatesATwoStepLearningSchedule()
    {
        var config = Config(["1m", "10m"]);

        LearningStepsCore.CalculateLearningSteps(config, State.New, 0).Should().BeEquivalentTo(
            Expected((Rating.Again, 1, 0), (Rating.Hard, 5.5, 0), (Rating.Good, 10, 1)));
        LearningStepsCore.CalculateLearningSteps(config, State.Learning, 1).Should().BeEquivalentTo(
            Expected((Rating.Again, 1, 0), (Rating.Hard, 5.5, 1)));
    }

    [Fact]
    public void CalculatesASingleLearningStepAndGraduatesAfterIt()
    {
        var config = Config(["1m"]);

        LearningStepsCore.CalculateLearningSteps(config, State.Learning, 0).Should().BeEquivalentTo(
            Expected((Rating.Again, 1, 0), (Rating.Hard, 1.5, 0)));
        LearningStepsCore.CalculateLearningSteps(config, State.Learning, 1).Should().BeEmpty();
    }

    [Fact]
    public void OmitsGoodWhenTheNextStepIsZero() =>
        LearningStepsCore.CalculateLearningSteps(Config(["1m", "0m"]), State.Learning, 0).Should().NotContainKey(Rating.Good);

    [Fact]
    public void ReturnsNoScheduleForEmptyOrOutOfRangeSteps()
    {
        LearningStepsCore.CalculateLearningSteps(Config(), State.New, 0).Should().BeEmpty();
        LearningStepsCore.CalculateLearningSteps(Config([], ["10m"]), State.Relearning, 1).Should().BeEmpty();
        LearningStepsCore.CalculateLearningSteps(Config([], ["10m"]), State.Relearning, int.MaxValue).Should().BeEmpty();
    }

    [Fact]
    public void UsesTheFirstStepForANegativeCurrentStep() =>
        LearningStepsCore.CalculateLearningSteps(Config(["1m"]), State.New, -1).Should().BeEquivalentTo(
            Expected((Rating.Again, 1, 0), (Rating.Hard, 1.5, -1), (Rating.Good, 1, 0)));

    [Fact]
    public void OnlySchedulesAgainWhenAReviewEntersRelearning() =>
        LearningStepsCore.CalculateLearningSteps(Config([], ["10m", "20m"]), State.Review, 0).Should().BeEquivalentTo(
            Expected((Rating.Again, 10, 0)));

    [Fact]
    public void CalculatesRelearningStepsIndependentlyFromLearningSteps()
    {
        var config = Config(["1m"], ["10m", "20m"]);

        LearningStepsCore.CalculateLearningSteps(config, State.Relearning, 0).Should().BeEquivalentTo(
            Expected((Rating.Again, 10, 0), (Rating.Hard, 15, 0), (Rating.Good, 20, 1)));
        LearningStepsCore.CalculateLearningSteps(config, State.Relearning, 1).Should().BeEquivalentTo(
            Expected((Rating.Again, 10, 0), (Rating.Hard, 15, 1)));
    }

    [Fact]
    public void PreservesDecimalIntervalsForAllGrades() =>
        LearningStepsCore.CalculateLearningSteps(Config(["1.5m", "2.5m"]), State.New, 0).Should().BeEquivalentTo(
            Expected((Rating.Again, 1.5, 0), (Rating.Hard, 2, 0), (Rating.Good, 2.5, 1)));
}
