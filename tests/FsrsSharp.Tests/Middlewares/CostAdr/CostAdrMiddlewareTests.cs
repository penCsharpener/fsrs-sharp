using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs6;
using FsrsSharp.Models.Fsrs7;
using FsrsSharp.Tests.Kit;
using Xunit;
using static FsrsSharp.Tests.Middlewares.FuzzMonotonicTestSchedulers;

namespace FsrsSharp.Tests.Middlewares.CostAdr;

/// <summary>Port of <c>middlewares/cost-adr/middleware.spec.ts</c>.</summary>
public class CostAdrMiddlewareTests
{
    private static readonly CostAdrPolicy Policy = CostAdrConstants.DefaultPolicy;
    private static readonly DateTime Today = new(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Fsrs7State State7 = new(Stability: 10, StabilityFast: 10, Difficulty: 5);
    private static readonly Rating[] Ratings = [Rating.Again, Rating.Hard, Rating.Good, Rating.Easy];

    private static FieldBag.Builder CostConfig(FieldBag.Builder builder, CostAdrPolicy? policy = null) =>
        builder
            .Set(CostAdrMiddleware.Policy, policy ?? Policy)
            .Set(CostAdrMiddleware.GoalCostWeight, CostAdrConstants.DefaultGoalCostWeight)
            .Set(KitSchemas.FractionalDays, true);

    private static FieldBag.Builder Config7(CostAdrPolicy? policy = null) => CostConfig(Fsrs7Config(), policy);

    private static DateTime AddDays(DateTime time, double days) =>
        new(time.Ticks + (long)Math.Truncate(days * 86_400_000) * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

    private static double Evaluate(FsrsState state, double weight = 64, CostAdrPolicy? policy = null) =>
        CostAdrCore.EvaluateRetention(policy ?? Policy, state, weight);

    private sealed class RetentionOverrideMiddleware(string name = "retention-override") : Middleware
    {
        public override string Name => name;

        public override void NextInterval<TState, TTime>(NextIntervalContext<TState, TTime> context, Action next)
        {
            context.Candidate.DesiredRetention[context.Grade] = 0.8;
            next();
        }
    }

    private sealed class SetScheduledDaysAfterNextMiddleware(double? value) : Middleware
    {
        public override string Name => "missing-interval";

        public override void NextInterval<TState, TTime>(NextIntervalContext<TState, TTime> context, Action next)
        {
            next();
            context.ScheduledDays = value;
        }
    }

    private sealed class ObserveRetentionMapMiddleware : Middleware
    {
        public List<DesiredRetentionByGrade> Seen { get; } = [];

        public override string Name => "observe-retention-map";

        public override void NextInterval<TState, TTime>(NextIntervalContext<TState, TTime> context, Action next)
        {
            var initial = context.Candidate.DesiredRetention;
            next();
            context.Candidate.DesiredRetention.Should().BeSameAs(initial);
            Seen.Add(context.Candidate.DesiredRetention);
        }
    }

    /// <summary>Counts evaluator calls on the current thread.</summary>
    private sealed class EvaluationCounter : IDisposable
    {
        public EvaluationCounter() => CostAdrCore.EvaluationObserver = () => Count++;

        public int Count { get; private set; }

        public void Dispose() => CostAdrCore.EvaluationObserver = null;
    }

    // ------------------------------------------------------------------ policy

    // Produced by compiling the evaluator extracted from that Rust revision.
    // .NET uses f64; the reference implementation uses f32.
    [Theory]
    [InlineData(0.0001, 1, 0, 0.61252141)]
    [InlineData(0.01, 1, 64, 0.8736233711)]
    [InlineData(1, 5, 64, 0.9325417876)]
    [InlineData(10, 5, 64, 0.9510779381)]
    [InlineData(1000, 10, 64, 0.9755957723)]
    [InlineData(36500, 10, 1024, 0.9783571959)]
    [InlineData(0, -1, 0, 0.61252141)]
    [InlineData(1000000, 20, 2048, 0.9783571959)]
    public void MatchesRust(double stability, double difficulty, double weight, double retention)
    {
        CostAdrCore.EvaluateRetention(Policy, new FsrsState(stability, difficulty), weight).Should().BeApproximately(retention, 5e-7);
    }

    [Fact]
    public void SupportsCustomBoundsAndClampsCostWeightsAtBothEnds()
    {
        var custom = Policy with
        {
            CostWeightMin = 4,
            CostWeightMax = 128,
            RetentionMin = 0.4,
            RetentionMax = 0.95,
            Bounds = new CostAdrBounds(0.1, 1000, 2, 8),
        };

        Evaluate(State7, 32, custom).Should().BeApproximately(0.9057754278, 5e-7);
        Evaluate(State7, 0, custom).Should().Be(Evaluate(State7, 4, custom));
        Evaluate(State7, 2048, custom).Should().Be(Evaluate(State7, 128, custom));
    }

    [Fact]
    public void KeepsRetentionFiniteForExtremeLogitsAndDecreasesItWithCost()
    {
        foreach (var baseValue in new[] { -64d, 0, 64 })
        {
            foreach (var effect in new[] { -64d, -20, 0, 20, 64 })
            {
                var coefficients = new double[15];
                coefficients[0] = baseValue;
                coefficients[5] = effect;
                coefficients[10] = effect;
                var extreme = Policy with { Coefficients = coefficients };
                var low = Evaluate(State7, 0, extreme);
                var high = Evaluate(State7, 1024, extreme);
                high.Should().BeGreaterThanOrEqualTo(Policy.RetentionMin);
                low.Should().BeLessThanOrEqualTo(Policy.RetentionMax);
                high.Should().BeLessThanOrEqualTo(low);
            }
        }
        Evaluate(State7, 0).Should().BeGreaterThan(Evaluate(State7, 1024));
    }

    // ------------------------------------------------------------------ middleware

    [Theory]
    [InlineData(Rating.Again)]
    [InlineData(Rating.Hard)]
    [InlineData(Rating.Good)]
    [InlineData(Rating.Easy)]
    public void QueriesOnlyGradeAndItsPredecessors(Rating grade)
    {
        var spy = new SpyModel<Fsrs7State>(Fsrs7Model.Instance);
        var core = SchedulerDefinition.Define(spy, DateTimeChrono.Instance)
            .Use(CostAdrMiddleware.Instance, MonotonicIntervalMiddleware.Instance)
            .Create(Config7());
        var card = core.NewCard(Today);
        using var evaluations = new EvaluationCounter();
        spy.Clear();

        core.NextInterval(State7, 0.7, new NextIntervalQuery<Fsrs7State>(card, grade, 0.25));

        evaluations.Count.Should().Be((int)grade);
        spy.Steps.Select(s => s.Rating).Should().Equal(Ratings.Where(r => r < grade));
        spy.NextIntervals.Should().HaveCount((int)grade);
        spy.ForgettingCurves.Should().Be(grade == Rating.Again ? 0 : 1);
    }

    [Fact]
    public void OnlyExtendsTheRetentionCacheAsALazyPreviewIsConsumed()
    {
        var spy = new SpyModel<Fsrs7State>(Fsrs7Model.Instance);
        var core = SchedulerDefinition.Define(spy, DateTimeChrono.Instance)
            .Use(CostAdrMiddleware.Instance, MonotonicIntervalMiddleware.Instance)
            .Create(Config7());
        var card = core.NewCard(Today);
        using var evaluations = new EvaluationCounter();
        spy.Clear();
        var preview = core.Preview(card, Today);
        evaluations.Count.Should().Be(0);
        spy.Steps.Should().BeEmpty();
        spy.NextIntervals.Should().BeEmpty();

        preview[Rating.Again].Grade.Should().Be(Rating.Again);
        evaluations.Count.Should().Be(1);
        spy.Steps.Select(s => s.Rating).Should().Equal(Rating.Again);
        preview[Rating.Hard].Grade.Should().Be(Rating.Hard);
        evaluations.Count.Should().Be(2);
        spy.Steps.Select(s => s.Rating).Should().Equal(Rating.Again, Rating.Hard);

        preview[Rating.Again].Grade.Should().Be(Rating.Again);
        evaluations.Count.Should().Be(2);
        var first = preview.ToArray();
        preview.ToArray().Should().Equal(first);
        evaluations.Count.Should().Be(4);
        spy.Steps.Should().HaveCount(4);
        spy.NextIntervals.Should().HaveCount(4);
    }

    [Fact]
    public void RejectsACandidateFromANonFsrsModelBeforeScheduling()
    {
        var config = KitFixtures.ConfigBuilder()
            .Set(CostAdrMiddleware.Policy, Policy)
            .Set(CostAdrMiddleware.GoalCostWeight, CostAdrConstants.DefaultGoalCostWeight);
        var scheduler = KitFixtures.NumericDefinition().Use(CostAdrMiddleware.Instance).Create(config);
        var nextCalls = 0;
        var intervalCalls = 0;
        var candidate = new ReviewCandidate<Sm2State>(
            new DesiredRetentionByGrade(0.9),
            _ => new Sm2State(1, 2.5, 1),
            _ => Rating.Good,
            (_, _) =>
            {
                intervalCalls++;
                return 1;
            });
        var card = new Card<Sm2State>(new Sm2State(0, 2.5, 0), State.New, ScheduleStatus.New, FieldBag.Empty);
        var context = new NextIntervalContext<Sm2State, double>(scheduler, card, Rating.Good, null, 0, candidate);

        var act = () => CostAdrMiddleware.Instance.NextInterval(context, () => nextCalls++);

        act.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS memory state");
        nextCalls.Should().Be(0);
        intervalCalls.Should().Be(0);
    }

    private static void MatchesRustConstantRetention<TState>(
        SchedulerDefinition<TState, DateTime> definition,
        FieldBag.Builder config,
        Func<double, double, double, TState> makeState)
        where TState : FsrsState
    {
        // cost_adr.rs: constant_retention(0.9) and test_cost_adr_next_states_matches_constant_retention.
        var ratio = (0.9 - 0.3) / (0.995 - 0.3);
        var coefficients = new double[15];
        coefficients[0] = Math.Log(ratio / (1 - ratio));
        coefficients[5] = -40;
        coefficients[10] = -40;
        var constantPolicy = Policy with { Coefficients = coefficients };
        var core = definition.Create(config.Set(CostAdrMiddleware.Policy, constantPolicy));

        TState?[] memoryStates = [null, makeState(7, 5, 7), makeState(7, 3, 1.5)];
        foreach (var memoryState in memoryStates)
        {
            foreach (var elapsedDays in new[] { 0.25, 7 })
            {
                var newCard = core.NewCard(Today);
                var card = memoryState is null ? newCard : newCard with { MemoryState = memoryState };
                foreach (var grade in Ratings)
                {
                    var nextState = core.Model.Step(new ModelStepInput<TState>(card.MemoryState, grade, elapsedDays));
                    var expected = core.Model.NextInterval(nextState, 0.9);
                    var interval = core.NextInterval(nextState, 0.9, new NextIntervalQuery<TState>(card, grade, elapsedDays));
                    Math.Abs(interval - expected).Should().BeLessThan(1e-3);
                    Math.Abs(CostAdrCore.EvaluateRetention(constantPolicy, nextState, 64) - 0.9).Should().BeLessThan(1e-4);
                }
            }
        }
    }

    [Fact]
    public void MatchesRustConstantRetentionScenariosFsrs6() =>
        MatchesRustConstantRetention(
            Fsrs6Definition(CostAdrMiddleware.Instance),
            CostConfig(Fsrs6Config(true, 1)),
            (s, d, _) => new FsrsState(s, d));

    [Fact]
    public void MatchesRustConstantRetentionScenariosFsrs7() =>
        MatchesRustConstantRetention(
            Fsrs7Definition(CostAdrMiddleware.Instance),
            CostConfig(Fsrs7Config().Set(Fsrs6Model.EnableShortTerm, true).Set(Fsrs6Model.NumRelearningSteps, 1)),
            (s, d, f) => new Fsrs7State(s, f, d));

    private static void UsesPostRatingState<TState>(Scheduler<TState, DateTime> core)
        where TState : FsrsState
    {
        var card = core.NewCard(Today);
        foreach (var result in core.Preview(card, Today))
        {
            var retention = CostAdrCore.EvaluateRetention(Policy, result.Card.MemoryState.Stability, result.Card.MemoryState.Difficulty, 64);
            var expected = core.Model.NextInterval(result.Card.MemoryState, retention);
            result.Card.DueAt.Should().Be(AddDays(Today, expected));
            core.Review(card, result.Grade, Today).Card.Should().Be(result.Card);
            core.Forward([new SchedulerReview<DateTime>(result.Grade, Today)], card)[0].Card.Should().Be(result.Card);
            core.NextInterval(result.Card.MemoryState, 0.9, new NextIntervalQuery<TState>(card, result.Grade, 0)).Should().Be(expected);
        }
    }

    [Fact]
    public void UsesPostRatingStateInReviewPreviewForwardAndQueriesFsrs6() =>
        UsesPostRatingState(Fsrs6Definition(CostAdrMiddleware.Instance).Create(CostConfig(Fsrs6Config(true, 1))));

    [Fact]
    public void UsesPostRatingStateInReviewPreviewForwardAndQueriesFsrs7() =>
        UsesPostRatingState(Fsrs7Definition(CostAdrMiddleware.Instance).Create(CostConfig(Fsrs7Config().Set(Fsrs6Model.EnableShortTerm, true).Set(Fsrs6Model.NumRelearningSteps, 1))));

    [Fact]
    public void PassesTheSuppliedFsrs7StateWithoutSteppingTheCurrentGrade()
    {
        // Upstream additionally asserts that the chrono clock is not read; the chrono core cannot be spied here.
        var spy = new SpyModel<Fsrs7State>(Fsrs7Model.Instance);
        var core = SchedulerDefinition.Define(spy, DateTimeChrono.Instance).Use(CostAdrMiddleware.Instance).Create(Config7());
        var card = core.NewCard(Today);
        var expectedRetention = Evaluate(State7);
        spy.Clear();

        var result = core.NextInterval(State7, 0.7, new NextIntervalQuery<Fsrs7State>(card, Rating.Good, 0.25));

        result.Should().BeGreaterThan(0);
        spy.NextIntervals.Should().ContainSingle().Which.Should().Be((State7, expectedRetention));
        spy.Steps.Select(s => s.Rating).Should().Equal(Rating.Again, Rating.Hard);
        spy.ForgettingCurves.Should().Be(1);
    }

    [Fact]
    public void UsesTheEffectivePerRatingRetentionAfterDownstreamOverrides()
    {
        var core = Fsrs7Definition(CostAdrMiddleware.Instance, new RetentionOverrideMiddleware()).Create(Config7());
        var card = core.NewCard(Today);

        core.NextInterval(State7, 0.9, new NextIntervalQuery<Fsrs7State>(card, Rating.Good, 0))
            .Should().Be(core.Model.NextInterval(State7, 0.8));
    }

    [Fact(Skip = "needs the maximum-interval middleware (model interval 0, 1/1440 and 10 days capped at 3)")]
    public void AppliesOnlyTheUpperCapToModelInterval() { }

    [Fact(Skip = "needs the maximum-interval middleware")]
    public void CapsDueTimesWithoutChangingThePostRatingMemoryState() { }

    [Fact(Skip = "needs the maximum-interval middleware")]
    public void UsesMaximumIntervalMiddlewareForAReviewedFsrs7State() { }

    [Fact(Skip = "needs the maximum-interval middleware")]
    public void CapsEveryGradeAfterMonotonicComparisonInTheDocumentedOrder() { }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    public void RejectsInvalidIntervalAfterDownstreamMiddleware(double? scheduledDays)
    {
        var core = Fsrs7Definition(CostAdrMiddleware.Instance, new SetScheduledDaysAfterNextMiddleware(scheduledDays)).Create(Config7());
        var card = core.NewCard(Today);

        var act = () => core.NextInterval(State7, 0.9, new NextIntervalQuery<Fsrs7State>(card, Rating.Good, 0));

        act.Should().Throw<FsrsValidationException>().WithMessage("*scheduledDays*");
    }

    [Fact]
    public void UsesDownstreamPerRatingRetentionForSiblingComparisons()
    {
        var spy = new SpyModel<Fsrs7State>(Fsrs7Model.Instance);
        var core = SchedulerDefinition.Define(spy, DateTimeChrono.Instance)
            .Use(CostAdrMiddleware.Instance, new RetentionOverrideMiddleware("retention-override-with-siblings"), MonotonicIntervalMiddleware.Instance)
            .Create(Config7());
        var card = core.NewCard(Today);
        spy.Clear();

        core.NextInterval(State7, 0.9, new NextIntervalQuery<Fsrs7State>(card, Rating.Good, 0));

        spy.NextIntervals.Should().HaveCount(3);
        foreach (var (memoryState, retention) in spy.NextIntervals)
            retention.Should().Be(memoryState.Stability == State7.Stability ? 0.8 : Evaluate(memoryState));
    }

    [Fact]
    public void SharesOneCachedRetentionRecordAcrossGradesAndRepeatedPreviewConsumption()
    {
        var observer = new ObserveRetentionMapMiddleware();
        using var evaluations = new EvaluationCounter();
        var core = Fsrs7Definition(observer, CostAdrMiddleware.Instance).Create(Config7());

        var preview = core.Preview(core.NewCard(Today), Today);
        var first = preview.ToArray();
        preview.ToArray().Should().Equal(first);

        evaluations.Count.Should().Be(4);
        // Upstream re-runs the pipeline on every enumeration (8 observations); the C# preview caches
        // each grade's result, so the second enumeration observes nothing new.
        observer.Seen.Should().HaveCount(4);
        observer.Seen.Should().OnlyContain(retentions => ReferenceEquals(retentions, observer.Seen[0]));
    }

    [Fact]
    public void ComparesEachRatingAtItsOwnRetentionWhileSharingThePreviewCache()
    {
        double[] coefficients = [32, 0, -64, 0, 0, -40, 0, 0, 0, 0, -40, 0, 0, 0, 0];
        var spy = new SpyModel<Fsrs7State>(Fsrs7Model.Instance);
        var core = SchedulerDefinition.Define(spy, DateTimeChrono.Instance)
            .Use(CostAdrMiddleware.Instance, MonotonicIntervalMiddleware.Instance)
            .Create(Config7(Policy with { Coefficients = coefficients }).SetRaw36500());
        var card = core.NewCard(Today);
        using var evaluations = new EvaluationCounter();
        spy.Clear();

        var preview = core.Preview(card, Today);
        var first = preview.ToArray();
        for (var index = 1; index < first.Length; index++)
            first[index].Card.DueAt.Should().BeOnOrAfter(first[index - 1].Card.DueAt);
        preview.ToArray().Should().Equal(first);
        evaluations.Count.Should().Be(4);
        spy.Steps.Should().HaveCount(4);
        spy.NextIntervals.Should().HaveCount(4);

        foreach (var result in first)
        {
            var query = core.NextInterval(result.Card.MemoryState, 0.9, new NextIntervalQuery<Fsrs7State>(card, result.Grade, 0));
            AddDays(Today, query).Should().Be(result.Card.DueAt);
        }
    }

    [Fact(Skip = "needs the learning-steps middleware")]
    public void PreservesFractionalElapsedDaysThroughTwoGoodReviewsAndTheThirdPreview() { }

    [Fact(Skip = "needs the learning-steps, scheduled-days and maximum-interval middlewares")]
    public void KeepsLearningDelaysAndExistingCandidateCachesWithFuzzingAndMonotonicIntervals() { }
}

internal static class CostAdrTestConfigExtensions
{
    public static FieldBag.Builder SetRaw36500(this FieldBag.Builder builder) =>
        builder.Set(MonotonicIntervalMiddleware.MaximumInterval, 36500d);
}
