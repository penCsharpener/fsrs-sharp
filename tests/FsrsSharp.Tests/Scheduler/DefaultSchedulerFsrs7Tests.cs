using System.Globalization;
using System.Text.Json;
using Xunit;
using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs7;
using FsrsSharp.Reschedule;
using FsrsSharp.Tests.Models.Fsrs7;
using FsrsSharp.Tests.Middlewares;
using static FsrsSharp.Tests.Scheduler.SchedulerTestSupport;

namespace FsrsSharp.Tests.Scheduler;

/// <summary>Port of ts-fsrs <c>scheduler/default-scheduler.fsrs7.spec.ts</c>.</summary>
public class DefaultSchedulerFsrs7Tests
{
    private sealed record ReferenceHistory(IReadOnlyList<ModelReview> History, IReadOnlyList<Fsrs7State> States);

    // Reference: fsrs-rs c9562d6 and srs-benchmark b4b0254, evaluated in float32.
    private static readonly ReferenceHistory[] ReferenceHistories =
        JsonDocument.Parse(Fsrs7ReferenceData.HistoriesJson).RootElement.EnumerateArray()
            .Select(e => new ReferenceHistory(
                [.. e.GetProperty("history").EnumerateArray()
                    .Select(h => new ModelReview((Rating)h.GetProperty("rating").GetInt32(), h.GetProperty("deltaT").GetDouble()))],
                [.. e.GetProperty("states").EnumerateArray()
                    .Select(s => new Fsrs7State(
                        s.GetProperty("stability").GetDouble(),
                        s.GetProperty("stabilityFast").GetDouble(),
                        s.GetProperty("difficulty").GetDouble()))]))
            .ToArray();

    private static readonly DateTime Now0 = new(2026, 9, 8, 23, 59, 0, DateTimeKind.Utc);
    private const double MsPerDay = 86_400_000;

    private static IReadOnlyList<string> LearningSteps<TState>(Scheduler<TState, DateTime> scheduler) where TState : class =>
        scheduler.Config.Get(LearningStepsMiddleware.LearningSteps);

    private static IReadOnlyList<string> RelearningSteps<TState>(Scheduler<TState, DateTime> scheduler) where TState : class =>
        scheduler.Config.Get(LearningStepsMiddleware.RelearningSteps);

    private static void ExpectClose(double actual, double expected) =>
        Math.Abs(actual - expected).Should().BeLessThanOrEqualTo(Math.Max(1e-7, expected * 1e-4));

    private static void ExpectStateClose(Fsrs7State actual, Fsrs7State expected)
    {
        ExpectClose(actual.Stability, expected.Stability);
        ExpectClose(actual.StabilityFast, expected.StabilityFast);
        ExpectClose(actual.Difficulty, expected.Difficulty);
    }

    private static DateTime Instant(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture).UtcDateTime;

    [Fact]
    public void DefaultsAnUnspecifiedPresetVersionToFsrs7()
    {
        DefaultScheduler.Create().Definition.Should().BeSameAs(DefaultScheduler.Fsrs7Definition);
        DefaultScheduler.GetDefinition(FsrsVersion.Fsrs7).Should().BeSameAs(DefaultScheduler.GetDefinition(FsrsVersion.Fsrs7));
        DefaultScheduler.Fsrs7Definition.Should().BeSameAs(DefaultScheduler.Fsrs7Definition);
    }

    [Fact]
    public void UsesDefaultStepsWithModelOverrideWhenStepsAreUnspecified()
    {
        var scheduler = DefaultScheduler.Create();
        var widened = DefaultScheduler.Create(FsrsVersion.Fsrs7);

        LearningSteps(scheduler).Should().Equal("1m", "10m");
        RelearningSteps(scheduler).Should().Equal("10m");
        scheduler.Config.Get(LearningStepsMiddleware.EnableShortTerm).Should().BeTrue();
        scheduler.Config.Get(LearningStepsMiddleware.AllowModelOverride).Should().BeTrue();
        LearningSteps(widened).Should().Equal("1m", "10m");
        RelearningSteps(widened).Should().Equal("10m");
        widened.Config.Get(LearningStepsMiddleware.AllowModelOverride).Should().BeTrue();

        var result = scheduler.Review(scheduler.NewCard(Now0), Rating.Again, Now0);
        // The model's Again interval is shorter than the 1m step.
        scheduler.Model.NextInterval(result.Card.MemoryState, 0.9).Should().BeLessThan(1.0 / 1440);
        result.Card.ScheduledDays.Should().Be(1.0 / 1440);
        result.Card.State.Should().Be(State.Learning);
        result.Card.LearningStep.Should().Be(0);
    }

