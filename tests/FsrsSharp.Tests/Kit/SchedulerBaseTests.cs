using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using Xunit;
using static FsrsSharp.Tests.Kit.KitFixtures;

namespace FsrsSharp.Tests.Kit;

/// <summary>
/// Port of srs-kit <c>scheduler/base.spec.ts</c> (create, newCard, review, middleware handlers,
/// preview, forward, runtime context). Skipped upstream tests exercise TS-only behaviour: setters and
/// <c>__proto__</c> fields on plain objects, <c>Object.freeze</c>, mutable review input, and schema
/// call-count spies.
/// </summary>
public partial class SchedulerBaseTests
{
    private static Scheduler<Sm2State, double> StatsScheduler(FieldBag? config = null, params Middleware[] more) =>
        NumericDefinition().Use([.. more, StatsMiddleware.Instance]).Create(config ?? Config);

    private static readonly Scheduler<Sm2State, double> Core = StatsScheduler();

    private static FieldBag SourceConfigBag(string source) =>
        ConfigBuilder().Set(SourceMiddleware.SourceConfig, source).ToFieldBag();

    private static FieldBag SourceInit(string source) =>
        new FieldBag.Builder().Set(SourceMiddleware.SourceInput, source).ToFieldBag();

    // ---------------------------------------------------------------- create

    [Fact]
    public void Create_ValidatesConfigAndReturnsCore()
    {
        var expected = ConfigBuilder().Set(StatsMiddleware.ClearStatsOnForget, true).ToFieldBag();
        Core.Config.ShouldBeBag(expected);
        Core.Definition.Model.Should().BeSameAs(Sm2Model.Instance);
        Core.Definition.Chrono.Should().BeSameAs(NumericChrono.Instance);
        Core.Model.Should().BeOfType<Sm2Core>().Which.Config.Should().BeEquivalentTo(new Sm2Config(Sm2Model.DefaultWeights));
        Core.Chrono.Now().Should().Be(0);
    }

    [Fact]
    public void Create_RejectsInvalidConfig()
    {
        var config = new FieldBag.Builder { { Sm2Model.Weights, (IReadOnlyList<double>)[1] } };
        var act = () => NumericDefinition().Use(StatsMiddleware.Instance).Create(config);
        act.Should().Throw<FsrsValidationException>().WithMessage("Expected SM2 config with 6 finite number weights");
        var missing = () => NumericDefinition().Create(FieldBag.Empty);
        missing.Should().Throw<FsrsValidationException>();
    }

    [Fact]
    public void Create_ParsesModelConfigExactlyOnce()
    {
        var model = new Sm2Model();
        SchedulerDefinition.Define(model, NumericChrono.Instance).Create(Config);
        model.ParseConfigCalls.Should().Be(1);
    }

    [Fact]
    public void Create_ValidatesChronoAndMiddlewareConfig()
    {
        var bad = ConfigBuilder();
        bad.SetRaw("fractionalDays", 1);
        var act = () => DateDefinition().Create(bad);
        act.Should().Throw<FsrsValidationException>();

        // Required middleware config key missing.
        var missing = () => NumericDefinition().Use(SourceMiddleware.Instance).Create(Config);
        missing.Should().Throw<FsrsValidationException>();

        // Config keys are filled with their defaults and only declared keys survive.
        var withExtra = ConfigBuilder();
        withExtra.SetRaw("unrelated", 1);
        var scheduler = DateDefinition().Create(withExtra);
        scheduler.Config.ShouldBeBag(ConfigBuilder().Set(KitSchemas.FractionalDays, false).ToFieldBag());
    }

    [Fact]
    public void Create_RunsMiddlewareConfigValidation()
    {
        var middleware = new LambdaMiddleware<double>("validates") { OnValidateConfig = _ => "bad cross-field config" };
        var act = () => NumericDefinition().Use(middleware).Create(Config);
        act.Should().Throw<FsrsValidationException>().WithMessage("bad cross-field config");
    }

