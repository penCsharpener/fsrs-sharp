using Xunit;
using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;

namespace FsrsSharp.Tests.Middlewares.Leech;

/// <summary>Port of ts-fsrs <c>middlewares/leech/middleware.spec.ts</c> and <c>schema.spec.ts</c>.</summary>
public class LeechMiddlewareTests
{
    private static DateTime Now => MiddlewareTestSchedulers.Now;
    private static DateTime Later => MiddlewareTestSchedulers.Later;
    private static readonly ScheduleStatus Suspended = "suspended";

    private static FieldBag.Builder ModelConfig() => MiddlewareTestSchedulers.Fsrs6Config();

    private static Scheduler<FsrsState, DateTime> CreateLeechCore(int leechThreshold = 8)
    {
        var config = ModelConfig();
        config.Add(LeechMiddleware.LeechThreshold, leechThreshold);
        return MiddlewareTestSchedulers.Fsrs6(LeechMiddleware.Instance).Create(config);
    }

    private static Card<FsrsState> CreateReviewCard(Scheduler<FsrsState, DateTime> core) =>
        core.Review(core.NewCard(Now), Rating.Good, Now).Card;

    private static Card<FsrsState> WithLapses(Card<FsrsState> card, int lapses) => card.With(StatsMiddleware.Lapses, lapses);

    [Fact]
    public void DefaultsNewCardsToZeroLapsesWithoutStatsMiddleware()
    {
        var core = MiddlewareTestSchedulers.Fsrs6(LeechMiddleware.Instance).Create(ModelConfig());

        core.NewCard(Now).Lapses.Should().Be(0);
        core.Config.Get(LeechMiddleware.LeechThreshold).Should().Be(0);
        LeechMiddleware.Instance.ScheduleStatuses.Should().Contain(Suspended);
        // The scheduler accepts the declared 'suspended' status on cards.
        core.Forget(core.NewCard(Now) with { ScheduleStatus = Suspended }, Now).Should().NotBeNull();
    }

    [Theory]
    [InlineData(7, 8)]
    [InlineData(15, 16)]
    public void SuspendsAReviewCardAtALapseMultiple(int before, int after)
    {
        var core = CreateLeechCore();
        var card = WithLapses(CreateReviewCard(core), before);

        var result = core.Review(card, Rating.Again, Later);

        result.Card.Lapses.Should().Be(after);
        result.Card.ScheduleStatus.Should().Be(Suspended);
    }

    [Fact]
    public void DoesNotSuspendBeforeTheNextThresholdMultiple()
    {
        var core = CreateLeechCore();
        var card = WithLapses(CreateReviewCard(core), 8);

        var result = core.Review(card, Rating.Again, Later);

        result.Card.Lapses.Should().Be(9);
        result.Card.ScheduleStatus.Should().Be(ScheduleStatus.Review);
    }

    [Fact]
    public void DisablesSuspensionAtThresholdZeroWhileStillTrackingLapses()
    {
        var core = CreateLeechCore(0);
        var card = WithLapses(CreateReviewCard(core), 7);

        var result = core.Review(card, Rating.Again, Later);

        result.Card.Lapses.Should().Be(8);
        result.Card.ScheduleStatus.Should().Be(ScheduleStatus.Review);
    }

    [Fact]
    public void DoesNotSuspendWhenTheCurrentReviewIsNotALapse()
    {
        var core = CreateLeechCore();
        var card = WithLapses(CreateReviewCard(core), 8);

        var result = core.Review(card, Rating.Good, Later);

        result.Card.Lapses.Should().Be(8);
        result.Card.ScheduleStatus.Should().Be(ScheduleStatus.Review);
    }

    [Fact]
    public void DoesNotCountAgainOutsideTheReviewStateAsALapse()
    {
        var core = CreateLeechCore(1);

        var result = core.Review(core.NewCard(Now), Rating.Again, Now);

        result.Card.Lapses.Should().Be(0);
        result.Card.ScheduleStatus.Should().Be(ScheduleStatus.Review);
    }

    [Fact]
    public void OnlySuspendsTheAgainPreviewAtTheNextThreshold()
    {
        var core = CreateLeechCore();
        var card = WithLapses(CreateReviewCard(core), 7);
        var preview = core.Preview(card, Later).ToList();

        preview.Select(item => item.Card.Lapses).Should().Equal(8, 7, 7, 7);
        preview.Select(item => item.Card.ScheduleStatus).Should().Equal(Suspended, ScheduleStatus.Review, ScheduleStatus.Review, ScheduleStatus.Review);
    }

    [Fact]
    public void IsIndependentOfStatsMiddlewareRegistrationOrder()
    {
        var config = ModelConfig();
        config.Add(LeechMiddleware.LeechThreshold, 8);
        var leechFirst = MiddlewareTestSchedulers.Fsrs6(LeechMiddleware.Instance, StatsMiddleware.Instance).Create(config);
        var statsFirst = MiddlewareTestSchedulers.Fsrs6(StatsMiddleware.Instance, LeechMiddleware.Instance).Create(config);
        var leechFirstCard = WithLapses(leechFirst.Review(leechFirst.NewCard(Now), Rating.Good, Now).Card, 7);
        var statsFirstCard = WithLapses(statsFirst.Review(statsFirst.NewCard(Now), Rating.Good, Now).Card, 7);

        var leechFirstResult = leechFirst.Review(leechFirstCard, Rating.Again, Later);
        var statsFirstResult = statsFirst.Review(statsFirstCard, Rating.Again, Later);

        leechFirstResult.Card.Lapses.Should().Be(8);
        leechFirstResult.Card.ScheduleStatus.Should().Be(Suspended);
        statsFirstResult.Card.Lapses.Should().Be(8);
        statsFirstResult.Card.ScheduleStatus.Should().Be(Suspended);
    }

