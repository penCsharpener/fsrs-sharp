using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;
using Xunit;
using static FsrsSharp.Tests.Middlewares.MiddlewareTestSchedulers;
using Monotonic = FsrsSharp.Middlewares.MonotonicInterval;

namespace FsrsSharp.Tests.Middlewares.MonotonicInterval;

/// <summary>Port of <c>middlewares/monotonic-interval/middleware.spec.ts</c>.</summary>
public class MonotonicIntervalMiddlewareTests
{
    private const double MaximumInterval = 100;

    private static readonly Dictionary<Rating, double> LongTermIntervals = new()
    {
        [Rating.Again] = 2,
        [Rating.Hard] = 4,
        [Rating.Good] = 3,
        [Rating.Easy] = 7,
    };

    /// <summary>A hand-built <see cref="NextIntervalContext{TState,TTime}"/> with a recording candidate (upstream's <c>createReviewContext</c>).</summary>
    private sealed class ReviewHarness
    {
        public ReviewHarness(
            Rating grade,
            IReadOnlyDictionary<Rating, double> intervals,
            State state = State.Review,
            double maximum = MaximumInterval,
            double? scheduledDays = null)
        {
            var scheduler = Fsrs6Definition(MonotonicIntervalMiddleware.Instance)
                .Create(Fsrs6Config().Set(MaximumIntervalMiddleware.MaximumInterval, maximum));
            var states = Grades.All.ToDictionary(r => r, r => new FsrsState((int)r, (int)r));
            var intervalByState = states.ToDictionary(p => p.Value, p => intervals[p.Key]);

            var candidate = new ReviewCandidate<FsrsState>(
                new DesiredRetentionByGrade(0.9),
                rating =>
                {
                    Steps.Add(rating);
                    return states[rating];
                },
                _ => null,
                (s, retention) =>
                {
                    Retentions.Add(retention);
                    return intervalByState[s];
                });

            var card = new Card<FsrsState>(new FsrsState(0, 0), state, ScheduleStatus.Review, FieldBag.Empty);
            Context = new NextIntervalContext<FsrsState, DateTime>(scheduler, card, grade, null, 9, candidate)
            {
                ScheduledDays = scheduledDays,
            };
            Next = () =>
            {
                NextCalls++;
                Context.ScheduledDays ??= intervals[grade];
            };
        }

        public NextIntervalContext<FsrsState, DateTime> Context { get; }
        public Action Next { get; }
        public List<Rating> Steps { get; } = [];
        public List<double> Retentions { get; } = [];
        public int NextCalls { get; private set; }

        public void Run() => MonotonicIntervalMiddleware.Instance.NextInterval(Context, Next);
    }

    // ------------------------------------------------------------------ handler

    [Theory]
    [InlineData(Rating.Again, 2, new Rating[0])]
    [InlineData(Rating.Hard, 4, new[] { Rating.Again })]
    [InlineData(Rating.Good, 5, new[] { Rating.Again, Rating.Hard })]
    [InlineData(Rating.Easy, 7, new[] { Rating.Again, Rating.Hard, Rating.Good })]
    public void UsesTheRequiredLongTermCandidatesForGrade(Rating grade, double expected, Rating[] expectedGrades)
    {
        var harness = new ReviewHarness(grade, LongTermIntervals);

        harness.Run();

        harness.Context.ScheduledDays.Should().Be(expected);
        harness.Steps.Should().Equal(expectedGrades);
        harness.Retentions.Should().HaveCount(expectedGrades.Length);
        harness.Retentions.Should().OnlyContain(r => r == 0.9);
        harness.NextCalls.Should().Be(1);
    }

    [Fact]
    public void NormalizesAnIntervalSelectedByAnEarlierMiddleware()
    {
        var harness = new ReviewHarness(Rating.Good, LongTermIntervals, scheduledDays: 2);

        harness.Run();

        harness.Context.ScheduledDays.Should().Be(5);
        harness.Steps.Should().Equal(Rating.Again, Rating.Hard);
        harness.NextCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(State.New)]
    [InlineData(State.Learning)]
    [InlineData(State.Review)]
    [InlineData(State.Relearning)]
    public void UsesTheFullRatingChainForCardState(State state)
    {
        var harness = new ReviewHarness(Rating.Easy, LongTermIntervals, state);

        harness.Run();

        harness.Context.ScheduledDays.Should().Be(7);
        harness.Steps.Should().Equal(Rating.Again, Rating.Hard, Rating.Good);
        harness.NextCalls.Should().Be(1);
    }

