using AwesomeAssertions;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Tests.Kit;
using Xunit;

namespace FsrsSharp.Tests.Middlewares.Stats;

/// <summary>Ports of srs-kit <c>middleware/stats/middleware.spec.ts</c> and <c>schema.spec.ts</c>.</summary>
public class StatsMiddlewareTests
{
    private static readonly Scheduler<Sm2State, double> Instance = KitFixtures.NumericScheduler(StatsMiddleware.Instance);
    private static readonly StatsMiddleware Stats = StatsMiddleware.Instance;

    private static Card<Sm2State> StatsCard(int reps, int lapses, State state) =>
        new(new Sm2State(0, 2.5, 0), state, ScheduleStatus.Review,
            new FieldBag.Builder().Set(StatsMiddleware.Reps, reps).Set(StatsMiddleware.Lapses, lapses).ToFieldBag());

    private static ReviewContext<Sm2State, double> ReviewContext(Rating grade = Rating.Good, State state = State.Learning, int reps = 1, int lapses = 0) =>
        new(Instance, StatsCard(reps, lapses, state), grade, 0, 1, 0.9,
            new ReviewCandidate<Sm2State>(new DesiredRetentionByGrade(0.9), _ => new Sm2State(0, 0, 0), _ => null, (_, _) => 1));

    private static RollbackContext<Sm2State, double> RollbackContext(Rating rating = Rating.Good, State state = State.Learning, int reps = 1, int lapses = 0) =>
        new(Instance, StatsCard(reps, lapses, state),
            new ReviewLog<Sm2State>(new Sm2State(0, 2.5, 0), rating, state, ScheduleStatus.Review, FieldBag.Empty));

    private static CardDefaultContext<Sm2State> Defaults(CardDefaultOperation op, bool clear, Card<Sm2State>? existing = null) =>
        new(op, new FieldBag.Builder().Set(StatsMiddleware.ClearStatsOnForget, clear).ToFieldBag(), FieldBag.Empty, existing);

    // ---------------------------------------------------------------- middleware

    [Fact]
    public void ProvidesDefaultStatsFields()
    {
        var card = new FieldBag.Builder();
        Stats.WriteCardDefaults(Defaults(CardDefaultOperation.NewCard, true), card);
        card.ToFieldBag().ShouldBeBag(new FieldBag.Builder().Set(StatsMiddleware.Reps, 0).Set(StatsMiddleware.Lapses, 0).ToFieldBag());
    }

    [Fact]
    public void ClearsOldStatsByDefaultOnForget()
    {
        var card = new FieldBag.Builder();
        Stats.WriteCardDefaults(Defaults(CardDefaultOperation.Forget, true, StatsCard(3, 1, State.Review)), card);
        card.ToFieldBag().ShouldBeBag(new FieldBag.Builder().Set(StatsMiddleware.Reps, 0).Set(StatsMiddleware.Lapses, 0).ToFieldBag());
    }

    [Fact]
    public void CanPreserveOldStatsOnForget()
    {
        var card = new FieldBag.Builder();
        Stats.WriteCardDefaults(Defaults(CardDefaultOperation.Forget, false, StatsCard(3, 1, State.Review)), card);
        card.ToFieldBag().ShouldBeBag(new FieldBag.Builder().Set(StatsMiddleware.Reps, 3).Set(StatsMiddleware.Lapses, 1).ToFieldBag());
    }

    [Fact]
    public void IncrementsRepsWithoutALapseForNonReviewAgainRatings()
    {
        var ctx = ReviewContext(reps: 2, lapses: 1);
        Stats.Review(ctx, () => ctx.ResultCard.State = State.Review);
        ctx.ResultCard.Get(StatsMiddleware.Reps).Should().Be(3);
        ctx.ResultCard.Get(StatsMiddleware.Lapses).Should().Be(1);
    }

    [Fact]
    public void IncrementsLapsesWhenReviewCardsAreRatedAgain()
    {
        var ctx = ReviewContext(Rating.Again, State.Review, 2, 1);
        Stats.Review(ctx, () => { });
        ctx.ResultCard.Get(StatsMiddleware.Reps).Should().Be(3);
        ctx.ResultCard.Get(StatsMiddleware.Lapses).Should().Be(2);
    }

    [Fact]
    public void ReviewWritesStatsAfterNextReturns()
    {
        var ctx = ReviewContext();
        Stats.Review(ctx, () => ctx.ResultCard.Fields.Contains(StatsMiddleware.Reps).Should().BeFalse());
        ctx.ResultCard.Fields.Contains(StatsMiddleware.Reps).Should().BeTrue();
    }