    [Fact]
    public void UsesLapsesAlreadyAssignedByAnEarlierMiddleware()
    {
        var assigned = new ProbeMiddleware<FsrsState, DateTime>("test.assigned-lapses")
        {
            Card = [StatsMiddleware.Lapses],
            OnReview = (ctx, next) =>
            {
                ctx.ResultCard.Set(StatsMiddleware.Lapses, 16);
                next();
            },
        };
        var config = ModelConfig();
        config.Add(LeechMiddleware.LeechThreshold, 8);
        var core = MiddlewareTestSchedulers.Fsrs6(assigned, LeechMiddleware.Instance).Create(config);
        var card = WithLapses(core.Review(core.NewCard(Now), Rating.Good, Now).Card, 7);

        var result = core.Review(card, Rating.Again, Later);

        result.Card.Lapses.Should().Be(16);
        result.Card.ScheduleStatus.Should().Be(Suspended);
    }

    [Fact]
    public void OverridesLearningStepsWhenRegisteredBeforeThem()
    {
        var config = MiddlewareTestSchedulers.Fsrs6Config(enableShortTerm: true, numRelearningSteps: 1);
        config.SetRaw("learningSteps", new[] { "1m", "10m" });
        config.SetRaw("relearningSteps", new[] { "10m" });
        config.Add(LeechMiddleware.LeechThreshold, 1);
        var core = MiddlewareTestSchedulers
            .Fsrs6(LeechMiddleware.Instance, LearningStepsMiddleware.Instance, StatsMiddleware.Instance)
            .Create(config);
        var reviewed = core.Review(core.NewCard(Now), Rating.Easy, Now).Card;

        var result = core.Review(reviewed, Rating.Again, Later);

        result.Card.Lapses.Should().Be(1);
        result.Card.ScheduleStatus.Should().Be(Suspended);
    }

    // lifecycle

    [Fact]
    public void RestoresLapsesAndScheduleStatusDuringRollbackWithoutStats()
    {
        var core = CreateLeechCore();
        var card = WithLapses(CreateReviewCard(core), 7);
        var reviewed = core.Review(card, Rating.Again, Later);

        var restored = core.Rollback(reviewed.Card, reviewed.Revlog);

        restored.Lapses.Should().Be(7);
        restored.ScheduleStatus.Should().Be(ScheduleStatus.Review);
    }

    [Fact]
    public void KeepsLapsesWhenRollingBackANonLapseReview()
    {
        var core = CreateLeechCore();
        var card = WithLapses(CreateReviewCard(core), 7);
        var reviewed = core.Review(card, Rating.Good, Later);

        core.Rollback(reviewed.Card, reviewed.Revlog).Lapses.Should().Be(7);
    }

    [Fact]
    public void ClearsLapsesOnForgetByDefault()
    {
        var core = CreateLeechCore();
        var card = WithLapses(CreateReviewCard(core), 7);

        core.Forget(card, Later).Lapses.Should().Be(0);
    }

    [Fact]
    public void PreservesLapsesWithStatsRegardlessOfRegistrationOrder()
    {
        var config = ModelConfig();
        config.Add(LeechMiddleware.LeechThreshold, 8);
        config.Add(StatsMiddleware.ClearStatsOnForget, false);
        var leechFirst = MiddlewareTestSchedulers.Fsrs6(LeechMiddleware.Instance, StatsMiddleware.Instance).Create(config);
        var statsFirst = MiddlewareTestSchedulers.Fsrs6(StatsMiddleware.Instance, LeechMiddleware.Instance).Create(config);

        leechFirst.Forget(WithLapses(leechFirst.NewCard(Now), 7), Later).Lapses.Should().Be(7);
        statsFirst.Forget(WithLapses(statsFirst.NewCard(Now), 7), Later).Lapses.Should().Be(7);
    }

    // leechConfigSchema / leechCardFieldsSchema

    [Fact]
    public void DefaultsLeechThresholdToDisabled() =>
        LeechMiddleware.DefaultLeechThreshold.Should().Be(0);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    public void AcceptsLeechThreshold(int leechThreshold) =>
        CreateLeechCore(leechThreshold).Config.Get(LeechMiddleware.LeechThreshold).Should().Be(leechThreshold);

    public static TheoryData<object?> BadThresholds => [-1, 1.5, double.NaN, double.PositiveInfinity, "8"];

    [Theory]
    [MemberData(nameof(BadThresholds))]
    public void RejectsInvalidLeechConfig(object? leechThreshold)
    {
        var config = ModelConfig();
        config.SetRaw("leechThreshold", leechThreshold);
        FluentActions.Invoking(() => MiddlewareTestSchedulers.Fsrs6(LeechMiddleware.Instance).Create(config))
            .Should().Throw<FsrsValidationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void ParsesNonNegativeIntegerLapses(int lapses) =>
        LeechMiddleware.Lapses.Validate(lapses).Should().BeNull();

    public static TheoryData<object?> BadLapses => [default(object), -1, 1.5, double.NaN, double.PositiveInfinity, "1"];

    [Theory]
    [MemberData(nameof(BadLapses))]
    public void RejectsInvalidLapses(object? lapses)
    {
        // Upstream also rejects null/{} objects; here the field is validated directly.
        LeechMiddleware.Lapses.Validate(lapses).Should().NotBeNull();
    }
}