    [Fact]
    public void PreservesExplicitlyConfiguredFsrs7StepsIndependently()
    {
        var learning = DefaultScheduler.Create(new DefaultSchedulerOptions { LearningSteps = ["2m"] });
        LearningSteps(learning).Should().Equal("2m");
        RelearningSteps(learning).Should().Equal("10m");
        var relearning = DefaultScheduler.Create(new DefaultSchedulerOptions { RelearningSteps = ["3m"] });
        LearningSteps(relearning).Should().Equal("1m", "10m");
        RelearningSteps(relearning).Should().Equal("3m");
    }

    [Theory]
    [InlineData(FsrsVersion.Fsrs3)]
    [InlineData(FsrsVersion.Fsrs4)]
    [InlineData(FsrsVersion.Fsrs4Dot5)]
    [InlineData(FsrsVersion.Fsrs5)]
    [InlineData(FsrsVersion.Fsrs6)]
    public void PreservesDefaultStepsForOlderVersions(FsrsVersion version)
    {
        var scheduler = DefaultScheduler.Create(version);

        LearningSteps(scheduler).Should().Equal("1m", "10m");
        RelearningSteps(scheduler).Should().Equal("10m");
        scheduler.Config.Get(LearningStepsMiddleware.AllowModelOverride).Should().BeFalse();
    }

    [Fact]
    public void ExposesTheFsrs7ModelConfigWithoutLegacyModelFields()
    {
        var scheduler = DefaultScheduler.Create();

        scheduler.Model.Config.Should().BeOfType<Fsrs7Config>().Which.Weights.Should().Equal(Fsrs7Constants.DefaultWeights);
        scheduler.Config.Get(LearningStepsMiddleware.EnableShortTerm).Should().BeTrue();
        scheduler.Config.Get(KitSchemas.FractionalDays).Should().BeTrue();
        scheduler.NewCard(Now0).MemoryState.StabilityFast.Should().Be(0);
        scheduler.Chrono.Difference(Instant("2024-03-09T12:00:00-05:00"), Instant("2024-03-10T12:00:00-04:00")).Should().Be(23.0 / 24);
        scheduler.Chrono.Difference(Instant("2024-11-02T12:00:00-04:00"), Instant("2024-11-03T12:00:00-05:00")).Should().Be(25.0 / 24);
    }

    [Fact]
    public void ConfiguresFractionalDaysThroughTheSharedDateChronoDefinition()
    {
        var scheduler = DefaultScheduler.Create();
        scheduler.Definition.Chrono.Should().BeSameAs(DateTimeChrono.Instance);
        scheduler.Config.Get(KitSchemas.FractionalDays).Should().BeTrue();
        DefaultScheduler.Create(FsrsVersion.Fsrs6).Config.Get(KitSchemas.FractionalDays).Should().BeFalse();

        var definition = SchedulerDefinition.Define(Fsrs7Model.Instance, DateTimeChrono.Instance);
        var fractional = definition.Create(new FieldBag.Builder()
            .Set(Fsrs7Model.Weights, Fsrs7Constants.DefaultWeights)
            .Set(KitSchemas.FractionalDays, true));
        var calendar = definition.Create(new FieldBag.Builder().Set(Fsrs7Model.Weights, Fsrs7Constants.DefaultWeights));
        var later = AddMs(Now0, 120_000);

        fractional.Chrono.Difference(Now0, later).Should().Be(2.0 / 1440);
        calendar.Chrono.Difference(Now0, later).Should().Be(1);

        var invalid = new FieldBag.Builder().Set(Fsrs7Model.Weights, Fsrs7Constants.DefaultWeights);
        invalid.SetRaw("fractionalDays", "true");
        var act = () => definition.Create(invalid);
        act.Should().Throw<FsrsValidationException>().WithMessage("*fractionalDays*");
    }

