using AwesomeAssertions;
using FsrsSharp.Legacy;
using FsrsSharp.Middlewares;
using Xunit;

namespace FsrsSharp.Tests.Legacy.Middlewares.LearningSteps;

/// <summary>Port of <c>legacy/__tests__/middlewares/learning-steps/legacy.spec.ts</c>.</summary>
public class LearningStepsLegacyTests
{
    private static readonly DateTime Now = TestParameters.Utc(2022, 12, 29, 12, 30);

    private static LearningStepsResolver Resolver(Func<int, Dictionary<Rating, LearningStepSchedule>> byStep) =>
        (_, _, learningStep) =>
        {
            var result = new LearningStepsResult();
            foreach (var (rating, schedule) in byStep(learningStep)) result[rating] = schedule;
            return result;
        };

    [Fact]
    public void SchedulesTheFirstLearningStepForEveryGrade()
    {
        var scheduler = new Fsrs(new FsrsParametersInput { LearningSteps = ["1m", "10m"], RelearningSteps = ["10m"] });
        var record = scheduler.Repeat(Defaults.CreateEmptyCard(Now), Now);

        record[Rating.Again].Card.LearningSteps.Should().Be(0);
        (record[Rating.Again].Card.Due - Now).Should().Be(TimeSpan.FromMilliseconds(60_000));
        record[Rating.Hard].Card.LearningSteps.Should().Be(0);
        (record[Rating.Hard].Card.Due - Now).Should().Be(TimeSpan.FromMilliseconds(5.5 * 60_000));
        record[Rating.Good].Card.LearningSteps.Should().Be(1);
        (record[Rating.Good].Card.Due - Now).Should().Be(TimeSpan.FromMilliseconds(10 * 60_000));
        record[Rating.Easy].Card.LearningSteps.Should().Be(0);
        LegacyHelpers.DateDiffInDays(Now, record[Rating.Easy].Card.Due).Should().Be((int)Math.Floor(Constants.DefaultW[3]));
    }

    [Fact]
    public void KeepsTheLegacyCustomLearningStepExtensionAvailable()
    {
        var calls = 0;
        var learningSteps = Resolver(learningStep =>
        {
            calls++;
            return learningStep >= 1
                ? new()
                {
                    [Rating.Again] = new(5, 1),
                    [Rating.Hard] = new(10, 1),
                }
                : new()
                {
                    [Rating.Again] = new(1, 1),
                    [Rating.Hard] = new(5, 1),
                    [Rating.Good] = new(10, 1),
                };
        });
        var scheduler = new Fsrs().UseLearningStepsStrategy(learningSteps);
        var first = scheduler.Repeat(Defaults.CreateEmptyCard(Now), Now);

        first[Rating.Good].Card.LearningSteps.Should().Be(1);
        (first[Rating.Good].Card.Due - Now).Should().Be(TimeSpan.FromMinutes(10));
        calls.Should().Be(1);

        var nextNow = first[Rating.Good].Card.Due;
        var second = scheduler.Repeat(first[Rating.Good].Card, nextNow);
        second[Rating.Again].Card.LearningSteps.Should().Be(1);
        (second[Rating.Again].Card.Due - nextNow).Should().Be(TimeSpan.FromMinutes(5));
        second[Rating.Good].Card.State.Should().Be(State.Review);
        calls.Should().Be(2);

        scheduler.LearningStepsStrategy.Should().BeSameAs(learningSteps);
        scheduler.UseLearningStepsStrategy(null);
        scheduler.LearningStepsStrategy.Should().BeNull();
    }

    [Fact]
    public void KeepsMinutePrecisionWhileExposingWholeScheduledDays()
    {
        var learningSteps = Resolver(_ => new()
        {
            [Rating.Again] = new(5, 1),
            [Rating.Hard] = new(1440, 1),
            [Rating.Good] = new(4320, 1),
        });
        var scheduler = new Fsrs().UseLearningStepsStrategy(learningSteps);
        var record = scheduler.Repeat(Defaults.CreateEmptyCard(Now), Now);

        record[Rating.Again].Card.ScheduledDays.Should().Be(0);
        record[Rating.Hard].Card.ScheduledDays.Should().Be(1);
        (record[Rating.Hard].Card.Due - Now).Should().Be(TimeSpan.FromMinutes(1440));
        record[Rating.Good].Card.ScheduledDays.Should().Be(3);
        (record[Rating.Good].Card.Due - Now).Should().Be(TimeSpan.FromMinutes(4320));
    }

    [Fact]
    public void PreservesSecondPrecisionInDecimalLearningSteps()
    {
        var scheduler = new Fsrs(new FsrsParametersInput { LearningSteps = ["1.5m"] });
        var result = scheduler.Next(Defaults.CreateEmptyCard(Now), Now, Rating.Again);

        (result.Card.Due - Now).Should().Be(TimeSpan.FromMilliseconds(1.5 * 60_000));
        result.Card.ScheduledDays.Should().Be(0);
    }

    [Fact]
    public void StillAcceptsGeneratedParametersInTheLegacyFacade()
    {
        var parameters = Defaults.GeneratorParameters(new FsrsParametersInput { LearningSteps = ["2m"] });
        var scheduler = new Fsrs(new FsrsParametersInput
        {
            RequestRetention = parameters.RequestRetention,
            MaximumInterval = parameters.MaximumInterval,
            W = parameters.W,
            EnableFuzz = parameters.EnableFuzz,
            EnableShortTerm = parameters.EnableShortTerm,
            LearningSteps = parameters.LearningSteps,
            RelearningSteps = parameters.RelearningSteps,
        });

        (scheduler.Next(Defaults.CreateEmptyCard(Now), Now, Rating.Again).Card.Due - Now)
            .Should().Be(TimeSpan.FromMinutes(2));
    }
}
