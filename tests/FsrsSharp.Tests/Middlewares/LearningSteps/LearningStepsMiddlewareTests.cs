using Xunit;
using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs6;

namespace FsrsSharp.Tests.Middlewares.LearningSteps;

/// <summary>Port of ts-fsrs <c>middlewares/learning-steps/middleware.spec.ts</c>.</summary>
public class LearningStepsMiddlewareTests
{
    private const double Day = 86_400_000;
    private static DateTime Now => MiddlewareTestSchedulers.LegacyNow;

    private sealed record Core(Scheduler<FsrsState, DateTime> Scheduler, SpyModel<FsrsState> Spy);

    private static Core CreateCore(
        bool enableShortTerm = true,
        string[]? learningSteps = null,
        string[]? relearningSteps = null,
        double? graduatingInterval = null,
        bool? allowModelOverride = null,
        params Middleware[] extra)
    {
        learningSteps ??= ["1m", "10m"];
        relearningSteps ??= ["10m"];
        var config = MiddlewareTestSchedulers.Fsrs6Config(enableShortTerm, relearningSteps.Length);
        config.SetRaw("learningSteps", learningSteps);
        config.SetRaw("relearningSteps", relearningSteps);
        if (graduatingInterval is { } g) config.SetRaw("graduatingInterval", g);
        if (allowModelOverride is { } a) config.SetRaw("allowModelOverride", a);
        var spy = new SpyModel<FsrsState>(Fsrs6Model.Instance);
        var scheduler = MiddlewareTestSchedulers.Fsrs6(spy, [LearningStepsMiddleware.Instance, .. extra]).Create(config);
        return new Core(scheduler, spy);
    }

    private static double Delay(ScheduleResult<FsrsState> result, DateTime from) => (result.Card.DueAt - from).TotalMilliseconds;

    private static ScheduleResult<FsrsState> Review(Core core, Card<FsrsState> card, Rating grade, DateTime? now = null) =>
        core.Scheduler.Review(card, grade, now ?? Now);

    [Fact]
    public void RejectsUnsafeRelearningStepCountsAtCreate()
    {
        foreach (var numRelearningSteps in new[] { 0.5, Math.Pow(2, 53) })
        {
            var config = MiddlewareTestSchedulers.Fsrs6Config(true);
            config.SetRaw("numRelearningSteps", numRelearningSteps);
            config.SetRaw("learningSteps", new[] { "1m" });
            config.SetRaw("relearningSteps", new[] { "10m" });
            FluentActions.Invoking(() => MiddlewareTestSchedulers.Fsrs6(LearningStepsMiddleware.Instance).Create(config))
                .Should().Throw<FsrsValidationException>();
        }
    }

    [Fact]
    public void RejectsSparseAndOverflowingStepsWhenCreatingAScheduler()
    {
        // A sparse JS array has no C# counterpart; a null element is the closest malformed list.
        foreach (var learningSteps in new[] { new string?[] { null }, ["1e308m"], ["1e308d"] })
        {
            FluentActions.Invoking(() => CreateCore(learningSteps: learningSteps!))
                .Should().Throw<FsrsValidationException>().WithMessage("Expected valid learningSteps array");
        }
    }

    [Fact]
    public void AcceptsCardInputWithoutLearningStep()
    {
        var core = CreateCore();
        var card = core.Scheduler.NewCard(Now);
        card = card with { Fields = card.Fields.Without(LearningStepsMiddleware.LearningStepField) };
        var result = Review(core, card, Rating.Again);

        result.Revlog.LearningStep.Should().Be(0);
        result.Card.LearningStep.Should().Be(0);
    }

    [Fact]
    public void SchedulesEveryGradeFromANewCardLikeTheLegacyFsrsScheduler()
    {
        var core = CreateCore();
        var card = core.Scheduler.NewCard(Now);
        var previews = core.Scheduler.Preview(card, Now);

        card.LearningStep.Should().Be(0);

        var again = previews[Rating.Again];
        again.Card.State.Should().Be(State.Learning);
        again.Card.ScheduleStatus.Should().Be((ScheduleStatus)"learning");
        again.Card.LearningStep.Should().Be(0);
        (again.Card.DueAt - Now).TotalMilliseconds.Should().Be(60_000);
        again.Revlog.LearningStep.Should().Be(0);

        var hard = previews[Rating.Hard];
        hard.Card.State.Should().Be(State.Learning);
        hard.Card.LearningStep.Should().Be(0);
        (hard.Card.DueAt - Now).TotalMilliseconds.Should().Be(5.5 * 60_000);

        var good = previews[Rating.Good];
        good.Card.State.Should().Be(State.Learning);
        good.Card.LearningStep.Should().Be(1);
        (good.Card.DueAt - Now).TotalMilliseconds.Should().Be(10 * 60_000);

        var easy = previews[Rating.Easy];
        easy.Card.State.Should().Be(State.Review);
        easy.Card.ScheduleStatus.Should().Be(ScheduleStatus.Review);
        easy.Card.LearningStep.Should().Be(0);
    }

