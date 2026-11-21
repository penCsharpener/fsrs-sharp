using Xunit;
using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs7;
using FsrsSharp.Tests.Middlewares;
using static FsrsSharp.Tests.Scheduler.SchedulerTestSupport;

namespace FsrsSharp.Tests.Scheduler;

/// <summary>
/// Port of ts-fsrs <c>scheduler/next-interval.spec.ts</c>. Not ported: <c>Date</c>/<c>Date.now</c> proxy spies (no
/// C# equivalent; the chrono spies cover the same property) and the candidate-setter installation counts (the
/// candidate's <c>NextInterval</c> is a plain property here).
/// </summary>
public class NextIntervalTests
{
    private static readonly DateTime Now0 = new(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Before = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static double DaysBetween(DateTime later, DateTime earlier) => MsBetween(later, earlier) / Day;

    private static FieldBag CardInput(object cardId) => MiddlewareTestSchedulers.CardInput(cardId);

    [Fact]
    public void PreservesOnionOrderBetweenIntervalPoliciesAndReviewWritebacks()
    {
        var clamp = new ProbeMiddleware<Fsrs7State, DateTime>("interval-clamp")
        {
            OnNextInterval = (ctx, next) =>
            {
                next();
                ctx.ScheduledDays = Math.Min(ctx.ScheduledDays ?? 0, 2);
            },
        };
        var rewrite = new ProbeMiddleware<Fsrs7State, DateTime>("review-writeback")
        {
            OnReview = (ctx, next) =>
            {
                next();
                ctx.ScheduledDays = 10;
            },
        };

        foreach (var (middlewares, expected) in new (Middleware[], double)[]
                 {
                     ([clamp, rewrite], 2),
                     ([rewrite, clamp], 10),
                 })
        {
            var core = MiddlewareTestSchedulers.Fsrs7Definition(middlewares).Create(MiddlewareTestSchedulers.Fsrs7Config());
            var card = core.NewCard(Now0);
            var review = core.Review(card, Rating.Easy, Now0);

            DaysBetween(review.Card.DueAt, Now0).Should().Be(expected);
            core.Preview(card, Now0).Select(item => DaysBetween(item.Card.DueAt, Now0)).Should().Equal(expected, expected, expected, expected);
        }
    }

    [Fact]
    public void SharesIntervalHooksWhileKeepingReviewEffectsOutOfQueries()
    {
        int review = 0, nextInterval = 0, queryOnly = 0;
        var query = new ProbeMiddleware<Fsrs7State, DateTime>("query-only")
        {
            OnNextInterval = (ctx, next) =>
            {
                queryOnly++;
                ctx.ScheduledDays = 42;
                next();
            },
        };
        var operations = new ProbeMiddleware<Fsrs7State, DateTime>("operation-handlers")
        {
            OnReview = (_, next) =>
            {
                review++;
                next();
            },
            OnNextInterval = (_, next) =>
            {
                nextInterval++;
                next();
            },
        };
        var core = MiddlewareTestSchedulers.Fsrs7Definition(query, operations).Create(MiddlewareTestSchedulers.Fsrs7Config());
        var card = core.NewCard(Now0);
        var result = core.Review(card, Rating.Good, Now0);
        _ = core.Preview(card, Now0).ToList();
        core.Forward([new SchedulerReview<DateTime>(Rating.Good, Now0)], card);
        (review, nextInterval, queryOnly).Should().Be((6, 6, 6));

        core.NextInterval(result.Card.MemoryState, 0.9, new NextIntervalQuery<Fsrs7State>(card, Rating.Good, 0)).Should().Be(42);
        (review, nextInterval, queryOnly).Should().Be((6, 7, 7));
    }

    public static TheoryData<FsrsVersion> QueryParityVersions => [FsrsVersion.Fsrs6, FsrsVersion.Fsrs7];

    public static IEnumerable<string[]> LearningStepSets =>
    [
        ["1m", "10m"],
        ["0m", "10m"],
        ["0m"],
        [],
    ];

    [Theory]
    [MemberData(nameof(QueryParityVersions))]
    public void MatchesTheFinalReviewDelayAcrossPolicies(FsrsVersion version)
    {
        foreach (var enableFuzz in new[] { false, true })
        foreach (var enableShortTerm in new[] { false, true })
        foreach (var maximumInterval in new double[] { 1, 36500 })
        foreach (var learningSteps in LearningStepSets)
        {
            var core = DefaultScheduler.Create(version, new DefaultSchedulerOptions
            {
                EnableFuzz = enableFuzz,
                EnableShortTerm = enableShortTerm,
                MaximumInterval = maximumInterval,
                LearningSteps = learningSteps,
                RelearningSteps = learningSteps,
            });
            foreach (var state in AllStates)
            foreach (var learningStep in new[] { 0, 1 })
            {
                var created = core.NewCard(Before, CardInput("query-parity"));
                FsrsState memoryState = state == State.New
                    ? created.MemoryState
                    : version == FsrsVersion.Fsrs7 ? new Fsrs7State(10, 0.1, 5) : new FsrsState(10, 5);
                var card = created with
                {
                    State = state,
                    MemoryState = memoryState,
                    Fields = created.Fields
                        .With(LearningStepsMiddleware.LearningStepField, learningStep)
                        .With(DateTimeChrono.LastReviewAt, state == State.New ? null : (DateTime?)Before),
                };
                var elapsedDays = state == State.New ? 0 : 9;
                foreach (var grade in AllGrades)
                {
                    var result = core.Review(card, grade, Now0);
                    var interval = core.NextInterval(result.Card.MemoryState, 0.9, new NextIntervalQuery<FsrsState>(card, grade, elapsedDays));
                    ((long)Math.Truncate(Ms(Now0) + interval * Day)).Should().Be(Ms(result.Card.DueAt));
                }
            }
        }
    }

    [Fact]
    public void ReusesTheSuppliedFsrs7StateWithoutReviewOrChronologyEffects()
    {
        int review = 0, rollback = 0;
        var spy = new SpyModel<Fsrs7State>(Fsrs7Model.Instance);
        var chrono = new CountingChrono(DateTimeChrono.Instance);
        var effects = new EffectsMiddleware(() => review++, () => rollback++);
        var core = DefaultScheduler.Apply(SchedulerDefinition.Define<Fsrs7State, DateTime>(spy, chrono)).Use(effects)
            .Create(DefaultScheduler.BuildConfig(
                FsrsVersion.Fsrs7,
                new DefaultSchedulerOptions
                {
                    EnableFuzz = false,
                    EnableShortTerm = false,
                    LearningSteps = [],
                    RelearningSteps = [],
                    MaximumInterval = 100,
                }));
        var card = core.NewCard(Now0);
        var state = new Fsrs7State(11, 0.1, 4);
        var expected = Math.Min(core.Model.NextInterval(state, 0.8), core.Config.Get(MaximumIntervalMiddleware.MaximumInterval));
        spy.Clear();
        chrono.Reset();

        core.NextInterval(state, 0.8, new NextIntervalQuery<Fsrs7State>(card, Rating.Again, 0)).Should().Be(expected);

        spy.NextIntervals.Should().ContainSingle().Which.Should().Be((state, 0.8));
        spy.Steps.Should().BeEmpty();
        spy.ForgettingCurves.Should().Be(0);
        review.Should().Be(0);
        rollback.Should().Be(0);
        chrono.Calls.Should().Be(0);
        card.Reps.Should().Be(0);
        card.LearningStep.Should().Be(0);
    }

    [Fact]
    public void LetsASharedIntervalPolicyOverrideRetentionUsingThePostRatingState()
    {
        var policy = 0;
        var spy = new SpyModel<Fsrs7State>(Fsrs7Model.Instance);
        var adr = new ProbeMiddleware<Fsrs7State, DateTime>("cost-adr")
        {
            OnNextInterval = (ctx, next) =>
            {
                var state = ctx.Candidate.Step(ctx.Grade);
                policy++;
                ctx.Candidate.DesiredRetention[ctx.Grade] = state.StabilityFast > 0 ? 0.7 : 0.8;
                next();
            },
        };
        var config = MiddlewareTestSchedulers.Fsrs7Config();
        config.Add(DesiredRetentionMiddleware.DesiredRetention, 0.9);
        var core = SchedulerDefinition.Define<Fsrs7State, DateTime>(spy, DateTimeChrono.Instance)
            .Use(DesiredRetentionMiddleware.Instance, adr)
            .Create(config);
        var card = core.NewCard(Now0);
        var result = core.Review(card, Rating.Good, Now0);
        var expected = core.Model.NextInterval(result.Card.MemoryState, 0.7);
        spy.Clear();

        core.NextInterval(result.Card.MemoryState, 0.95, new NextIntervalQuery<Fsrs7State>(card, Rating.Good, 0)).Should().Be(expected);

        Ms(result.Card.DueAt).Should().Be((long)Math.Truncate(Ms(Now0) + expected * Day));
        var call = spy.NextIntervals.Should().ContainSingle().Which;
        call.State.StabilityFast.Should().Be(result.Card.MemoryState.StabilityFast);
        call.Retention.Should().Be(0.7);
        policy.Should().Be(2);
        spy.Steps.Should().BeEmpty();
    }

    [Fact]
    public void SharesCandidateCachesAndInstalledDecoratorsThroughRepeatedPreviewConsumption()
    {
        HashSet<ReviewCandidate<Fsrs7State>> candidates = new(ReferenceEqualityComparer.Instance);
        HashSet<ReviewCandidate<Fsrs7State>> decorated = new(ReferenceEqualityComparer.Instance);
        List<(Rating? Grade, double Retention)> visits = [];
        var probe = new ProbeMiddleware<Fsrs7State, DateTime>("candidate-probe")
        {
            OnNextInterval = (ctx, next) =>
            {
                candidates.Add(ctx.Candidate);
                next();
            },
        };
        var overrideMiddleware = new ProbeMiddleware<Fsrs7State, DateTime>("interval-override")
        {
            OnNextInterval = (ctx, next) =>
            {
                var candidate = ctx.Candidate;
                if (decorated.Add(candidate))
                {
                    var interval = candidate.NextInterval;
                    candidate.NextInterval = (state, retention) =>
                    {
                        visits.Add((candidate.FindGrade(state), retention));
                        return interval(state, retention) + 1;
                    };
                }

                next();
            },
        };
        var samples = 0;
        List<string> seeds = [];
        var rng = new FuzzingRng(seed =>
        {
            seeds.Add(seed);
            return () =>
            {
                samples++;
                return 0.5;
            };
        });
        var spy = new SpyModel<Fsrs7State>(Fsrs7Model.Instance);
        var core = SchedulerDefinition.Define<Fsrs7State, DateTime>(spy, DateTimeChrono.Instance)
            .Use(
                probe,
                FuzzingMiddleware.Create(new FuzzingMiddlewareOptions { Rng = rng, FuzzingRange = [] }),
                LearningStepsMiddleware.Instance,
                overrideMiddleware,
                MaximumIntervalMiddleware.Instance,
                MonotonicIntervalMiddleware.Instance)
            .Create(MiddlewareTestSchedulers.Fsrs7Config()
                .Set(FuzzingMiddleware.EnableFuzz, true)
                .Set(LearningStepsMiddleware.EnableShortTerm, true)
                .Set(LearningStepsMiddleware.LearningSteps, [])
                .Set(LearningStepsMiddleware.RelearningSteps, [])
                .Set(MaximumIntervalMiddleware.MaximumInterval, 100.0));
        var card = core.NewCard(Now0, CardInput("cache"));
        spy.Clear();
        spy.NextIntervalOverride = (_, _) => 10;
        var preview = core.Preview(card, Now0);

        var first = preview.ToList();

        first.Select(item => DaysBetween(item.Card.DueAt, Now0)).Should().Equal(11, 12, 13, 14);
        spy.Steps.Should().HaveCount(4);
        spy.NextIntervals.Should().HaveCount(4);
        visits.Should().HaveCount(10);
        seeds.Should().HaveCount(10);
        samples.Should().Be(10);

        preview.ToList().Should().Equal(first);
        spy.Steps.Should().HaveCount(4);
        spy.NextIntervals.Should().HaveCount(4);
        // Deviation: upstream re-runs the review pipeline on every iteration (visits and samples double to 20);
        // SchedulePreview caches each grade's item, so the second enumeration does no work at all.
        visits.Should().HaveCount(10);
        samples.Should().Be(10);
        candidates.Should().HaveCount(1);

        var state = first[3].Card.MemoryState;
        spy.Steps.Clear();
        spy.NextIntervals.Clear();
        core.NextInterval(state, 0.8, new NextIntervalQuery<Fsrs7State>(card, Rating.Easy, 0)).Should().Be(14);
        spy.Steps.Select(step => step.Rating).Should().Equal(Rating.Again, Rating.Hard, Rating.Good);
        spy.NextIntervals.Should().HaveCount(4);
        visits.TakeLast(4).Select(visit => visit.Retention).Should().Equal(0.8, 0.8, 0.8, 0.8);
        candidates.Should().HaveCount(2);
        seeds.Should().OnlyContain(seed => seed == "cache1");
    }

    [Fact]
    public void ValidatesQueryInputsAndTheFinalIntervalWithoutRejectingZeroOrFractions()
    {
        var spy = new SpyModel<Fsrs7State>(Fsrs7Model.Instance);
        var core = DefaultScheduler.Apply(SchedulerDefinition.Define<Fsrs7State, DateTime>(spy, DateTimeChrono.Instance))
            .Create(DefaultScheduler.BuildConfig(FsrsVersion.Fsrs7, new DefaultSchedulerOptions { EnableShortTerm = false }));
        var card = core.NewCard(Now0);
        var context = new NextIntervalQuery<Fsrs7State>(card, Rating.Again, 0);

        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, -1, 0, 1 })
        {
            var act = () => core.NextInterval(card.MemoryState, invalid, context);
            act.Should().Throw<FsrsValidationException>();
        }