    [Fact]
    public void MigratesFullReviewHistoryThroughRescheduleWithoutLosingTheFastTrace()
    {
        var scheduler = DefaultScheduler.Create(new DefaultSchedulerOptions { LearningSteps = [], RelearningSteps = [] });
        var rescheduler = new Rescheduler<Fsrs7State, DateTime>(scheduler);
        var time = Now0;
        var source = ReferenceHistories[2];
        List<RescheduleReview<DateTime>> history = [];
        foreach (var (rating, deltaT) in source.History)
        {
            time = AddMs(time, Helpers.JsRound(deltaT * MsPerDay));
            history.Add(new RescheduleReview<DateTime>(rating, time));
        }

        var replay = rescheduler.Replay(new ReplayInput<Fsrs7State, DateTime>(history));
        var scheduled = rescheduler.Reschedule(new RescheduleInput<Fsrs7State, DateTime>(history));

        scheduled.Card.MemoryState.Should().Be(replay.MemoryState);
        for (var index = 0; index < replay.MemoryStates.Count; index++)
            ExpectStateClose(replay.MemoryStates[index], source.States[index]);
    }

    [Fact]
    public void PreservesAllThreeMemoryFieldsInCardsRevlogsAndRollback()
    {
        var scheduler = DefaultScheduler.Create(new DefaultSchedulerOptions
        {
            LearningSteps = ["1m", "10m"],
            AllowModelOverride = false,
        });
        var card = scheduler.NewCard(Now0, MiddlewareTestSchedulers.CardInput("fsrs7"));
        card.MemoryState.StabilityFast.Should().Be(0);

        var result = scheduler.Review(card, Rating.Good, Now0);

        result.Card.MemoryState.StabilityFast.Should().BeApproximately(Fsrs7Constants.DefaultWeights[2] * 0.8, 1e-10);
        result.Revlog.MemoryState.StabilityFast.Should().Be(0);
        result.Card.LearningStep.Should().Be(1);
        result.Card.ScheduledDays.Should().Be(10.0 / 1440);
        scheduler.Rollback(result.Card, result.Revlog).Should().Be(card);
        scheduler.Forget(result.Card, Now0).MemoryState.StabilityFast.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplaysFractionalHistoriesWithEnableShortTerm(bool enableShortTerm)
    {
        var scheduler = DefaultScheduler.Create(new DefaultSchedulerOptions
        {
            EnableShortTerm = enableShortTerm,
            LearningSteps = [],
            RelearningSteps = [],
        });
        var cardId = MiddlewareTestSchedulers.CardInput("history");

        foreach (var test in ReferenceHistories)
        {
            var time = Now0;
            var card = scheduler.NewCard(time, cardId);
            List<ScheduleResult<Fsrs7State>> results = [];
            for (var index = 0; index < test.History.Count; index++)
            {
                var review = test.History[index];
                time = AddMs(time, Helpers.JsRound(review.DeltaT * MsPerDay));
                var result = scheduler.Review(card, review.Rating, time);
                ExpectStateClose(result.Card.MemoryState, test.States[index]);
                result.Revlog.MemoryState.StabilityFast.Should().Be(card.MemoryState.StabilityFast);
                result.Revlog.ScheduledDays.Should().Be(card.ScheduledDays);
                // Rollback restores the pre-review card exactly, due date included.
                scheduler.Rollback(result.Card, result.Revlog).Should().Be(card);
                results.Add(result);
                card = result.Card;
            }

            // Revlog before-images restore the sequence's memory state, including both traces.
            foreach (var result in Enumerable.Reverse(results))
                card = scheduler.Rollback(card, result.Revlog);
            card.Should().Be(scheduler.NewCard(Now0, cardId));
        }
    }

    [Fact]
    public void UsesFractionalElapsedDaysAcrossMidnightAndDistinguishesReviewsWithinOneDay()
    {
        var scheduler = DefaultScheduler.Create(new DefaultSchedulerOptions { EnableShortTerm = false });
        var first = scheduler.Review(scheduler.NewCard(Now0), Rating.Again, Now0);
        var later = AddMs(Now0, 2 * 60_000);

        var result = scheduler.Review(first.Card, Rating.Good, later);

        var expected = scheduler.Model.Step(new ModelStepInput<Fsrs7State>(first.Card.MemoryState, Rating.Good, 2.0 / 1440));
        result.Card.MemoryState.Stability.Should().Be(expected.Stability);
        result.Card.MemoryState.StabilityFast.Should().Be(expected.StabilityFast);
        result.Card.MemoryState.Should().NotBe(
            scheduler.Model.Step(new ModelStepInput<Fsrs7State>(first.Card.MemoryState, Rating.Good, 1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeepsModelSubDayIntervalsPositiveWithEnableFuzz(bool enableFuzz)
    {
        var scheduler = DefaultScheduler.Create(new DefaultSchedulerOptions { EnableShortTerm = false, EnableFuzz = enableFuzz });
        var card = scheduler.NewCard(Now0, MiddlewareTestSchedulers.CardInput("fractional"));

        var result = scheduler.Review(card, Rating.Again, Now0);

        var interval = scheduler.Model.NextInterval(result.Card.MemoryState, 0.9);
        interval.Should().BeGreaterThan(0);
        interval.Should().BeLessThan(1);
        result.Card.ScheduledDays.Should().Be(interval);
        MsBetween(result.Card.DueAt, Now0).Should().Be((long)Math.Truncate(interval * MsPerDay));
        result.Card.State.Should().Be(State.Learning);
        foreach (var preview in scheduler.Preview(card, Now0))
            preview.Card.Should().Be(scheduler.Review(card, preview.Grade, Now0).Card);
    }

    [Theory]
    [InlineData("0m")]
    [InlineData("0.001m")]
    [InlineData("0.01m")]
    [InlineData("1439.6m")]
    [InlineData("1440m")]
    public void OnlyAPositiveSecondRoundedExplicitStepOverridesTheModel(string step)
    {
        var scheduler = DefaultScheduler.Create(new DefaultSchedulerOptions
        {
            LearningSteps = [step],
            EnableFuzz = true,
            AllowModelOverride = false,
        });
        var card = scheduler.NewCard(Now0);

        var result = scheduler.Review(card, Rating.Again, Now0);

        var minutes = Helpers.JsRound(double.Parse(step.TrimEnd('m'), CultureInfo.InvariantCulture) * 60) / 60;
        if (minutes > 0)
        {
            result.Card.ScheduledDays.Should().Be(minutes / 1440);
            MsBetween(result.Card.DueAt, Now0).Should().Be((long)Helpers.JsRound(minutes * 60_000));
            result.Card.State.Should().Be(minutes < 1440 ? State.Learning : State.Review);
        }
        else
        {
            result.Card.ScheduledDays.Should().Be(scheduler.Model.NextInterval(result.Card.MemoryState, 0.9));
            result.Card.State.Should().Be(State.Learning);
        }

        result.Card.LearningStep.Should().Be(0);
        scheduler.Rollback(result.Card, result.Revlog).Should().Be(card);
    }

    [Fact]
    public void LetsALongerModelIntervalOverrideAShorterStepWhenEnabled()
    {
        var scheduler = DefaultScheduler.Create(new DefaultSchedulerOptions
        {
            LearningSteps = ["0.01m"],
            EnableFuzz = true,
            AllowModelOverride = true,
        });
        var card = scheduler.NewCard(Now0);

        var result = scheduler.Review(card, Rating.Again, Now0);

        var modelInterval = scheduler.Model.NextInterval(result.Card.MemoryState, 0.9);
        // 0.01m rounds to a 1s step, shorter than the model interval.
        modelInterval.Should().BeGreaterThan(1.0 / 86400);
        modelInterval.Should().BeLessThan(1);
        result.Card.ScheduledDays.Should().Be(modelInterval);
        MsBetween(result.Card.DueAt, Now0).Should().Be((long)Math.Truncate(modelInterval * 86_400_000));
        result.Card.State.Should().Be(State.Learning);
        result.Card.LearningStep.Should().Be(0);
        scheduler.Rollback(result.Card, result.Revlog).Should().Be(card);
    }

    [Fact]
    public void GraduatesACompletedStepForOneDayWhenTheModelIntervalIsBelowOneDay()
    {
        var scheduler = DefaultScheduler.Create(new DefaultSchedulerOptions { DesiredRetention = 0.99, LearningSteps = ["1m"] });
        var first = scheduler.Review(scheduler.NewCard(Now0), Rating.Again, Now0);

        var result = scheduler.Review(first.Card, Rating.Good, first.Card.DueAt);

        scheduler.Model.NextInterval(result.Card.MemoryState, 0.99).Should().BeLessThan(1);
        result.Card.ScheduledDays.Should().Be(1);
        result.Card.State.Should().Be(State.Review);
        result.Card.ScheduleStatus.Should().Be(ScheduleStatus.Review);
        result.Card.LearningStep.Should().Be(0);
    }

    [Fact]
    public void GraduatesForTheConfiguredGraduatingInterval()
    {
        var scheduler = DefaultScheduler.Create(new DefaultSchedulerOptions
        {
            DesiredRetention = 0.99,
            LearningSteps = ["1m"],
            GraduatingInterval = 0.5,
        });
        var first = scheduler.Review(scheduler.NewCard(Now0), Rating.Again, Now0);

        var result = scheduler.Review(first.Card, Rating.Good, first.Card.DueAt);

        scheduler.Model.NextInterval(result.Card.MemoryState, 0.99).Should().BeLessThan(0.5);
        result.Card.ScheduledDays.Should().Be(0.5);
        result.Card.State.Should().Be(State.Review);
        result.Card.LearningStep.Should().Be(0);
    }

    [Fact]
    public void KeepsMaximumIntervalAsMiddlewarePolicy()
    {
        var scheduler = DefaultScheduler.Create(new DefaultSchedulerOptions { EnableShortTerm = false, MaximumInterval = 2 });
        var created = scheduler.NewCard(Now0);
        var card = created with
        {
            MemoryState = new Fsrs7State(1000, 800, 5),
            State = State.Review,
            Fields = created.Fields.With(DateTimeChrono.LastReviewAt, (DateTime?)Now0),
        };

        var result = scheduler.Review(card, Rating.Easy, AddMs(Now0, MsPerDay));

        scheduler.Model.NextInterval(result.Card.MemoryState, 0.9).Should().BeGreaterThan(2);
        result.Card.ScheduledDays.Should().Be(2);
    }

    [Fact]
    public void DoesNotUseTheEnableShortTermPolicyToDisableTheIntrinsicFastTrace()
    {
        var withSteps = DefaultScheduler.Create(new DefaultSchedulerOptions { EnableShortTerm = true });
        var withoutSteps = DefaultScheduler.Create(new DefaultSchedulerOptions { EnableShortTerm = false });
        var model = Fsrs7Model.Instance.Create(new Fsrs7Config([.. Fsrs7Constants.DefaultWeights]));
        var input = new ModelStepInput<Fsrs7State>(new Fsrs7State(1, 0.8, 5), Rating.Good, 0.01);

        withSteps.Model.Step(input).Should().Be(model.Step(input));
        withoutSteps.Model.Step(input).Should().Be(model.Step(input));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MapsFinalIntervalsForEveryStateWithEnableShortTerm(bool enableShortTerm)
    {
        var spy = new SpyModel<Fsrs7State>(Fsrs7Model.Instance);
        var scheduler = DefaultScheduler.Apply(SchedulerDefinition.Define(spy, DateTimeChrono.Instance))
            .Create(DefaultScheduler.BuildConfig(
                FsrsVersion.Fsrs7,
                new DefaultSchedulerOptions { EnableShortTerm = enableShortTerm, LearningSteps = [], RelearningSteps = [] }));

        foreach (var state in AllStates)
        {
            var created = scheduler.NewCard(Now0);
            var card = created with
            {
                MemoryState = new Fsrs7State(1, 0.8, 5),
                State = state,
                ScheduleStatus = state switch
                {
                    State.New => ScheduleStatus.New,
                    State.Review => ScheduleStatus.Review,
                    _ => ScheduleStatus.Learning,
                },
                Fields = created.Fields.With(DateTimeChrono.LastReviewAt, state == State.New ? null : (DateTime?)Now0),
            };

            foreach (var days in new[] { 0.0, 0.5, 1, 2 })
            {
                spy.NextIntervalOverride = (_, _) => days;
                foreach (var result in scheduler.Preview(card, Now0))
                {
                    var expected = days < 1
                        ? state is State.New or State.Learning ? State.Learning : State.Relearning
                        : State.Review;
                    result.Card.State.Should().Be(expected);
                    result.Card.ScheduleStatus.Should().Be(days < 1 ? ScheduleStatus.Learning : ScheduleStatus.Review);
                    result.Card.LearningStep.Should().Be(0);
                    result.Card.Should().Be(scheduler.Review(card, result.Grade, Now0).Card);
                    scheduler.Rollback(result.Card, result.Revlog).Should().Be(card);
                }
            }
        }
    }
}
