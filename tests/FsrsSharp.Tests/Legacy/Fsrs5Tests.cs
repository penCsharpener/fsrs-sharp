using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs5;
using Xunit;

namespace FsrsSharp.Tests.Legacy;

/// <summary>Port of <c>legacy/__tests__/FSRS-5.test.ts</c>. Since v6.0.0 upstream drives this suite through the new
/// scheduler API composed with the FSRS-5 model and the standard middlewares (the "get retrievability" and
/// "fsrs.next" groups of that file exercise the legacy facade and live in <see cref="RetrievabilityTests"/> and
/// <see cref="Fsrs6Tests"/>).</summary>
public class Fsrs5Tests
{
    private static readonly DateTime Epoch = TestParameters.Epoch;

    // TODO(config-key cleanup): upstream also composes MaximumIntervalMiddleware here, but its int-typed
    // maximumInterval key currently collides with the double-typed one Fuzzing/MonotonicInterval declare. The cap
    // (36500) never binds in these scenarios, so leaving it out changes no asserted value.
    private static Scheduler<FsrsState, DateTime> CreateScheduler(bool enableShortTerm = true) =>
        SchedulerDefinition.Define(Fsrs5Model.Instance, DateTimeChrono.Instance)
            .Use(
                DesiredRetentionMiddleware.Instance,
                FuzzingMiddleware.Instance,
                StatsMiddleware.Instance,
                ScheduledDaysMiddleware.Instance,
                LearningStepsMiddleware.Instance,
                MonotonicIntervalMiddleware.Instance)
            .Create(new FieldBag.Builder
            {
                { Fsrs5Model.Weights, TestParameters.W5 },
                { Fsrs5Model.EnableShortTerm, enableShortTerm },
                { DesiredRetentionMiddleware.DesiredRetention, 0.9 },
                { FuzzingMiddleware.EnableFuzz, false },
                { FuzzingMiddleware.MaximumInterval, 36500.0 },
                { LearningStepsMiddleware.LearningSteps, ["1m", "10m"] },
                { LearningStepsMiddleware.RelearningSteps, ["10m"] },
            });

    [Fact]
    public void IvlHistory_ShouldMatchTheUpstreamGoldenVector_OverThirteenReviews()
    {
        var scheduler = CreateScheduler();
        var now = Epoch;
        var card = scheduler.NewCard(now);
        Rating[] ratings =
        [
            Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good,
            Rating.Again, Rating.Again,
            Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good,
        ];
        var ivlHistory = new List<double>();

        foreach (var rating in ratings)
        {
            var selectedCard = card;
            foreach (var schedulingCard in scheduler.Preview(card, now))
            {
                var check = schedulingCard.Grade;
                var rollbackCard = scheduler.Rollback(schedulingCard.Card, schedulingCard.Revlog);
                rollbackCard.Should().BeEquivalentTo(card);
                var next = scheduler.Review(card, check, now);
                new ScheduleResult<FsrsState>(schedulingCard.Card, schedulingCard.Revlog).Should().BeEquivalentTo(next);
                if (check == rating) selectedCard = schedulingCard.Card;
            }

            card = selectedCard;
            ivlHistory.Add(card.ScheduledDays);
            now = card.DueAt;
        }

        ivlHistory.Should().Equal(0, 4, 14, 44, 125, 328, 0, 0, 7, 16, 34, 71, 142);
    }

    [Theory]
    [InlineData(true, 48.26549438, 7.10441712)]
    [InlineData(false, 48.065163, 7.10441712)]
    public void MemoryState_ShouldMatchUpstream(bool enableShortTerm, double expectedStability, double expectedDifficulty)
    {
        Rating[] ratings = [Rating.Again, Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good];
        int[] intervals = [0, 0, 1, 3, 8, 21];
        var scheduler = CreateScheduler(enableShortTerm);
        var now = Epoch;
        var card = scheduler.NewCard(now);

        for (var index = 0; index < ratings.Length; index++)
        {
            now = now.AddDays(intervals[index]);
            card = scheduler.Review(card, ratings[index], now).Card;
        }

        card.MemoryState.Stability.Should().BeApproximately(expectedStability, 1e-4);
        card.MemoryState.Difficulty.Should().BeApproximately(expectedDifficulty, 1e-4);
    }

    [Fact]
    public void FirstRepeat_ShouldMatchUpstream_ForEveryGrade()
    {
        var scheduler = CreateScheduler();
        var card = scheduler.NewCard(Epoch);
        var cards = scheduler.Preview(card, Epoch).Select(item => item.Card).ToList();

        cards.Select(c => c.MemoryState.Stability).Should().Equal(0.40255, 1.18385, 3.173, 15.69105);
        cards.Select(c => c.MemoryState.Difficulty).Should().Equal(7.1949, 6.48830527, 5.28243442, 3.22450159);
        cards.Select(c => c.Reps).Should().Equal(1, 1, 1, 1);
        cards.Select(c => c.Lapses).Should().Equal(0, 0, 0, 0);
        cards.Select(c => c.ScheduledDays).Should().Equal(0, 0, 0, 16);
        cards.Select(c => c.State).Should().Equal(State.Learning, State.Learning, State.Learning, State.Review);
    }
}
