using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;
using Xunit;
using static FsrsSharp.Tests.Middlewares.FuzzMonotonicTestSchedulers;
using Fuzz = FsrsSharp.Middlewares.Fuzzing;

namespace FsrsSharp.Tests.Middlewares.Fuzzing;

/// <summary>Port of <c>middlewares/fuzzing/middleware.spec.ts</c>.</summary>
public class FuzzingMiddlewareTests
{
    private sealed class SpyRng(double value)
    {
        public List<string> Seeds { get; } = [];

        public FuzzingRng Rng => seed =>
        {
            Seeds.Add(seed);
            return () => value;
        };
    }

    private sealed class AssignedRepsMiddleware : Middleware
    {
        public override string Name => "test.assigned-reps";

        public override void Review<TState, TTime>(ReviewContext<TState, TTime> context, Action next)
        {
            context.ResultCard.Set(StatsMiddleware.Reps, 7);
            next();
        }
    }

    private sealed class FixedIntervalMiddleware : Middleware
    {
        public override string Name => "test.fixed-interval";

        public override void Review<TState, TTime>(ReviewContext<TState, TTime> context, Action next)
        {
            context.ScheduledDays = 1;
            next();
        }
    }

    private static FieldBag.Builder Config(bool enableFuzz = true) =>
        Fsrs6Config().Set(FuzzingMiddleware.EnableFuzz, enableFuzz).Set(FuzzingMiddleware.MaximumInterval, 36_500d);

    private static readonly FuzzingConfigInput FuzzConfig = new(true, 36_500);

    private static Scheduler<FsrsState, DateTime> Make(FieldBag.Builder config, params Middleware[] middlewares) =>
        Fsrs6Definition(middlewares).Create(config);

    private static readonly Scheduler<FsrsState, DateTime> FuzzFirstCore =
        Make(Config(), FuzzingMiddleware.Instance, StatsMiddleware.Instance);

    private static readonly Scheduler<FsrsState, DateTime> StatsFirstCore =
        Make(Config(), StatsMiddleware.Instance, FuzzingMiddleware.Instance);

    private static readonly Scheduler<FsrsState, DateTime> FuzzFirstPreserveCore =
        Make(Config().Set(StatsMiddleware.ClearStatsOnForget, false), FuzzingMiddleware.Instance, StatsMiddleware.Instance);

    private static readonly Scheduler<FsrsState, DateTime> StatsFirstPreserveCore =
        Make(Config().Set(StatsMiddleware.ClearStatsOnForget, false), StatsMiddleware.Instance, FuzzingMiddleware.Instance);

    private static readonly Scheduler<FsrsState, DateTime> PlainCore = Make(Config(), StatsMiddleware.Instance);

    private static double ScheduledDays(DateTime dueAt, DateTime reviewedAt) => DueDays(dueAt, reviewedAt);

    // ------------------------------------------------------------------ learning steps / scheduled days

    [Fact(Skip = "needs learning-steps and scheduled-days middlewares (and a model.nextInterval spy)")]
    public void PreservesAPositiveStepAtLearningStep0() { }

    [Fact(Skip = "needs learning-steps and scheduled-days middlewares (and a model.nextInterval spy)")]
    public void UsesTheConfiguredRoundingForZeroSteps() { }

    [Fact(Skip = "needs learning-steps and scheduled-days middlewares (and a model.nextInterval spy)")]
    public void GraduatesASubDayModelIntervalToOneDay() { }

    [Fact(Skip = "needs scheduled-days middleware (and a model.nextInterval spy)")]
    public void SharesFractionalDaysConfigBetweenFuzzingAndScheduledDays() { }

    // ------------------------------------------------------------------ defaults

    [Fact]
    public void InjectsAnExplicitCardIdIntoANewCard()
    {
        FuzzFirstCore.NewCard(Now, CardInput("explicit-card")).CardId.Should().Be("explicit-card");
    }

    [Fact]
    public void GeneratesDistinctUuidCardIdsByDefault()
    {
        var first = FuzzFirstCore.NewCard(Now);
        var second = FuzzFirstCore.NewCard(Now);

        first.CardId.Should().BeOfType<string>().Which
            .Should().MatchRegex("(?i)^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");
        second.CardId.Should().NotBe(first.CardId);
    }

    [Fact]
    public void ValidatesAnExplicitlyInjectedCardId()
    {
        var act = () => FuzzFirstCore.NewCard(Now, CardInput(""));

        act.Should().Throw<FsrsValidationException>().WithMessage("Expected new card cardId");
    }

    // ------------------------------------------------------------------ review