    [Fact]
    public void AllowsLaterRatingsToShareTheMaximumInterval()
    {
        var harness = new ReviewHarness(
            Rating.Easy,
            new Dictionary<Rating, double> { [Rating.Again] = 98, [Rating.Hard] = 99, [Rating.Good] = 100, [Rating.Easy] = 101 },
            maximum: 100);

        harness.Run();

        harness.Context.ScheduledDays.Should().Be(100);
    }

    // ------------------------------------------------------------------ integration

    [Fact]
    public void UsesTheDefaultMaximumIntervalWhenItIsOmitted()
    {
        var core = Fsrs6Definition(MonotonicIntervalMiddleware.Instance).Create(Fsrs6Config());

        core.Config.Get(MaximumIntervalMiddleware.MaximumInterval).Should().Be(36_500);
    }

    [Fact]
    public void KeepsLongTermReviewAndPreviewDueDatesAligned()
    {
        var baseScheduler = Fsrs6Definition().Create(Fsrs6Config());
        var core = Fsrs6Definition(MonotonicIntervalMiddleware.Instance)
            .Create(Fsrs6Config().Set(MaximumIntervalMiddleware.MaximumInterval, MaximumInterval));
        var baseCard = baseScheduler.NewCard(Now);
        var card = core.NewCard(Now);
        var raw = PreviewDays(baseScheduler, baseCard, Now);
        var expected = Monotonic.CalculateScheduleDays(raw, MaximumInterval);

        PreviewDays(core, card, Now).Should().Equal(expected);
        foreach (var (grade, index) in Grades.All.Select((g, i) => (g, i)))
            DueDays(core.Review(card, grade, Now).Card.DueAt, Now).Should().Be(expected[index]);
    }

    [Fact]
    public void UsesTheFullRatingChainWhenShortTermSchedulingIsEnabled()
    {
        var baseScheduler = Fsrs6Definition().Create(Fsrs6Config(true, 1));
        var core = Fsrs6Definition(MonotonicIntervalMiddleware.Instance)
            .Create(Fsrs6Config(true, 1).Set(MaximumIntervalMiddleware.MaximumInterval, MaximumInterval));
        var baseCard = baseScheduler.Review(baseScheduler.NewCard(Now), Rating.Easy, Now).Card;
        var card = core.Review(core.NewCard(Now), Rating.Easy, Now).Card;
        var raw = PreviewDays(baseScheduler, baseCard, Later);
        var expected = Monotonic.CalculateScheduleDays(raw, MaximumInterval);

        PreviewDays(core, card, Later).Should().Equal(expected);
        foreach (var (grade, index) in Grades.All.Select((g, i) => (g, i)))
            DueDays(core.Review(card, grade, Later).Card.DueAt, Later).Should().Be(expected[index]);
    }

    [Fact]
    public void UsesFuzzingOutputWhenFuzzingIsRegisteredBeforeMonotonic()
    {
        var config = () => Fsrs6Config()
            .Set(FuzzingMiddleware.EnableFuzz, true)
            .Set(FuzzingMiddleware.MaximumInterval, MaximumInterval);
        var fuzzOnlyCalls = 0;
        var combinedCalls = 0;
        var fuzzOnly = Fsrs6Definition(FuzzingMiddleware.Create(new FuzzingMiddlewareOptions
        {
            Rng = _ =>
            {
                fuzzOnlyCalls++;
                return () => 0;
            },
        })).Create(config());
        var core = Fsrs6Definition(
            FuzzingMiddleware.Create(new FuzzingMiddlewareOptions
            {
                Rng = _ =>
                {
                    combinedCalls++;
                    return () => 0;
                },
            }),
            MonotonicIntervalMiddleware.Instance).Create(config());
        const string cardId = "monotonic-fuzz";
        var fuzzOnlyCard = fuzzOnly.NewCard(Now, CardInput(cardId));
        var card = core.NewCard(Now, CardInput(cardId));
        var fuzzed = PreviewDays(fuzzOnly, fuzzOnlyCard, Now);
        var expected = Monotonic.CalculateScheduleDays(fuzzed, MaximumInterval);

        var preview = PreviewDays(core, card, Now);
        var direct = core.Review(card, Rating.Easy, Now);

        preview.Should().Equal(expected);
        DueDays(direct.Card.DueAt, Now).Should().Be(expected[3]);
        fuzzOnlyCalls.Should().BeGreaterThan(0);
        combinedCalls.Should().BeGreaterThan(0);
    }