    [Fact]
    public void UsesTheModelIntervalOnlyWhenNoPositiveLearningStepExists()
    {
        var core = CreateCore(learningSteps: ["1m"]);
        var card = core.Scheduler.NewCard(Now);

        Review(core, card, Rating.Again);
        core.Spy.CallCount.Should().Be(0);

        Review(core, card, Rating.Easy);
        core.Spy.CallCount.Should().Be(1);
    }

    [Theory]
    [InlineData(null, 86_400_000.0)]
    [InlineData(0.5, 43_200_000.0)]
    public void GraduatesFromLearningStepsForAtLeastTheGraduatingInterval(double? graduatingInterval, double expected)
    {
        var core = CreateCore(learningSteps: ["1m"], graduatingInterval: graduatingInterval);
        var card = core.Scheduler.NewCard(Now);
        core.Spy.NextIntervalOverride = (_, _) => 0.25;

        foreach (var grade in new[] { Rating.Good, Rating.Easy })
        {
            var result = Review(core, card, grade);
            Delay(result, Now).Should().Be(expected);
            result.Card.State.Should().Be(State.Review);
            result.Card.LearningStep.Should().Be(0);
        }
    }

    [Theory]
    [InlineData(new[] { "10m" }, 86_400_000.0, State.Review)]
    [InlineData(new string[0], 21_600_000.0, State.Relearning)]
    public void KeepsPassingReviewsInReviewForAtLeastTheGraduatingInterval(string[] relearningSteps, double expected, State state)
    {
        var core = CreateCore(relearningSteps: relearningSteps);
        var card = Review(core, core.Scheduler.NewCard(Now), Rating.Easy).Card;
        core.Spy.NextIntervalOverride = (_, _) => 0.25;

        foreach (var grade in new[] { Rating.Hard, Rating.Good, Rating.Easy })
        {
            var result = Review(core, card, grade);
            Delay(result, Now).Should().Be(expected);
            result.Card.State.Should().Be(state);
        }
    }

    [Theory]
    [InlineData(0.4, State.Learning)]
    [InlineData(0.6, State.Review)]
    public void ClassifiesIntervalsByTheGraduatingInterval(double modelInterval, State state)
    {
        var core = CreateCore(learningSteps: [], graduatingInterval: 0.5);
        core.Spy.NextIntervalOverride = (_, _) => modelInterval;
        Review(core, core.Scheduler.NewCard(Now), Rating.Good).Card.State.Should().Be(state);
    }

    [Fact]
    public void DoesNotApplyTheGraduationFloorToEmptyLearningSteps()
    {
        var core = CreateCore(learningSteps: []);
        core.Spy.NextIntervalOverride = (_, _) => 0.25;
        var result = Review(core, core.Scheduler.NewCard(Now), Rating.Good);

        Delay(result, Now).Should().Be(21_600_000);
        result.Card.State.Should().Be(State.Learning);
    }

    [Theory]
    // [model days, Again, Hard, Good] in minutes
    [InlineData(0.5 / 1440, 1.0, 5.5, 10.0)]
    [InlineData(30.0 / 1440, 30.0, 30.0, 30.0)]
    [InlineData(2.0, 1440.0, 2880.0, 2880.0)]
    public void LetsAModelIntervalOverrideShorterSteps(double modelInterval, double again, double hard, double good)
    {
        var core = CreateCore(allowModelOverride: true);
        var card = core.Scheduler.NewCard(Now);
        core.Spy.NextIntervalOverride = (_, _) => modelInterval;
        var results = new[] { Rating.Again, Rating.Hard, Rating.Good }.Select(grade => Review(core, card, grade)).ToArray();

        results.Select(r => Delay(r, Now) / 60_000).Should().Equal(again, hard, good);
        // Again stays in learning; a day or more graduates Hard and Good early.
        results[0].Card.State.Should().Be(State.Learning);
        results[0].Card.LearningStep.Should().Be(0);
        for (var index = 0; index < 2; index++) // Hard, Good (upstream: results.slice(1).entries())
        {
            var result = results[index + 1];
            result.Card.State.Should().Be(modelInterval >= 1 ? State.Review : State.Learning);
            result.Card.LearningStep.Should().Be(modelInterval >= 1 ? 0 : index == 0 ? 0 : 1);
        }
    }