    [Fact]
    public void UsesCardIdPlusTheIncrementedReviewRepsAndKeepsDueAtAligned()
    {
        const string cardId = "card-";
        var card = FuzzFirstCore.NewCard(Now, CardInput(cardId));
        var plainCard = PlainCore.NewCard(Now);
        var baseResult = PlainCore.Review(plainCard, Rating.Easy, Now);
        var baseInterval = ScheduledDays(baseResult.Card.DueAt, Now);
        var expectedInterval = Fuzz.WithFuzzing(baseInterval, 0, FuzzConfig, $"{cardId}1");

        var first = FuzzFirstCore.Review(card, Rating.Easy, Now);
        var second = FuzzFirstCore.Review(card, Rating.Easy, Now);

        ScheduledDays(first.Card.DueAt, Now).Should().Be(expectedInterval);
        second.Card.DueAt.Should().Be(first.Card.DueAt);
        first.Card.Reps.Should().Be(1);
        first.Card.CardId.Should().Be(cardId);
        first.Revlog.CardId.Should().Be(cardId);
    }

    [Fact]
    public void UsesMiddlewareFactoryRangeAndRngOptions()
    {
        const string cardId = "string-card";
        var rng = new SpyRng(0.5);
        var core = Make(Config(), FuzzingMiddleware.Create(new FuzzingMiddlewareOptions { FuzzingRange = [], Rng = rng.Rng }), StatsMiddleware.Instance);
        var card = core.NewCard(Now, CardInput(cardId));
        var baseResult = PlainCore.Review(PlainCore.NewCard(Now), Rating.Easy, Now);

        var result = core.Review(card, Rating.Easy, Now);

        result.Card.DueAt.Should().Be(baseResult.Card.DueAt);
        core.Config.ContainsKey("fuzzingRange").Should().BeFalse();
        core.Config.ContainsKey("rng").Should().BeFalse();
        rng.Seeds.Should().Contain($"{cardId}1");
    }

    [Fact]
    public void IsIndependentOfTheStatsMiddlewareRegistrationOrder()
    {
        const string cardId = "stable-order";
        var fuzzFirstCard = FuzzFirstCore.NewCard(Now, CardInput(cardId));
        var statsFirstCard = StatsFirstCore.NewCard(Now, CardInput(cardId));

        var fuzzFirst = FuzzFirstCore.Review(fuzzFirstCard, Rating.Easy, Now);
        var statsFirst = StatsFirstCore.Review(statsFirstCard, Rating.Easy, Now);

        statsFirst.Card.DueAt.Should().Be(fuzzFirst.Card.DueAt);
        statsFirst.Card.Reps.Should().Be(fuzzFirst.Card.Reps);
    }

    [Fact]
    public void UsesRepsAlreadyAssignedByAnEarlierMiddleware()
    {
        const string cardId = "assigned-reps";
        var rng = new SpyRng(0.5);
        var core = Make(Config(), new AssignedRepsMiddleware(), FuzzingMiddleware.Create(new FuzzingMiddlewareOptions { Rng = rng.Rng }));
        var card = core.NewCard(Now, CardInput(cardId));

        var result = core.Review(card, Rating.Easy, Now);

        result.Card.Reps.Should().Be(7);
        rng.Seeds.Should().Contain($"{cardId}7");
    }

    [Fact]
    public void RoundsAShortModelIntervalWithoutCreatingAFuzzFactor()
    {
        var card = FuzzFirstCore.NewCard(Now, CardInput("short-model-interval"));
        var expected = PlainCore.Review(PlainCore.NewCard(Now), Rating.Again, Now);

        var result = FuzzFirstCore.Review(card, Rating.Again, Now);

        result.Card.DueAt.Should().Be(expected.Card.DueAt);
    }

    [Fact]
    public void DerivesStableIntervalsWithoutCachingTheFuzzFactor()
    {
        var rng = new SpyRng(0.5);
        var core = Make(Config(), FuzzingMiddleware.Create(new FuzzingMiddlewareOptions { Rng = rng.Rng }));
        var rawIntervals = PreviewDays(PlainCore, PlainCore.NewCard(Now), Now);
        var card = FuzzFirstCore.NewCard(Now, CardInput("preview-card"));

        var first = PreviewCards(core, card, Now);
        var firstCallCount = rng.Seeds.Count;
        var second = PreviewCards(core, card, Now);

        first.Should().HaveCount(4);
        second.Select(c => c.DueAt).Should().Equal(first.Select(c => c.DueAt));
        firstCallCount.Should().Be(rawIntervals.Count(interval => interval >= 2.5));
        rng.Seeds.Should().HaveCount(firstCallCount * 2);
        rng.Seeds.Should().OnlyContain(seed => seed == "preview-card1");
    }