        foreach (var elapsedDays in new[] { double.NaN, double.PositiveInfinity, -1 })
        {
            var act = () => core.NextInterval(card.MemoryState, 0.9, context with { ElapsedDays = elapsedDays });
            act.Should().Throw<FsrsValidationException>();
        }

        var manual = () => core.NextInterval(card.MemoryState, 0.9, context with { Grade = Rating.Manual });
        manual.Should().Throw<FsrsValidationException>();
        var nan = () => core.NextInterval(new Fsrs7State(double.NaN, 0, 0), 0.9, context);
        nan.Should().Throw<FsrsValidationException>();

        foreach (var value in new[] { double.NaN, -1 })
        {
            spy.NextIntervalOverride = (_, _) => value;
            var act = () => core.NextInterval(card.MemoryState, 0.9, context);
            act.Should().Throw<FsrsValidationException>();
        }

        foreach (var value in new[] { 0, 1.0 / 1440 })
        {
            spy.NextIntervalOverride = (_, _) => value;
            core.NextInterval(card.MemoryState, 0.9, context).Should().Be(value);
        }
    }

    /// <summary>Counts every chronology operation a created scheduler performs.</summary>
    private sealed class CountingChrono(IChrono<DateTime> inner) : IChrono<DateTime>
    {
        private int _calls;

        public int Calls => _calls;

        public void Reset() => _calls = 0;

        public IReadOnlyList<FieldKey> ConfigKeys => inner.ConfigKeys;
        public IReadOnlyList<FieldKey> CardFields => inner.CardFields;
        public IReadOnlyList<FieldKey> RevlogFields => inner.RevlogFields;

        public ChronoTimes<DateTime> NormalizeReview(FieldBag cardFields, DateTime time) => inner.NormalizeReview(cardFields, time);

        public ChronoTimes<DateTime> NormalizeRollback(FieldBag revlogFields) => inner.NormalizeRollback(revlogFields);

        public void WriteCardDefaults(FieldBag.Builder card, FieldBag config, DateTime time, DateTime? previous) =>
            inner.WriteCardDefaults(card, config, time, previous);

        public void WriteRevlogDefaults(FieldBag.Builder revlog, FieldBag config, DateTime time, ChronoTimes<DateTime> previous) =>
            inner.WriteRevlogDefaults(revlog, config, time, previous);

        public IChronoCore<DateTime> Create(FieldBag config) => new Core(this, inner.Create(config));

        private sealed class Core(CountingChrono owner, IChronoCore<DateTime> core) : IChronoCore<DateTime>
        {
            public DateTime Now()
            {
                owner._calls++;
                return core.Now();
            }

            public int Compare(DateTime left, DateTime right)
            {
                owner._calls++;
                return core.Compare(left, right);
            }

            public double Difference(DateTime from, DateTime to)
            {
                owner._calls++;
                return core.Difference(from, to);
            }

            public DateTime Add(DateTime from, double days)
            {
                owner._calls++;
                return core.Add(from, days);
            }
        }
    }

    private sealed class EffectsMiddleware(Action onReview, Action onRollback) : Middleware
    {
        public override string Name => "effects";

        public override void Review<TState, TTime>(ReviewContext<TState, TTime> context, Action next)
        {
            onReview();
            next();
        }

        public override void Rollback<TState, TTime>(RollbackContext<TState, TTime> context, Action next)
        {
            onRollback();
            next();
        }
    }
}
