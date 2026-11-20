using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;
using Xunit;
using static FsrsSharp.Tests.Middlewares.FuzzMonotonicTestSchedulers;
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
                .Create(Fsrs6Config().Set(MonotonicIntervalMiddleware.MaximumInterval, maximum));
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

        core.Config.Get(MonotonicIntervalMiddleware.MaximumInterval).Should().Be(36_500);
    }

    [Fact]
    public void KeepsLongTermReviewAndPreviewDueDatesAligned()
    {
        var baseScheduler = Fsrs6Definition().Create(Fsrs6Config());
        var core = Fsrs6Definition(MonotonicIntervalMiddleware.Instance)
            .Create(Fsrs6Config().Set(MonotonicIntervalMiddleware.MaximumInterval, MaximumInterval));
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
            .Create(Fsrs6Config(true, 1).Set(MonotonicIntervalMiddleware.MaximumInterval, MaximumInterval));
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

    [Fact(Skip = "needs the learning-steps middleware")]
    public void LetsLearningStepsOwnShortTermAndGraduationIntervals() { }

    [Fact(Skip = "needs the learning-steps middleware (positive-minute, mixed-minute, zero-minute, sub-minute, empty, exhausted and maximum-interval cases)")]
    public void KeepsLearningIntervalsMonotonic() { }
}