    [Fact]
    public void UsesTheModelIntervalInsteadOfAnEarlierScheduledDaysValue()
    {
        var core = Make(
            Config(),
            new FixedIntervalMiddleware(),
            FuzzingMiddleware.Create(new FuzzingMiddlewareOptions { FuzzingRange = [], Rng = _ => () => 0 }),
            StatsMiddleware.Instance);
        var card = core.NewCard(Now, CardInput("short-term"));

        var result = core.Review(card, Rating.Easy, Now);

        ScheduledDays(result.Card.DueAt, Now).Should().BeGreaterThan(1);
    }

    [Fact]
    public void TracksRepsWithoutRequiringTheStatsMiddleware()
    {
        var enabledCore = Make(Config(), FuzzingMiddleware.Instance);
        var enabledCard = enabledCore.NewCard(Now, CardInput("missing-reps"));
        enabledCore.Review(enabledCard, Rating.Easy, Now).Card.Reps.Should().Be(1);

        var disabledCore = Make(Config(enableFuzz: false), FuzzingMiddleware.Instance);
        var disabledCard = disabledCore.NewCard(Now, CardInput("disabled"));
        disabledCore.Review(disabledCard, Rating.Easy, Now).Card.Reps.Should().Be(1);
    }

    [Fact]
    public void ReturnsBeforeCreatingAFuzzFactorWhenFuzzingIsDisabled()
    {
        var rng = new SpyRng(0.5);
        var core = Make(Config(enableFuzz: false), FuzzingMiddleware.Create(new FuzzingMiddlewareOptions { Rng = rng.Rng }));
        var card = core.NewCard(Now, CardInput("disabled-fast-path"));

        core.Review(card, Rating.Easy, Now);

        rng.Seeds.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ lifecycle

    [Fact]
    public void PreservesCardIdThroughForgetAndRollback()
    {
        const string cardId = "lifecycle-card";
        var card = FuzzFirstCore.NewCard(Now, CardInput(cardId));
        var reviewed = FuzzFirstCore.Review(card, Rating.Easy, Now);

        var forgotten = FuzzFirstCore.Forget(reviewed.Card, Later);
        var restored = FuzzFirstCore.Rollback(reviewed.Card, reviewed.Revlog);

        forgotten.CardId.Should().Be(cardId);
        restored.CardId.Should().Be(cardId);
    }

    [Fact]
    public void ClearsStatsIndependentlyOfMiddlewareRegistrationOrder()
    {
        var fuzzFirstCard = FuzzFirstCore.NewCard(Now, CardInput("fuzz-first-clear")).With(StatsMiddleware.Reps, 3).With(StatsMiddleware.Lapses, 1);
        var statsFirstCard = StatsFirstCore.NewCard(Now, CardInput("stats-first-clear")).With(StatsMiddleware.Reps, 3).With(StatsMiddleware.Lapses, 1);

        var fuzzFirst = FuzzFirstCore.Forget(fuzzFirstCard, Later);
        var statsFirst = StatsFirstCore.Forget(statsFirstCard, Later);

        (fuzzFirst.Reps, fuzzFirst.Lapses).Should().Be((0, 0));
        (statsFirst.Reps, statsFirst.Lapses).Should().Be((0, 0));
    }

    [Fact]
    public void PreservesStatsIndependentlyOfMiddlewareRegistrationOrder()
    {
        var fuzzFirstCard = FuzzFirstPreserveCore.NewCard(Now, CardInput("fuzz-first-preserve")).With(StatsMiddleware.Reps, 3).With(StatsMiddleware.Lapses, 1);
        var statsFirstCard = StatsFirstPreserveCore.NewCard(Now, CardInput("stats-first-preserve")).With(StatsMiddleware.Reps, 3).With(StatsMiddleware.Lapses, 1);

        var fuzzFirst = FuzzFirstPreserveCore.Forget(fuzzFirstCard, Later);
        var statsFirst = StatsFirstPreserveCore.Forget(statsFirstCard, Later);

        (fuzzFirst.Reps, fuzzFirst.Lapses).Should().Be((3, 1));
        (statsFirst.Reps, statsFirst.Lapses).Should().Be((3, 1));
    }

    [Fact]
    public void RejectsReviewCardsWithoutCardId()
    {
        // Upstream: 'Expected card cardId'. The field-bag parser reports the missing required field instead.
        var card = FuzzFirstCore.NewCard(Now);
        var withoutCardId = card with { Fields = card.Fields.Without(FuzzingMiddleware.CardIdField) };

        var act = () => FuzzFirstCore.Review(withoutCardId, Rating.Good, Now);

        act.Should().Throw<FsrsValidationException>().WithMessage("*cardId*");
    }
}