    [Fact]
    public void LetsLearningStepsOwnShortTermAndGraduationIntervals()
    {
        var core = Fsrs6Definition(
            FuzzingMiddleware.Create(new FuzzingMiddlewareOptions { Rng = _ => () => 0.5 }),
            LearningStepsMiddleware.Instance,
            MonotonicIntervalMiddleware.Instance).Create(
            Fsrs6Config(enableShortTerm: true, numRelearningSteps: 1)
                .Set(FuzzingMiddleware.EnableFuzz, true)
                .Set(MaximumIntervalMiddleware.MaximumInterval, MaximumInterval)
                .Set(LearningStepsMiddleware.LearningSteps, ["1m", "10m"])
                .Set(LearningStepsMiddleware.RelearningSteps, ["10m"]));
        var card = core.NewCard(Now, CardInput("learning-step"));
        var preview = core.Preview(card, Now).ToDictionary(item => item.Grade);

        (preview[Rating.Again].Card.DueAt - Now).TotalMilliseconds.Should().Be(60_000);
        (preview[Rating.Hard].Card.DueAt - Now).TotalMilliseconds.Should().Be(5.5 * 60_000);
        (preview[Rating.Good].Card.DueAt - Now).TotalMilliseconds.Should().Be(10 * 60_000);
        preview[Rating.Easy].Card.State.Should().Be(State.Review);

        var learningCard = preview[Rating.Good].Card;
        var learningGraduation = core.Review(learningCard, Rating.Good, learningCard.DueAt);
        learningGraduation.Card.State.Should().Be(State.Review);
        DueDays(learningGraduation.Card.DueAt, learningCard.DueAt).Should().Be(2);

        var reviewCard = preview[Rating.Easy].Card;
        var relearningCard = core.Review(reviewCard, Rating.Again, reviewCard.DueAt).Card;
        relearningCard.State.Should().Be(State.Relearning);

        var relearningGraduation = core.Review(relearningCard, Rating.Good, relearningCard.DueAt);
        relearningGraduation.Card.State.Should().Be(State.Review);
        DueDays(relearningGraduation.Card.DueAt, relearningCard.DueAt).Should().Be(1);
    }

    public static TheoryData<string, string[], int, double, double[]> LearningIntervalCases() => new()
    {
        // Again 1m, Hard 5m, Good 1d, Easy 2d
        { "positive-minute", ["1m", "9m"], 1, 100, [1, 5, 1440, 2880] },
        // Again 1d, Hard 5m, Good 10m, Easy 2d
        { "mixed-minute", ["0m", "10m"], 0, 100, [1440, 5, 10, 2880] },
        // Again 1d, Hard 2d, Good 3d, Easy 4d
        { "zero-minute", ["0m"], 0, 100, [1440, 2880, 4320, 5760] },
        // Again 0.4m, Hard 0.4m, Good 0.4m, Easy 1d
        { "sub-minute", ["0.4m", "0.4m"], 0, 100, [0.4, 0.4, 0.4, 1440] },
        // Again 1d, Hard 2d, Good 3d, Easy 4d
        { "empty", [], 0, 100, [1440, 2880, 4320, 5760] },
        // Again 1d, Hard 2d, Good 3d, Easy 4d
        { "exhausted", ["1m"], 1, 100, [1440, 2880, 4320, 5760] },
        // Again 1m, Hard 5m, Good 1d, Easy capped at 1d
        { "maximum interval", ["1m", "9m"], 1, 1, [1, 5, 1440, 1440] },
    };

    [Theory]
    [MemberData(nameof(LearningIntervalCases))]
    public void KeepsLearningIntervalsMonotonic(string name, string[] learningSteps, int learningStep, double maximum, double[] expected)
    {
        _ = name;
        var learningCore = Fsrs6Definition(LearningStepsMiddleware.Instance).Create(
            Fsrs6Config(enableShortTerm: true, numRelearningSteps: 1)
                .Set(LearningStepsMiddleware.LearningSteps, ["1m", "10m"])
                .Set(LearningStepsMiddleware.RelearningSteps, ["10m"]));
        var monotonicCore = Fsrs6Definition(LearningStepsMiddleware.Instance, MonotonicIntervalMiddleware.Instance).Create(
            Fsrs6Config(enableShortTerm: true, numRelearningSteps: learningSteps.Length)
                .Set(MaximumIntervalMiddleware.MaximumInterval, maximum)
                .Set(LearningStepsMiddleware.LearningSteps, learningSteps)
                .Set(LearningStepsMiddleware.RelearningSteps, learningSteps));
        var learningCard = learningCore.Review(learningCore.NewCard(Now), Rating.Again, Now).Card;
        var reviewTime = learningCard.DueAt;
        var input = learningCard with { Fields = learningCard.Fields.With(LearningStepsMiddleware.LearningStepField, learningStep) };

        monotonicCore.Preview(input, reviewTime)
            .Select(item => (item.Card.DueAt - reviewTime).TotalMilliseconds / 60_000)
            .Should().Equal(expected);
    }
}
