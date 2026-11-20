using AwesomeAssertions;
using Xunit;

namespace FsrsSharp.Tests;

/// <summary>Port of <c>fixed/calc-elapsed-days.test.ts</c>. Both cases replay a real review log — actual
/// timestamps from a real collection, not synthetic offsets — which is what makes them worth having: they
/// catch elapsed-day arithmetic that looks right on tidy whole-day intervals and drifts on ragged ones.</summary>
public class SimulatorTests
{
    [Fact]
    public void Next_ShouldMatchTheSimulatorsStability_OverFourRealReviews()
    {
        var f = TestParameters.CreateWith(
        [
            1.1596, 1.7974, 13.1205, 49.3729, 7.2303, 0.5081, 1.5371, 0.001, 1.5052,
            0.1261, 0.9735, 1.8924, 0.1486, 0.2407, 2.1937, 0.1518, 3.0699, 0.4636, 0.6048,
        ]);
        long[] reviewedAt = [1704468957000, 1704469645000, 1704599572000, 1705509507000];
        double[] expected = [13.1205, 17.3668145, 21.28550751, 39.63452215];

        var card = Defaults.CreateEmptyCard(DateTimeOffset.FromUnixTimeMilliseconds(reviewedAt[0]).UtcDateTime);
        for (var i = 0; i < reviewedAt.Length; i++)
        {
            var now = DateTimeOffset.FromUnixTimeMilliseconds(reviewedAt[i]).UtcDateTime;
            card = f.Next(card, now, Rating.Good).Card;

            card.Stability.Should().BeApproximately(expected[i], 1e-4);
        }
    }

    [Fact]
    public void NextState_ShouldMatchTheSimulator_OverTenReviewsWithTwoSkips()
    {
        var f = TestParameters.CreateWith(
        [
            0.4911, 4.5674, 24.8836, 77.045, 7.5474, 0.1873, 1.7732, 0.001, 1.1112,
            0.152, 0.5728, 1.8747, 0.1733, 0.2449, 2.2905, 0.0, 2.9898, 0.0883, 0.9033,
        ]);
        long[] reviewedAt =
        [
            1698678054940, 1698678126399, 1698688771401, 1698688837021, 1698688916440,
            1698698192380, 1699260169343, 1702718934003, 1704910583686, 1713000017248,
        ];
        // Rating 0 is Manual: the entry is in the log but doesn't reset the "last real review" clock, which
        // is the whole point of the case — the two long gaps that follow are measured from before it.
        Rating[] ratings =
        [
            Rating.Good, Rating.Good, Rating.Again, Rating.Good, Rating.Good,
            Rating.Good, Rating.Manual, Rating.Good, Rating.Manual, Rating.Good,
        ];

        var last = DateTimeOffset.FromUnixTimeMilliseconds(reviewedAt[0]).UtcDateTime;
        FsrsMemoryState? memoryState = null;
        for (var i = 0; i < reviewedAt.Length; i++)
        {
            var current = DateTimeOffset.FromUnixTimeMilliseconds(reviewedAt[i]).UtcDateTime;
            var deltaT = Helpers.DateDiffInDays(last, current);

            memoryState = f.NextState(memoryState, deltaT, ratings[i]);

            if (ratings[i] != Rating.Manual)
            {
                last = current;
            }
        }

        memoryState!.Value.Stability.Should().BeApproximately(71.77, 0.005);
    }
}