    [Fact]
    public void RollsBackRepsAndNonLapseReviewsWithoutGoingBelowZero()
    {
        var ctx = RollbackContext(reps: 0, lapses: 0);
        Stats.Rollback(ctx, () => { });
        ctx.ResultCard.Get(StatsMiddleware.Reps).Should().Be(0);
        ctx.ResultCard.Get(StatsMiddleware.Lapses).Should().Be(0);
    }

    [Fact]
    public void RollsBackLapseCountsForReviewAgainRevlogs()
    {
        var ctx = RollbackContext(Rating.Again, State.Review, 3, 2);
        Stats.Rollback(ctx, () => { });
        ctx.ResultCard.Get(StatsMiddleware.Reps).Should().Be(2);
        ctx.ResultCard.Get(StatsMiddleware.Lapses).Should().Be(1);
    }

    [Fact]
    public void DeclaresKeysAndName()
    {
        Stats.Name.Should().Be("srs-kit.stats");
        Stats.ConfigKeys.Should().Equal(StatsMiddleware.ClearStatsOnForget);
        Stats.CardFields.Should().Equal(StatsMiddleware.Reps, StatsMiddleware.Lapses);
        Stats.RevlogFields.Should().BeEmpty();
    }

    [Fact]
    public void ExtensionPropertiesReadTheFields()
    {
        var card = StatsCard(4, 2, State.Review);
        card.Reps.Should().Be(4);
        card.Lapses.Should().Be(2);
    }

    // ---------------------------------------------------------------- schema (schema.spec.ts)

    [Fact]
    public void ConfigKey_DefaultsClearStatsOnForgetToTrue()
    {
        StatsMiddleware.ClearStatsOnForget.Name.Should().Be("clearStatsOnForget");
        StatsMiddleware.ClearStatsOnForget.HasDefault.Should().BeTrue();
        StatsMiddleware.ClearStatsOnForget.Default.Should().BeTrue();
        Instance.Config.Get(StatsMiddleware.ClearStatsOnForget).Should().BeTrue();
    }

    [Fact]
    public void ConfigKey_AcceptsFalseAndRejectsNonBoolean()
    {
        var config = KitFixtures.ConfigBuilder().Set(StatsMiddleware.ClearStatsOnForget, false);
        KitFixtures.NumericDefinition().Use(Stats).Create(config).Config.Get(StatsMiddleware.ClearStatsOnForget).Should().BeFalse();

        var bad = KitFixtures.ConfigBuilder();
        bad.SetRaw("clearStatsOnForget", "yes");
        var act = () => KitFixtures.NumericDefinition().Use(Stats).Create(bad);
        act.Should().Throw<FsrsValidationException>();
    }

    [Fact]
    public void Fields_AcceptNonNegativeIntegers()
    {
        StatsMiddleware.Reps.Validate(0).Should().BeNull();
        StatsMiddleware.Reps.Validate(3).Should().BeNull();
        StatsMiddleware.Lapses.Validate(0).Should().BeNull();
    }

    [Fact]
    public void Fields_RejectNegativeAndNonNumericValues()
    {
        StatsMiddleware.Reps.Validate(-1).Should().Be("Expected non-negative integer reps");
        StatsMiddleware.Lapses.Validate(-1).Should().Be("Expected non-negative integer lapses");
        StatsMiddleware.Reps.Validate("1").Should().NotBeNull();
        StatsMiddleware.Reps.Validate(1.5).Should().NotBeNull(); // not an int (upstream: not a safe integer)
        StatsMiddleware.Lapses.Validate(null).Should().NotBeNull();
    }

    [Fact]
    public void SchedulerRejectsCardsWithInvalidOrMissingStats()
    {
        var card = Instance.NewCard(0);
        var negative = () => Instance.Review(card.With(StatsMiddleware.Reps, -1), Rating.Good, 0);
        negative.Should().Throw<FsrsValidationException>().WithMessage("Expected non-negative integer reps");
        var negativeLapses = () => Instance.Review(card.With(StatsMiddleware.Lapses, -1), Rating.Good, 0);
        negativeLapses.Should().Throw<FsrsValidationException>().WithMessage("Expected non-negative integer lapses");
        var missing = () => Instance.Review(card with { Fields = FieldBag.Empty }, Rating.Good, 0);
        missing.Should().Throw<FsrsValidationException>();
    }
}
