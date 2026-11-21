using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs6;
using Xunit;

namespace FsrsSharp.Tests.Legacy.Fixed;

/// <summary>Port of <c>legacy/__tests__/fixed/calc-elapsed-days.test.ts</c>. Both cases replay a real review log —
/// actual timestamps from a real collection, not synthetic offsets — which is what makes them worth having: they
/// catch elapsed-day arithmetic that looks right on tidy whole-day intervals and drifts on ragged ones. Since
/// v6.0.0 upstream drives them through a bare FSRS-6 scheduler (model + date chrono, no middlewares).</summary>
public class CalcElapsedDaysTests
{
    private static Scheduler<FsrsState, DateTime> CreateScheduler(double[] weights) =>
        SchedulerDefinition.Define(Fsrs6Model.Instance, DateTimeChrono.Instance)
            .Create(new FieldBag.Builder
            {
                { Fsrs6Model.Weights, Fsrs6Parameters.Migrate(weights).ToArray() },
                { Fsrs6Model.EnableShortTerm, true },
                { Fsrs6Model.NumRelearningSteps, 1 },
            });

    private static DateTime At(long unixMilliseconds) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).UtcDateTime;

    [Fact]
    public void TsFsrsSimulator_ShouldMatchTheSimulatorsStability_OverFourRealReviews()
    {
        var scheduler = CreateScheduler(
        [
            1.1596, 1.7974, 13.1205, 49.3729, 7.2303, 0.5081, 1.5371, 0.001, 1.5052,
            0.1261, 0.9735, 1.8924, 0.1486, 0.2407, 2.1937, 0.1518, 3.0699, 0.4636, 0.6048,
        ]);
        long[] rids = [1704468957000, 1704469645000, 1704599572000, 1705509507000];
        double[] expected = [13.1205, 17.3668145, 21.28550751, 39.63452215];

        var card = scheduler.NewCard(At(rids[0]));
        for (var i = 0; i < rids.Length; i++)
        {
            card = scheduler.Review(card, Rating.Good, At(rids[i])).Card;

            card.MemoryState.Stability.Should().BeApproximately(expected[i], 1e-4);
        }
    }

    [Fact]
    public void SseUseNextState_ShouldMatchTheSimulator_OverTenReviewsWithTwoSkips()
    {
        var scheduler = CreateScheduler(
        [
            0.4911, 4.5674, 24.8836, 77.045, 7.5474, 0.1873, 1.7732, 0.001, 1.1112,
            0.152, 0.5728, 1.8747, 0.1733, 0.2449, 2.2905, 0.0, 2.9898, 0.0883, 0.9033,
        ]);
        long[] rids =
        [
            1698678054940, 1698678126399, 1698688771401, 1698688837021, 1698688916440,
            1698698192380, 1699260169343, 1702718934003, 1704910583686, 1713000017248,
        ];
        // Rating 0 is Manual: the entry is in the log but is skipped, so the two long gaps that follow are
        // measured from the last real review.
        Rating[] ratings =
        [
            Rating.Good, Rating.Good, Rating.Again, Rating.Good, Rating.Good,
            Rating.Good, Rating.Manual, Rating.Good, Rating.Manual, Rating.Good,
        ];

        var card = scheduler.NewCard(At(rids[0]));
        for (var i = 0; i < rids.Length; i++)
        {
            if (ratings[i] == Rating.Manual) continue;
            card = scheduler.Review(card, ratings[i], At(rids[i])).Card;
        }

        card.MemoryState.Stability.Should().BeApproximately(71.77, 0.005);
    }
}
