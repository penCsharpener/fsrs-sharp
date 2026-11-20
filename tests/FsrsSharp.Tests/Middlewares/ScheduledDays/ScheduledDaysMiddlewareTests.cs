using Xunit;
using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;

namespace FsrsSharp.Tests.Middlewares.ScheduledDays;

/// <summary>Port of ts-fsrs <c>middlewares/scheduled-days/middleware.spec.ts</c> and <c>schema.spec.ts</c>.</summary>
public class ScheduledDaysMiddlewareTests
{
    private static DateTime Now => MiddlewareTestSchedulers.Now;

    private static Scheduler<FsrsState, DateTime> CreateCore(double scheduledDays = 2.9)
    {
        var fixedInterval = new ProbeMiddleware<FsrsState, DateTime>("test.scheduled-days.interval")
        {
            OnReview = (ctx, next) =>
            {
                ctx.ScheduledDays = scheduledDays;
                next();
            },
        };
        return MiddlewareTestSchedulers.Fsrs6(fixedInterval, ScheduledDaysMiddleware.Instance)
            .Create(MiddlewareTestSchedulers.Fsrs6Config());
    }

    /// <summary>Runs the handler directly against a hand-built context (upstream calls <c>handlers.review(ctx, () => {})</c>).</summary>
    private static ReviewContext<FsrsState, DateTime> RunHandler(bool fractionalDays, double inputScheduledDays, double? scheduledDays)
    {
        var config = MiddlewareTestSchedulers.Fsrs6Config();
        config.Add(KitSchemas.FractionalDays, fractionalDays);
        var scheduler = MiddlewareTestSchedulers.Fsrs6(ScheduledDaysMiddleware.Instance).Create(config);
        var card = scheduler.NewCard(Now).With(ScheduledDaysMiddleware.ScheduledDays, inputScheduledDays);
        var candidate = new ReviewCandidate<FsrsState>(new DesiredRetentionByGrade(0.9), _ => card.MemoryState, _ => null, (_, _) => 0);
        var ctx = new ReviewContext<FsrsState, DateTime>(scheduler, card, Rating.Good, Now, 0, 0, candidate)
        {
            ScheduledDays = scheduledDays,
        };
        ScheduledDaysMiddleware.Instance.Review(ctx, () => { });
        return ctx;
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0 / 86400)]
    [InlineData(0.5)]
    [InlineData(2.9)]
    [InlineData(-1.0)]
    public void PreservesFractionalIntervalsWithParsedConfig(double days)
    {
        var ctx = RunHandler(true, 0.25, days);
        ctx.ResultCard.Get(ScheduledDaysMiddleware.ScheduledDays).Should().Be(Math.Max(0, days));
        ctx.ResultRevlog.Get(ScheduledDaysMiddleware.ScheduledDays).Should().Be(0.25);
    }

    [Fact]
    public void DefaultsANewCardScheduledDaysToZero() =>
        CreateCore().NewCard(Now).ScheduledDays.Should().Be(0);

    [Fact]
    public void StoresTheFlooredScheduledIntervalAndLogsTheInputInterval()
    {
        var core = CreateCore();
        var card = core.NewCard(Now).With(ScheduledDaysMiddleware.ScheduledDays, 7.5);

        var result = core.Review(card, Rating.Easy, Now);

        result.Card.ScheduledDays.Should().Be(2);
        result.Revlog.ScheduledDays.Should().Be(7.5);
    }

    [Fact]
    public void DefaultsAMissingScheduledIntervalToZero()
    {
        var ctx = RunHandler(false, 7, null);
        ctx.ResultCard.Get(ScheduledDaysMiddleware.ScheduledDays).Should().Be(0);
        ctx.ResultRevlog.Get(ScheduledDaysMiddleware.ScheduledDays).Should().Be(7);
    }

    [Fact]
    public void StoresAnIntervalSelectedByALaterMiddlewareDuringUnwind()
    {
        var interval = new ProbeMiddleware<FsrsState, DateTime>("test.scheduled-days.post-interval")
        {
            OnReview = (ctx, next) =>
            {
                next();
                ctx.ScheduledDays = 4.9;
            },
        };
        var core = MiddlewareTestSchedulers.Fsrs6(ScheduledDaysMiddleware.Instance, interval)
            .Create(MiddlewareTestSchedulers.Fsrs6Config());

        var result = core.Review(core.NewCard(Now), Rating.Good, Now);

        result.Card.ScheduledDays.Should().Be(4);
        (result.Card.DueAt - Now).TotalMilliseconds.Should().Be(Math.Truncate(4.9 * 24 * 60 * 60 * 1000));
    }

    [Fact]
    public void ClampsANegativeScheduledIntervalToZero()
    {
        var core = CreateCore(-1.2);
        core.Review(core.NewCard(Now), Rating.Again, Now).Card.ScheduledDays.Should().Be(0);
    }

    [Fact]
    public void StoresASecondPrecisionLearningIntervalBeforeTheDayThreshold()
    {
        var config = MiddlewareTestSchedulers.Fsrs6Config(enableShortTerm: true);
        config.SetRaw("learningSteps", new[] { "1439.6m" });
        config.SetRaw("relearningSteps", new string[0]);
        var core = MiddlewareTestSchedulers.Fsrs6(ScheduledDaysMiddleware.Instance, LearningStepsMiddleware.Instance).Create(config);

        var result = core.Review(core.NewCard(Now), Rating.Again, Now);

        result.Card.State.Should().Be(State.Learning);
        result.Card.ScheduleStatus.Should().Be((ScheduleStatus)"learning");
        result.Card.ScheduledDays.Should().Be(0);
        (result.Card.DueAt - Now).TotalMilliseconds.Should().Be(1439.6 * 60_000);
    }

    [Fact]
    public void RestoresScheduledDaysDuringRollback()
    {
        var core = CreateCore();
        var card = core.NewCard(Now).With(ScheduledDaysMiddleware.ScheduledDays, 1.5);
        var result = core.Review(card, Rating.Easy, Now);

        result.Revlog.ScheduledDays.Should().Be(1.5);
        core.Rollback(result.Card, result.Revlog).ScheduledDays.Should().Be(1.5);
    }

    // scheduledDaysFieldsSchema

    [Theory]
    [InlineData(0.0)]
    [InlineData(12.0)]
    [InlineData(1.5)]
    public void FieldAcceptsNonNegativeFiniteScheduledDays(double scheduledDays) =>
        ScheduledDaysMiddleware.ScheduledDays.Validate(scheduledDays).Should().BeNull();

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1.0)]
    public void FieldRejectsInvalidScheduledDays(double scheduledDays) =>
        ScheduledDaysMiddleware.ScheduledDays.Validate(scheduledDays).Should().Be("scheduledDays must be finite and non-negative");

    [Fact]
    public void FieldRejectsNonNumbers()
    {
        // Upstream also rejects a missing field and '1'; the typed key and the missing-field check cover those.
        ScheduledDaysMiddleware.ScheduledDays.Validate("1").Should().NotBeNull();
        ScheduledDaysMiddleware.ScheduledDays.Validate(null).Should().NotBeNull();
    }
}