    [Fact]
    public void Create_IsIsolatedPerDefinition()
    {
        var baseDefinition = NumericDefinition();
        var before = baseDefinition.Create(Config);
        var derived = baseDefinition.Use(SourceMiddleware.Instance, StatsMiddleware.Instance).Create(SourceConfigBag("derived-source"));
        var after = baseDefinition.Create(Config);

        var beforeReview = before.Review(before.NewCard(), Rating.Good, 1);
        var derivedReview = derived.Review(derived.NewCard(), Rating.Good, 1);
        var afterReview = after.Review(after.NewCard(), Rating.Good, 1);

        beforeReview.Card.Fields.Should().BeEmpty();
        derivedReview.Card.Get(SourceMiddleware.SourceCard).Should().Be("derived-source");
        derivedReview.Card.Reps.Should().Be(1);
        derivedReview.Card.Lapses.Should().Be(0);
        derivedReview.Revlog.Get(SourceMiddleware.Audit).Should().Be("derived-source");
        afterReview.Card.Fields.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- newCard

    [Fact]
    public void NewCard_UsesModelDefaults()
    {
        var card = Core.NewCard();
        card.State.Should().Be(State.New);
        card.MemoryState.Should().Be(new Sm2State(0, Sm2Model.DefaultWeights[2], 0));
        card.ScheduleStatus.Should().Be(ScheduleStatus.New);
        card.Reps.Should().Be(0);
        card.Lapses.Should().Be(0);
    }

    [Fact]
    public void NewCard_CreatesEqualButDistinctCards()
    {
        var first = Core.NewCard();
        var second = Core.NewCard();
        second.Should().NotBeSameAs(first);
        second.Should().Be(first);
    }

    [Fact]
    public void NewCard_UsesNowForDueDate()
    {
        var core = DateDefinition().Create(Config);
        var first = Utc(2026, 1, 1);
        var second = Utc(2026, 1, 2);
        core.NewCard(first).DueAt.Should().Be(first);
        core.NewCard(second).DueAt.Should().Be(second);
        core.NewCard().LastReviewAt.Should().BeNull();
    }

    [Fact]
    public void NewCard_ValidatesNow()
    {
        var core = DateDefinition().Create(Config);
        var act = () => core.NewCard(DateTime.UnixEpoch);
        act.Should().Throw<FsrsValidationException>().WithMessage("Expected valid Date");
        var numeric = () => Core.NewCard(double.NaN);
        numeric.Should().Throw<FsrsValidationException>().WithMessage("Expected finite number");
    }

    [Fact]
    public void NewCard_AppliesMiddlewareDefaultsAndInput()
    {
        var core = NumericDefinition().Use(SourceMiddleware.Instance, StatsMiddleware.Instance).Create(SourceConfigBag("test-source"));
        core.NewCard().Get(SourceMiddleware.SourceCard).Should().Be("test-source");
        core.NewCard(0, SourceInit("injected-source")).Get(SourceMiddleware.SourceCard).Should().Be("injected-source");

        var bad = new FieldBag.Builder();
        bad.SetRaw("source", 1);
        var act = () => core.NewCard(null, bad.ToFieldBag());
        act.Should().Throw<FsrsValidationException>();
    }

    [Fact]
    public void NewCard_CallsMiddlewareDefaultsForEachCard()
    {
        var calls = 0;
        var middleware = new LambdaMiddleware<double>("cached")
        {
            Config = [SourceMiddleware.SourceConfig],
            Card = [SourceMiddleware.SourceCard],
            OnCardDefaults = (ctx, card) =>
            {
                calls++;
                card.Set(SourceMiddleware.SourceCard, ctx.Config.Get(SourceMiddleware.SourceConfig));
            },
        };
        var core = NumericDefinition().Use(middleware, StatsMiddleware.Instance).Create(SourceConfigBag("cached-source"));
        var first = core.NewCard();
        var second = core.NewCard();
        second.Should().NotBeSameAs(first);
        second.Should().Be(first);
        calls.Should().Be(2);
    }

    [Fact]
    public void NewCard_RunsMiddlewareDefaultsInUseOrderWithTopLevelConfig()
    {
        var trace = new List<string>();
        var firstSource = new FieldKey<string>("firstSource", "first-default");
        var secondSource = new FieldKey<string>("secondSource", "second-default");
        LambdaMiddleware<double> Make(string name, FieldKey<string> key) => new(name)
        {
            Config = [key],
            OnCardDefaults = (ctx, _) => trace.Add($"{name}:{ctx.Config.Get(key)}"),
        };

        var config = ConfigBuilder().Set(firstSource, "first-config").Set(secondSource, "second-config");
        NumericDefinition().Use(Make("second", secondSource), Make("first", firstSource), StatsMiddleware.Instance)
            .Create(config).NewCard();
        trace.Should().Equal("second:second-config", "first:first-config");
    }

    // ---------------------------------------------------------------- candidate

    [Fact]
    public void Candidate_LetsReviewMiddlewareReadAndUpdateDesiredRetention()
    {
        var seen = new List<double>();
        var middleware = new LambdaMiddleware<double>("retention")
        {
            OnReview = (ctx, next) =>
            {
                seen.Add(ctx.Candidate.DesiredRetention[ctx.Grade]);
                ctx.Candidate.DesiredRetention[ctx.Grade] = 0.5;
                next();
                ctx.ScheduledDays.Should().NotBeNull();
                seen.Add(ctx.ScheduledDays!.Value);
            },
        };
        var core = StatsScheduler(null, middleware);
        var result = core.Review(core.NewCard(0), Rating.Good, 0);
        seen.Should().Equal(0.9, 7);
        result.Card.MemoryState.Interval.Should().Be(1);
    }

    [Fact]
    public void Candidate_LetsMiddlewareDeriveRetentionFromCandidateState()
    {
        var seen = new List<object>();
        var middleware = new LambdaMiddleware<double>("candidate")
        {
            OnReview = (ctx, next) =>
            {
                var state = ctx.Candidate.Step(ctx.Grade);
                seen.Add(state);
                ctx.Candidate.DesiredRetention[ctx.Grade] = state.Interval == 1 ? 0.5 : 0.9;
                seen.Add(ctx.Candidate.NextInterval(state, ctx.Candidate.DesiredRetention[ctx.Grade]));
                next();
            },
        };
        var core = StatsScheduler(null, middleware);
        var result = core.Review(core.NewCard(0), Rating.Good, 0);
        seen.Should().Equal(new Sm2State(1, 2.5, 1), 7.0);
        result.Card.MemoryState.Interval.Should().Be(1);
    }

    [Fact]
    public void Candidate_CachesNextIntervalByStateAndRetention()
    {
        var seen = new List<double>();
        var middleware = new LambdaMiddleware<double>("cache")
        {
            OnReview = (ctx, next) =>
            {
                var state = ctx.Candidate.Step(ctx.Grade);
                seen.Add(ctx.Candidate.NextInterval(state, 0.9));
                seen.Add(ctx.Candidate.NextInterval(state, 0.5));
                next();
            },
        };
        var core = StatsScheduler(null, middleware);
        core.Review(core.NewCard(0), Rating.Good, 0);
        seen.Should().Equal(1, 7);
    }

    [Fact]
    public void Candidate_FindsLatestGradeByReference()
    {
        var seen = new List<(bool Same, Rating? Previous, Rating? Hard, Rating? Cached)>();
        var middleware = new LambdaMiddleware<double>("previous-candidate")
        {
            OnReview = (ctx, next) =>
            {
                var again = ctx.Candidate.Step(Rating.Again);
                var againGrade = ctx.Candidate.FindGrade(again);
                var hard = ctx.Candidate.Step(Rating.Hard);
                var hardGrade = ctx.Candidate.FindGrade(hard);
                var cachedAgain = ctx.Candidate.Step(Rating.Again);
                seen.Add((ReferenceEquals(again, hard), againGrade, hardGrade, ctx.Candidate.FindGrade(cachedAgain)));
                next();
            },
        };
        var core = StatsScheduler(null, middleware);
        var constant = new Sm2State(1, 2.5, 1);
        core.Core().StepOverride = _ => constant;
        core.Review(core.NewCard(0), Rating.Good, 0);
        seen.Should().Equal((true, Rating.Again, Rating.Hard, Rating.Again));
    }

    [Fact]
    public void Candidate_CachesNextIntervalAcrossCumulativeMiddlewareInPreview()
    {
        var middleware = new LambdaMiddleware<double>("cumulative")
        {
            OnReview = (ctx, next) =>
            {
                foreach (var g in Grades.All)
                {
                    if (g > ctx.Grade) break;
                    ctx.Candidate.NextInterval(ctx.Candidate.Step(g), ctx.Candidate.DesiredRetention[g]);
                }

                next();
            },
        };
        var core = StatsScheduler(null, middleware);
        var card = core.NewCard(0);
        core.Core().IntervalCalls.Clear();
        foreach (var _ in core.Preview(card, 0)) { }

        // Middleware: Again=1, Hard=2, Good=3, Easy=4 calls, plus one per grade in the terminal step,
        // yet only four distinct (state, retention) pairs reach the model.
        core.Core().IntervalCalls.Should().HaveCount(4);
    }

    // ---------------------------------------------------------------- review

    [Fact]
    public void Review_TransitionsNewToReview()
    {
        foreach (var grade in new[] { Rating.Good, Rating.Again })
        {
            var result = Core.Review(Core.NewCard(), grade);
            result.Card.State.Should().Be(State.Review);
            result.Card.ScheduleStatus.Should().Be(ScheduleStatus.Review);
            result.Card.MemoryState.Interval.Should().BeGreaterThan(0);
            result.Card.Reps.Should().Be(1);
            result.Card.Lapses.Should().Be(0);
        }
    }

    [Fact]
    public void Review_KeepsReviewOnAgainAndIncrementsLapses()
    {
        var r1 = Core.Review(Core.NewCard(), Rating.Good, 0);
        var r2 = Core.Review(r1.Card, Rating.Again, r1.Card.MemoryState.Interval);
        r2.Card.State.Should().Be(State.Review);
        r2.Card.Lapses.Should().Be(1);
    }

    [Fact]
    public void Review_RecordsPreviousStateInRevlog()
    {
        var card = Core.NewCard();
        var result = Core.Review(card, Rating.Good);
        result.Revlog.Rating.Should().Be(Rating.Good);
        result.Revlog.State.Should().Be(State.New);
        result.Revlog.ScheduleStatus.Should().Be(ScheduleStatus.New);
        result.Revlog.MemoryState.Should().Be(card.MemoryState);
        result.Revlog.Fields.Should().BeEmpty();

        var plain = NumericScheduler();
        var plainResult = plain.Review(plain.NewCard(), Rating.Good);
        plainResult.Revlog.Rating.Should().Be(Rating.Good);
        plainResult.Revlog.State.Should().Be(State.New);
    }

    [Fact]
    public void Review_ChainsMultipleReviews()
    {
        var r1 = Core.Review(Core.NewCard(), Rating.Good, 0);
        var r2 = Core.Review(r1.Card, Rating.Good, r1.Card.MemoryState.Interval);
        var r3 = Core.Review(r2.Card, Rating.Good, r1.Card.MemoryState.Interval + r2.Card.MemoryState.Interval);
        r3.Card.Reps.Should().Be(3);
        r3.Card.State.Should().Be(State.Review);
        r3.Card.MemoryState.Interval.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Review_ValidatesInput()
    {
        var card = Core.NewCard();
        var badNow = () => Core.Review(card, Rating.Good, double.NaN);
        badNow.Should().Throw<FsrsValidationException>().WithMessage("Expected finite number");
        var badGrade = () => Core.Review(card, Rating.Manual, 0);
        badGrade.Should().Throw<FsrsValidationException>().WithMessage("Expected grade");
        var badCard = () => Core.Review(card with { MemoryState = new Sm2State(double.NaN, 1, 1) }, Rating.Good, 0);
        badCard.Should().Throw<FsrsValidationException>().WithMessage("Expected SM2 memory state");
        var noReps = () => Core.Review(card with { Fields = FieldBag.Empty }, Rating.Good, 0);
        noReps.Should().Throw<FsrsValidationException>();
        var badStatus = () => Core.Review(card with { ScheduleStatus = "suspend" }, Rating.Good, 0);
        badStatus.Should().Throw<FsrsValidationException>().WithMessage("Expected known scheduleStatus");
        var badState = () => Core.Review(card with { State = (State)9 }, Rating.Good, 0);
        badState.Should().Throw<FsrsValidationException>().WithMessage("Expected state");
    }

    [Fact]
    public void Review_ThrowsChronoNormalizationErrors()
    {
        var chrono = new TestChrono
        {
            OnNormalizeReview = (_, _) => throw new FsrsValidationException("normalization failed"),
        };
        var core = SchedulerDefinition.Define(Sm2Model.Instance, chrono).Create(Config);
        var card = core.NewCard(0);
        var act = () => core.Review(card, Rating.Good, 0);
        act.Should().Throw<FsrsValidationException>().WithMessage("normalization failed");
    }

    [Fact]
    public void Review_NewCardTimeIsParsedOnceThroughChronoDefaults()
    {
        var seen = new List<(double Time, double? Previous)>();
        var dueAt = new FieldKey<double>("dueAt");
        var chrono = new TestChrono
        {
            CardFields = [dueAt],
            OnNow = () => 10,
            OnWriteCardDefaults = (card, _, time, previous) =>
            {
                seen.Add((time, previous));
                card.Set(dueAt, time);
            },
        };
        var core = SchedulerDefinition.Define(Sm2Model.Instance, chrono).Create(Config);
        core.NewCard(1).Get(dueAt).Should().Be(1);
        core.NewCard().Get(dueAt).Should().Be(10);
        seen.Should().Equal((1, null), (10, null));
    }

    [Fact]
    public void Review_MiddlewareInjectsResultFields()
    {
        var core = NumericDefinition().Use(SourceMiddleware.Instance, StatsMiddleware.Instance).Create(SourceConfigBag("test-source"));
        var result = core.Review(core.NewCard(0), Rating.Good, 0);
        result.Card.Get(SourceMiddleware.SourceCard).Should().Be("test-source");
        result.Revlog.Get(SourceMiddleware.Audit).Should().Be("test-source");
    }

    // ---------------------------------------------------------------- middleware handlers

    [Fact]
    public void Handlers_RecordNormalizedDueDatesWithoutChangingElapsedDays()
    {
        var elapsed = new List<double>();
        var middleware = new LambdaMiddleware<DateTime>("capture-elapsed-days")
        {
            OnReview = (ctx, next) =>
            {
                elapsed.Add(ctx.ElapsedDays);
                next();
            },
        };
        var core = DateDefinition().Use(middleware).Create(Config);
        var createdAt = Utc(2026, 1, 1);
        var reviewedAt = Utc(2026, 1, 2);
        var now = Utc(2026, 1, 10);
        var card = core.NewCard(createdAt);
        card.LastReviewAt.Should().BeNull();
        var first = core.Review(card, Rating.Good, reviewedAt);

        var records = new List<ScheduleResult<Sm2State>> { core.Review(first.Card, Rating.Good, now) };
        records.AddRange(core.Preview(first.Card, now).Select(p => new ScheduleResult<Sm2State>(p.Card, p.Revlog)));
        records.AddRange(core.Forward([new SchedulerReview<DateTime>(Rating.Good, now)], first.Card));

        elapsed.Should().Equal(0, 8, 8, 8, 8, 8, 8);
        foreach (var result in records)
        {
            result.Revlog.DueAt.Should().Be(first.Card.DueAt);
            result.Revlog.LastReviewAt.Should().Be(first.Card.LastReviewAt);
            result.Revlog.ReviewTime.Should().Be(now);
            result.Card.LastReviewAt.Should().Be(now);
            core.Rollback(result.Card, result.Revlog).Should().Be(first.Card);
        }
    }

    [Fact]
    public void Handlers_ApplyChronoDefaultsAfterMiddlewareUnwinds()
    {
        var seen = new List<bool>();
        var middleware = new LambdaMiddleware<DateTime>("review-chrono-order")
        {
            OnReview = (ctx, next) =>
            {
                next();
                seen.Add(ctx.ResultCard.Fields.Contains(DateTimeChrono.DueAt));
                ctx.ScheduledDays = 5;
            },
        };
        var core = DateDefinition().Use(middleware).Create(Config);
        var card = core.NewCard(Utc(2026, 1, 1));
        var reviewedAt = Utc(2026, 1, 2);

        var result = core.Review(card, Rating.Good, reviewedAt);
        var preview = core.Preview(card, reviewedAt).ToList();

        seen.Should().HaveCount(5).And.AllSatisfy(s => s.Should().BeFalse());
        result.Card.DueAt.Should().Be(Utc(2026, 1, 7));
        preview.Select(p => p.Card.DueAt).Should().Equal(Enumerable.Repeat(Utc(2026, 1, 7), 4));
    }

    [Fact]
    public void Handlers_ShortCircuitingReviewMiddlewareMustProvideScheduledDays()
    {
        var middleware = new LambdaMiddleware<DateTime>("short-circuit") { OnReview = (_, _) => { } };
        var core = DateDefinition().Use(middleware).Create(Config);
        var now = Utc(2026, 1, 1);
        var card = core.NewCard(now);
        var review = () => core.Review(card, Rating.Good, now);
        review.Should().Throw<FsrsOperationException>().WithMessage("Expected scheduledDays after review middleware");
        var preview = () => core.Preview(card, now).ToList();
        preview.Should().Throw<FsrsOperationException>().WithMessage("Expected scheduledDays after review middleware");
    }

    [Fact]
    public void Handlers_UseZeroElapsedDaysForNewCard()
    {
        var elapsed = new List<double>();
        var middleware = new LambdaMiddleware<DateTime>("new-card-elapsed")
        {
            OnReview = (ctx, next) =>
            {
                elapsed.Add(ctx.ElapsedDays);
                next();
            },
        };
        var core = DateDefinition().Use(middleware).Create(Config);
        core.Review(core.NewCard(Utc(2026, 1, 1)), Rating.Good, Utc(2026, 1, 10));
        elapsed.Should().Equal(0);
    }

    [Fact]
    public void Handlers_ApplyRollbackChronoDefaultsAfterMiddlewareUnwinds()
    {
        var seen = new List<bool>();
        var middleware = new LambdaMiddleware<DateTime>("rollback-chrono-order")
        {
            OnRollback = (ctx, next) =>
            {
                next();
                seen.Add(ctx.ResultCard.Fields.Contains(DateTimeChrono.DueAt));
            },
        };
        var core = DateDefinition().Use(middleware).Create(Config);
        var card = core.NewCard(Utc(2026, 1, 1));
        var reviewed = core.Review(card, Rating.Good, Utc(2026, 1, 2));

        var restored = core.Rollback(reviewed.Card, reviewed.Revlog);

        seen.Should().Equal(false);
        reviewed.Revlog.DueAt.Should().Be(card.DueAt);
        restored.DueAt.Should().Be(card.DueAt);
        restored.LastReviewAt.Should().BeNull();
    }

    [Fact]
    public void Handlers_SeedReviewResultWithEmptyContainers()
    {
        var seen = new List<(bool CardEmpty, bool RevlogEmpty, bool NoMemory)>();
        var middleware = new LambdaMiddleware<double>("seed-result")
        {
            OnReview = (ctx, next) =>
            {
                seen.Add((ctx.ResultCard.Fields.Count() == 0, ctx.ResultRevlog.Fields.Count() == 0,
                    ctx.ResultCard.MemoryState is null && ctx.ResultCard.State is null && ctx.ResultRevlog.Rating is null));
                next();
            },
        };
        var core = StatsScheduler(null, middleware);
        core.Review(core.NewCard(0), Rating.Good, 0);
        seen.Should().Equal((true, true, true));
    }

    [Fact]
    public void Handlers_LetReviewMiddlewareReadNow()
    {
        var seen = new List<double>();
        var middleware = new LambdaMiddleware<double>("input-now")
        {
            OnReview = (ctx, next) =>
            {
                seen.Add(ctx.Now);
                next();
            },
        };
        var core = StatsScheduler(null, middleware);
        core.Review(core.NewCard(0), Rating.Good, 7);
        seen.Should().Equal(7);
    }

    [Fact]
    public void Handlers_ValidateFinalReviewResult()
    {
        var middleware = new LambdaMiddleware<double>("invalid-result")
        {
            OnReview = (ctx, next) =>
            {
                next();
                ctx.ResultCard.MemoryState = new Sm2State(double.NaN, 1, 1);
            },
        };
        var core = StatsScheduler(null, middleware);
        var card = core.NewCard(0);
        var act = () => core.Review(card, Rating.Good, 0);
        act.Should().Throw<FsrsValidationException>().WithMessage("Expected SM2 memory state");

        var dropState = new LambdaMiddleware<double>("drop-memory")
        {
            OnReview = (ctx, next) =>
            {
                next();
                ctx.ResultCard.MemoryState = null;
            },
        };
        var core2 = StatsScheduler(null, dropState);
        var act2 = () => core2.Review(core2.NewCard(0), Rating.Good, 0);
        act2.Should().Throw<FsrsValidationException>();
    }

    private static LambdaMiddleware<double> Trace(string name, List<string> trace) => new(name)
    {
        OnReview = (ctx, next) =>
        {
            trace.Add($"{name}:review:before:{(int)ctx.Grade}");
            next();
            trace.Add($"{name}:review:after:{(int)ctx.ResultCard.State!.Value}");
        },
        OnRollback = (ctx, next) =>
        {
            trace.Add($"{name}:rollback:before:{(int)ctx.Revlog.Rating}");
            next();
            trace.Add($"{name}:rollback:after:{(int)ctx.ResultCard.State!.Value}");
        },
    };

    [Fact]
    public void Handlers_WrapReviewInArrayOrder()
    {
        var trace = new List<string>();
        var core = StatsScheduler(null, Trace("outer", trace), Trace("inner", trace));
        core.Review(core.NewCard(0), Rating.Good, 0);
        trace.Should().Equal("outer:review:before:3", "inner:review:before:3", "inner:review:after:2", "outer:review:after:2");
    }

    [Fact]
    public void Handlers_WrapPreviewReviewForEachGradeLazily()
    {
        var trace = new List<string>();
        var core = StatsScheduler(null, Trace("mw", trace));
        var previews = core.Preview(core.NewCard(0), 0);
        trace.Should().BeEmpty();

        _ = previews[Rating.Again];
        trace.Should().Equal("mw:review:before:1", "mw:review:after:2");

        _ = previews[Rating.Hard];
        _ = previews[Rating.Good];
        _ = previews[Rating.Easy];
        _ = previews[Rating.Easy]; // cached
        trace.Should().Equal(
            "mw:review:before:1", "mw:review:after:2",
            "mw:review:before:2", "mw:review:after:2",
            "mw:review:before:3", "mw:review:after:2",
            "mw:review:before:4", "mw:review:after:2");
    }

    [Fact]
    public void Handlers_WrapRollbackInArrayOrder()
    {
        var trace = new List<string>();
        var core = StatsScheduler(null, Trace("outer", trace), Trace("inner", trace));
        var first = core.Review(core.NewCard(0), Rating.Good, 0);
        var result = core.Review(first.Card, Rating.Good, first.Card.MemoryState.Interval);
        trace.Clear();

        core.Rollback(result.Card, result.Revlog);

        trace.Should().Equal("outer:rollback:before:3", "inner:rollback:before:3", "inner:rollback:after:2", "outer:rollback:after:2");
    }

    [Fact]
    public void Handlers_RejectCallingNextMoreThanOnce()
    {
        var middleware = new LambdaMiddleware<double>("bad")
        {
            OnReview = (_, next) =>
            {
                next();
                next();
            },
        };
        var core = StatsScheduler(null, middleware);
        var card = core.NewCard(0);
        var act = () => core.Review(card, Rating.Good, 0);
        act.Should().Throw<FsrsOperationException>().WithMessage("Middleware next() called multiple times");
    }

    // ---------------------------------------------------------------- preview

    [Fact]
    public void Preview_ReturnsResultsForAllGradesInOrder()
    {
        var previews = Core.Preview(Core.NewCard(), 0);
        previews.Count.Should().Be(4);
        previews.Select(p => p.Grade).Should().Equal(Rating.Again, Rating.Hard, Rating.Good, Rating.Easy);
        previews.All(p => p.Card is not null && p.Revlog is not null).Should().BeTrue();
        previews[2].Grade.Should().Be(Rating.Good);
        var manual = () => previews[Rating.Manual];
        manual.Should().Throw<FsrsValidationException>().WithMessage("Expected grade");
    }

    [Fact]
    public void Preview_MatchesIndividualReviews()
    {
        var card = Core.NewCard();
        foreach (var preview in Core.Preview(card, 0))
        {
            var individual = Core.Review(card, preview.Grade, 0);
            preview.Card.Should().Be(individual.Card);
            preview.Revlog.Should().Be(individual.Revlog);
        }
    }

    [Fact]
    public void Preview_ValidatesNowAndCard()
    {
        var card = Core.NewCard();
        var badNow = () => Core.Preview(card, double.NaN);
        badNow.Should().Throw<FsrsValidationException>().WithMessage("Expected finite number");
        var badCard = () => Core.Preview(card with { MemoryState = new Sm2State(double.NaN, 1, 1) }, 0);
        badCard.Should().Throw<FsrsValidationException>();
        Core.Preview(card).Select(p => p.Grade).Should().Equal(Grades.All);
    }

    [Fact]
    public void Preview_ForgettingCurveIsEvaluatedOnceForAllGrades()
    {
        var core = StatsScheduler();
        var card = core.NewCard(0) with { State = State.Review, MemoryState = new Sm2State(10, 2.5, 3) };
        core.Core().CurveCalls.Clear();
        var preview = core.Preview(card, 0.5);
        _ = preview.ToList();
        _ = preview.ToList();
        core.Core().CurveCalls.Should().Equal((card.MemoryState, 0.5));
        core.Core().StepCalls.Should().HaveCount(4);
        core.Core().StepCalls.Should().AllSatisfy(c =>
        {
            c.ElapsedDays.Should().Be(0.5);
            c.Retrievability.Should().Be(Math.Pow(0.9, 0.5 / 10));
        });
    }

    // ---------------------------------------------------------------- forward

    [Fact]
    public void Forward_ReturnsOneResultPerReview()
    {
        var results = Core.Forward(
        [
            new(Rating.Good, 0),
            new(Rating.Good, 1),
            new(Rating.Again, 4),
        ]);
        results.Should().HaveCount(3);
        results[0].Revlog.Rating.Should().Be(Rating.Good);
        results[0].Revlog.State.Should().Be(State.New);
        results[2].Revlog.Rating.Should().Be(Rating.Again);
        results[2].Card.Reps.Should().Be(3);
        results[2].Card.Lapses.Should().Be(1);
    }

    [Fact]
    public void Forward_MatchesEquivalentChainOfReviews()
    {
        SchedulerReview<double>[] history = [new(Rating.Good, 0), new(Rating.Hard, 2), new(Rating.Easy, 7)];
        var forwarded = Core.Forward(history);

        var card = Core.NewCard(history[0].ReviewTime);
        var expected = history.Select(review =>
        {
            var result = Core.Review(card, review.Rating, review.ReviewTime);
            card = result.Card;
            return result;
        }).ToList();

        forwarded.Should().Equal(expected);
    }

    [Fact]
    public void Forward_StartsFromGivenInitialCard()
    {
        var seeded = Core.Review(Core.NewCard(0), Rating.Good, 0);
        var results = Core.Forward([new(Rating.Good, 1)], seeded.Card);
        results.Should().HaveCount(1);
        results[0].Revlog.State.Should().Be(State.Review);
        results[0].Card.Reps.Should().Be(2);
    }

    [Fact]
    public void Forward_RejectsInvalidInitialCardOnlyWhenThereIsHistory()
    {
        var invalid = Core.NewCard(0) with { State = (State)99 };
        var act = () => Core.Forward([new(Rating.Good, 0)], invalid);
        act.Should().Throw<FsrsValidationException>();
        Core.Forward([], invalid).Should().BeEmpty();
        Core.Forward([]).Should().BeEmpty();
    }

    [Fact]
    public void Forward_RejectsManualRatings()
    {
        var act = () => Core.Forward([new(Rating.Manual, 0)]);
        act.Should().Throw<FsrsValidationException>().WithMessage("Expected grade");
    }

    // ---------------------------------------------------------------- runtime context

    [Fact]
    public void Context_ExposesActiveInstanceInReviewIntervalAndRollbackHandlers()
    {
        var reviews = new List<object>();
        var intervals = new List<object>();
        var rollbacks = new List<object>();
        var middleware = new LambdaMiddleware<double>("context-instance")
        {
            OnReview = (ctx, next) => { reviews.Add(ctx.Instance); next(); },
            OnNextInterval = (ctx, next) => { intervals.Add(ctx.Instance); next(); },
            OnRollback = (ctx, next) => { rollbacks.Add(ctx.Instance); next(); },
        };
        var definition = NumericDefinition().Use(middleware);
        var instances = new[] { definition.Create(Config), definition.Create(Config) };

        foreach (var instance in instances)
        {
            var card = instance.NewCard(0);
            var result = instance.Review(card, Rating.Good, 0);
            instance.NextInterval(result.Card.MemoryState, 0.9, new NextIntervalQuery<Sm2State>(card, Rating.Good, 0));
            instance.Rollback(result.Card, result.Revlog);
        }

        reviews.Should().HaveCount(2);
        intervals.Should().HaveCount(4);
        rollbacks.Should().HaveCount(2);
        for (var i = 0; i < instances.Length; i++)
        {
            reviews[i].Should().BeSameAs(instances[i]);
            intervals[i * 2].Should().BeSameAs(instances[i]);
            intervals[i * 2 + 1].Should().BeSameAs(instances[i]);
            rollbacks[i].Should().BeSameAs(instances[i]);
        }
    }

    [Theory]
    [InlineData(false, 0.5)]
    [InlineData(true, 0.5)]
    [InlineData(true, 100_000)]
    public void Context_ReusesInputCardRetrievability(bool preview, double elapsedDays)
    {
        var reviews = new List<double>();
        var middleware = new LambdaMiddleware<double>("context-retrievability")
        {
            OnReview = (ctx, next) =>
            {
                reviews.Add(ctx.Retrievability);
                ctx.ElapsedDays.Should().Be(elapsedDays);
                next();
                ctx.Retrievability.Should().Be(reviews[^1]);
            },
        };
        var instance = NumericScheduler(middleware);
        var card = instance.NewCard(0) with { MemoryState = new Sm2State(10, 2.5, 3), State = State.Review };
        var expected = Math.Pow(0.9, elapsedDays / 10);
        instance.Core().CurveCalls.Clear();
        instance.Core().StepCalls.Clear();

        if (!preview) instance.Review(card, Rating.Good, elapsedDays);
        else
        {
            var items = instance.Preview(card, elapsedDays);
            items.ToList().Should().HaveCount(4);
            items.ToList().Should().HaveCount(4);
        }

        reviews.Should().Equal(Enumerable.Repeat(expected, preview ? 4 : 1));
        instance.Core().CurveCalls.Should().Equal((card.MemoryState, elapsedDays));
        instance.Core().StepCalls.Should().HaveCount(preview ? 4 : 1);
        instance.Core().StepCalls.Should().AllSatisfy(c =>
        {
            c.ElapsedDays.Should().Be(elapsedDays);
            c.Retrievability.Should().Be(expected);
        });
    }
}