    [Fact]
    public void GraduatesGradesWhoseStepIsZeroMinutes()
    {
        var core = CreateCore(learningSteps: ["0m"]);
        var card = core.Scheduler.NewCard(Now);
        core.Spy.NextIntervalOverride = (_, _) => 0.25;

        // Hard's 0m step counts as no step; Again keeps the model interval.
        var hard = Review(core, card, Rating.Hard);
        Delay(hard, Now).Should().Be(86_400_000);
        hard.Card.State.Should().Be(State.Review);
        var again = Review(core, card, Rating.Again);
        Delay(again, Now).Should().Be(21_600_000);
        again.Card.State.Should().Be(State.Learning);
    }

    [Fact]
    public void ResetsLearningStepWhenAStepReachesReview()
    {
        var core = CreateCore(learningSteps: ["1d", "2d"], relearningSteps: ["10m", "20m"]);
        var result = Review(core, core.Scheduler.NewCard(Now), Rating.Good);

        Delay(result, Now).Should().Be(2 * Day);
        result.Card.State.Should().Be(State.Review);
        result.Card.LearningStep.Should().Be(0);
        core.Scheduler.Rollback(result.Card, result.Revlog).LearningStep.Should().Be(0);
    }

    [Theory]
    [InlineData(true, 2 * Day, State.Review, 0)]
    [InlineData(false, 5.5 * 60_000, State.Learning, 0)]
    public void KeepsDownstreamPoliciesOnAModelOverride(bool allowModelOverride, double expectedDelay, State state, int learningStep)
    {
        var config = MiddlewareTestSchedulers.Fsrs6Config(true, 1);
        config.SetRaw("learningSteps", new[] { "1m", "10m" });
        config.SetRaw("relearningSteps", new[] { "10m" });
        config.SetRaw("allowModelOverride", allowModelOverride);
        config.SetRaw("maximumInterval", 2);
        var spy = new SpyModel<FsrsState>(Fsrs6Model.Instance) { NextIntervalOverride = (_, _) => 3 };
        var scheduler = MiddlewareTestSchedulers.Fsrs6(spy, LearningStepsMiddleware.Instance, MaximumIntervalMiddleware.Instance).Create(config);
        var result = scheduler.Review(scheduler.NewCard(Now), Rating.Hard, Now);

        // An overriding 3d model interval is capped downstream instead of being replaced by the 5.5m Hard step.
        Delay(result, Now).Should().Be(expectedDelay);
        result.Card.State.Should().Be(state);
        result.Card.LearningStep.Should().Be(learningStep);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EntersReviewFromAOneDayAgainStep(bool allowModelOverride)
    {
        var core = CreateCore(learningSteps: ["1d"], allowModelOverride: allowModelOverride);
        core.Spy.NextIntervalOverride = (_, _) => 0.25;
        var result = Review(core, core.Scheduler.NewCard(Now), Rating.Again);

        Delay(result, Now).Should().Be(86_400_000);
        result.Card.State.Should().Be(State.Review);
        result.Card.LearningStep.Should().Be(0);
    }

    [Fact]
    public void CapsAnOverridingAgainAtTheGraduatingInterval()
    {
        var core = CreateCore(allowModelOverride: true, graduatingInterval: 0.5);
        var reviewed = Review(core, core.Scheduler.NewCard(Now), Rating.Easy);
        core.Spy.NextIntervalOverride = (_, _) => 13;
        var result = Review(core, reviewed.Card, Rating.Again);

        Delay(result, Now).Should().Be(43_200_000);
        result.Card.State.Should().Be(State.Relearning);
        result.Card.ScheduleStatus.Should().Be((ScheduleStatus)"learning");
        result.Card.LearningStep.Should().Be(0);
    }

    [Fact]
    public void DelegatesIntervalsForUncachedCandidateMemoryStates()
    {
        double interval = double.NaN;
        var probe = new ProbeMiddleware<FsrsState, DateTime>("uncached-candidate")
        {
            OnReview = (ctx, next) =>
            {
                var memoryState = ctx.Candidate.Step(Rating.Easy) with { };
                interval = ctx.Candidate.NextInterval(memoryState, ctx.Candidate.DesiredRetention[Rating.Easy]);
                next();
            },
        };
        var core = CreateCore(learningSteps: ["1m"], extra: probe);

        Review(core, core.Scheduler.NewCard(Now), Rating.Again);

        interval.Should().BeGreaterThan(0);
        core.Spy.CallCount.Should().Be(1);
    }

    [Fact]
    public void GraduatesAfterTheFinalLearningStep()
    {
        var core = CreateCore();
        var first = Review(core, core.Scheduler.NewCard(Now), Rating.Good);
        var second = Review(core, first.Card, Rating.Good, first.Card.DueAt);

        first.Card.LearningStep.Should().Be(1);
        second.Card.State.Should().Be(State.Review);
        second.Card.ScheduleStatus.Should().Be(ScheduleStatus.Review);
        second.Card.LearningStep.Should().Be(0);
    }

    [Fact]
    public void KeepsIntermediateLearningAndRelearningStates()
    {
        var core = CreateCore(learningSteps: ["1m", "10m", "20m"], relearningSteps: ["10m", "20m"]);
        var first = Review(core, core.Scheduler.NewCard(Now), Rating.Good);
        first.Card.State.Should().Be(State.Learning);
        first.Card.LearningStep.Should().Be(1);

        var learning = Review(core, first.Card, Rating.Good, first.Card.DueAt);
        learning.Card.State.Should().Be(State.Learning);
        learning.Card.LearningStep.Should().Be(2);

        var reviewed = Review(core, core.Scheduler.NewCard(Now), Rating.Easy);
        reviewed.Card.State.Should().Be(State.Review);
        reviewed.Card.LearningStep.Should().Be(0);
        var lapse = Review(core, reviewed.Card, Rating.Again, reviewed.Card.DueAt);
        var relearning = Review(core, lapse.Card, Rating.Good, lapse.Card.DueAt);
        relearning.Card.State.Should().Be(State.Relearning);
        relearning.Card.LearningStep.Should().Be(1);
    }

    [Fact]
    public void MovesAReviewLapseIntoRelearning()
    {
        var core = CreateCore(relearningSteps: ["10m", "20m"]);
        var reviewed = Review(core, core.Scheduler.NewCard(Now), Rating.Easy);
        var lapse = Review(core, reviewed.Card, Rating.Again, reviewed.Card.DueAt);

        lapse.Card.State.Should().Be(State.Relearning);
        lapse.Card.ScheduleStatus.Should().Be((ScheduleStatus)"learning");
        lapse.Card.LearningStep.Should().Be(0);
        (lapse.Card.DueAt - reviewed.Card.DueAt).TotalMilliseconds.Should().Be(10 * 60_000);
    }

    [Theory]
    [InlineData("0.6m", State.Learning, "learning", 0.6)]
    [InlineData("1439.4m", State.Learning, "learning", 1439.4)]
    [InlineData("1439.6m", State.Learning, "learning", 1439.6)]
    [InlineData("1440m", State.Review, "review", 1440.0)]
    public void PreservesSecondPrecisionBeforeApplyingTheDayThreshold(string step, State state, string scheduleStatus, double scheduledMinutes)
    {
        var core = CreateCore(learningSteps: [step]);
        var result = Review(core, core.Scheduler.NewCard(Now), Rating.Again);

        result.Card.State.Should().Be(state);
        result.Card.ScheduleStatus.Should().Be((ScheduleStatus)scheduleStatus);
        Delay(result, Now).Should().Be(scheduledMinutes * 60_000);
    }

    [Fact]
    public void GraduatesLongLearningDelaysWhilePreservingTheExactDueTime()
    {
        var core = CreateCore(learningSteps: ["1.5d"]);
        var result = Review(core, core.Scheduler.NewCard(Now), Rating.Again);

        result.Card.State.Should().Be(State.Review);
        result.Card.ScheduleStatus.Should().Be(ScheduleStatus.Review);
        result.Card.LearningStep.Should().Be(0);
        Delay(result, Now).Should().Be(2160 * 60_000);
    }

    [Fact]
    public void FallsBackToTheModelIntervalWhenAStepHasNoPositiveDuration()
    {
        var core = CreateCore(learningSteps: ["0m"]);
        var result = Review(core, core.Scheduler.NewCard(Now), Rating.Again);

        result.Card.State.Should().Be(State.Review);
        result.Card.ScheduleStatus.Should().Be(ScheduleStatus.Review);
        result.Card.LearningStep.Should().Be(0);
        result.Card.DueAt.Should().BeAfter(Now);
    }

    [Theory]
    [InlineData("0.017m", 1_000.0)]
    [InlineData("0.1m", 6_000.0)]
    [InlineData("0.4m", 24_000.0)]
    public void SchedulesStepsWithSecondPrecisionInsteadOfUsingTheModelInterval(string step, double expectedDelay)
    {
        var core = CreateCore(learningSteps: [step]);
        var result = Review(core, core.Scheduler.NewCard(Now), Rating.Again);

        result.Card.State.Should().Be(State.Learning);
        result.Card.ScheduleStatus.Should().Be((ScheduleStatus)"learning");
        result.Card.LearningStep.Should().Be(0);
        Delay(result, Now).Should().Be(expectedDelay);
        core.Spy.CallCount.Should().Be(0);
    }

    [Fact]
    public void KeepsASubDayRelearningStepInTheRelearningState()
    {
        var core = CreateCore(relearningSteps: ["1439.6m"]);
        var reviewed = Review(core, core.Scheduler.NewCard(Now), Rating.Easy);
        var callsBefore = core.Spy.CallCount;
        var result = Review(core, reviewed.Card, Rating.Again, reviewed.Card.DueAt);

        result.Card.State.Should().Be(State.Relearning);
        result.Card.ScheduleStatus.Should().Be((ScheduleStatus)"learning");
        result.Card.LearningStep.Should().Be(0);
        (result.Card.DueAt - reviewed.Card.DueAt).TotalMilliseconds.Should().Be(1439.6 * 60_000);
        core.Spy.CallCount.Should().Be(callsBefore);
    }

    public static TheoryData<string, string[], int> RawModelIntervalCases => new()
    {
        { "zero-minute", ["0m"], 0 },
        { "empty", [], 0 },
        { "exhausted", ["1m"], 1 },
    };

    [Theory]
    [MemberData(nameof(RawModelIntervalCases))]
    public void UsesRawModelIntervalsWithoutMonotonicMiddleware(string name, string[] steps, int learningStep)
    {
        _ = name;
        var seedCore = CreateCore();
        var learningCard = Review(seedCore, seedCore.Scheduler.NewCard(Now), Rating.Again).Card;
        var reviewCard = Review(seedCore, seedCore.Scheduler.NewCard(Now), Rating.Easy).Card;
        var relearningCard = Review(seedCore, reviewCard, Rating.Again, reviewCard.DueAt).Card;
        var core = CreateCore(learningSteps: steps, relearningSteps: steps);

        double[] PreviewDueDays(Card<FsrsState> card)
        {
            var reviewTime = card.DueAt;
            var preview = core.Scheduler.Preview(card.With(LearningStepsMiddleware.LearningStepField, learningStep), reviewTime);
            return [.. preview.Select(item => (item.Card.DueAt - reviewTime).TotalMilliseconds / Day)];
        }

        learningCard.State.Should().Be(State.Learning);
        relearningCard.State.Should().Be(State.Relearning);
        PreviewDueDays(learningCard).Should().Equal(1, 1, 1, 1);
        PreviewDueDays(relearningCard).Should().Equal(1, 1, 1, 2);
    }

    [Fact]
    public void BypassesLearningStepsWhenShortTermSchedulingIsDisabled()
    {
        var core = CreateCore(enableShortTerm: false);
        var result = Review(core, core.Scheduler.NewCard(Now), Rating.Again);

        result.Card.State.Should().Be(State.Review);
        result.Card.ScheduleStatus.Should().Be(ScheduleStatus.Review);
        result.Card.LearningStep.Should().Be(0);
        result.Revlog.LearningStep.Should().Be(0);
    }

    [Fact]
    public void RestoresLearningStepDuringRollback()
    {
        var core = CreateCore();
        var first = Review(core, core.Scheduler.NewCard(Now), Rating.Good);
        var second = Review(core, first.Card, Rating.Good, first.Card.DueAt);

        first.Card.LearningStep.Should().Be(1);
        second.Card.LearningStep.Should().Be(0);
        core.Scheduler.Rollback(second.Card, second.Revlog).LearningStep.Should().Be(1);
    }
}
