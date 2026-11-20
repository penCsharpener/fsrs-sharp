using Xunit;
using AwesomeAssertions;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;

namespace FsrsSharp.Tests.Middlewares.MaximumInterval;

/// <summary>Port of ts-fsrs <c>middlewares/maximum-interval/middleware.spec.ts</c>.</summary>
public class MaximumIntervalMiddlewareTests
{
    private const int MaximumInterval = 2;
    private static DateTime Now => MiddlewareTestSchedulers.Now;

    private static readonly ProbeMiddleware<FsrsState, DateTime> FixedInterval = new("test.maximum-interval.fixed")
    {
        OnReview = (ctx, next) =>
        {
            ctx.ScheduledDays = 10;
            next();
        },
    };

    [Fact]
    public void LeavesAnAbsentScheduledDaysValueUnchanged()
    {
        var config = MiddlewareTestSchedulers.Fsrs6Config();
        config.Add(MaximumIntervalMiddleware.MaximumInterval, MaximumInterval);
        var scheduler = MiddlewareTestSchedulers.Fsrs6(MaximumIntervalMiddleware.Instance).Create(config);
        var card = scheduler.NewCard(Now);
        var candidate = new ReviewCandidate<FsrsState>(new DesiredRetentionByGrade(0.9), _ => card.MemoryState, _ => null, (_, _) => 0);
        var ctx = new NextIntervalContext<FsrsState, DateTime>(scheduler, card, Rating.Good, null, 0, candidate);
        var calls = 0;

        MaximumIntervalMiddleware.Instance.NextInterval(ctx, () => calls++);

        calls.Should().Be(1);
        ctx.ScheduledDays.Should().BeNull();
    }

    [Fact]
    public void CapsReviewAndPreviewWithoutFuzzingOrMonotonicMiddleware()
    {
        var config = MiddlewareTestSchedulers.Fsrs6Config();
        config.Add(MaximumIntervalMiddleware.MaximumInterval, MaximumInterval);
        var core = MiddlewareTestSchedulers.Fsrs6(FixedInterval, ScheduledDaysMiddleware.Instance, MaximumIntervalMiddleware.Instance)
            .Create(config);
        var card = core.NewCard(Now);

        core.Review(card, Rating.Easy, Now).Card.ScheduledDays.Should().Be(MaximumInterval);
        core.Preview(card, Now).Select(item => item.Card.ScheduledDays).Should().Equal(2, 2, 2, 2);
    }

    [Fact(Skip = "needs fuzzing/monotonic middleware")]
    public void CapsIntervalsWhenFuzzingIsRegisteredButDisabled()
    {
        // Upstream: [fixedInterval, createSchedulerFuzzingMiddleware({ rng }), scheduledDays, maximumInterval]
        // with enableFuzz: false; Easy review is capped at 2 and the rng is never called.
    }

    [Fact]
    public void DefaultsMaximumIntervalTo36500AndRejectsNonPositiveValues()
    {
        MaximumIntervalMiddleware.DefaultMaximumInterval.Should().Be(36_500);
        var scheduler = MiddlewareTestSchedulers.Fsrs6(MaximumIntervalMiddleware.Instance).Create(MiddlewareTestSchedulers.Fsrs6Config());
        scheduler.Config.Get(MaximumIntervalMiddleware.MaximumInterval).Should().Be(36_500);

        MaximumIntervalMiddleware.MaximumInterval.Validate(0).Should().Be("Expected positive integer maximumInterval");
        MaximumIntervalMiddleware.MaximumInterval.Validate(-1).Should().Be("Expected positive integer maximumInterval");
        MaximumIntervalMiddleware.MaximumInterval.Validate(1).Should().BeNull();
    }
}
