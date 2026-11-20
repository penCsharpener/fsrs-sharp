using AwesomeAssertions;
using Xunit;

namespace FsrsSharp.Tests;

/// <summary>Port of <c>FSRS-5.test.ts</c>. Same shape as <see cref="Fsrs6Tests"/>, but seeded with a
/// nineteen-weight FSRS-5 vector — so as well as the scheduler, this pins the *migration* path: the weights
/// are upgraded to twenty-one on the way in (w19 = 0, w20 = the fixed FSRS-5 decay of 0.5), and the golden
/// intervals only come out right if that upgrade is exact.</summary>
public class Fsrs5Tests
{
    private static Fsrs Create(bool? enableShortTerm = null) =>
        TestParameters.CreateWith(TestParameters.W5, enableShortTerm);

    [Fact]
    public void Repeat_ShouldMatchTheUpstreamGoldenVector_OverThirteenReviews()
    {
        var f = Create();
        var card = Defaults.CreateEmptyCard();
        var now = TestParameters.Epoch;
        var scheduling = f.Repeat(card, now);
        var intervalHistory = new List<int>();

        Rating[] ratings =
        [
            Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good,
            Rating.Again, Rating.Again,
            Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good,
        ];

        foreach (var rating in ratings)
        {
            foreach (var check in Helpers.Grades)
            {
                var (checkedCard, log) = scheduling[check];

                f.Rollback(checkedCard, log).Should().BeEquivalentTo(card);
                log.ElapsedDays.Should().Be(card.LastReview is { } last
                    ? (int)Helpers.DateDiff(now, last, Helpers.DiffUnit.Days)
                    : 0);
                scheduling[check].Should().BeEquivalentTo(Create().Next(card, now, check));
            }

            card = scheduling[rating].Card;
            intervalHistory.Add(card.ScheduledDays);
            now = card.Due;
            scheduling = f.Repeat(card, now);
        }

        intervalHistory.Should().Equal(0, 4, 14, 44, 125, 328, 0, 0, 7, 16, 34, 71, 142);
    }

    [Fact]
    public void FirstRepeat_ShouldMatchUpstream_ForEveryGrade()
    {
        var cards = Create().Repeat(Defaults.CreateEmptyCard(), TestParameters.Epoch)
            .AsEnumerable().Select(item => item.Card).ToList();

        cards.Select(c => c.Stability).Should().Equal(0.40255, 1.18385, 3.173, 15.69105);
        cards.Select(c => c.Difficulty).Should().Equal(7.1949, 6.48830527, 5.28243442, 3.22450159);
        cards.Select(c => c.Reps).Should().Equal(1, 1, 1, 1);
        cards.Select(c => c.Lapses).Should().Equal(0, 0, 0, 0);
        cards.Select(c => c.ElapsedDays).Should().Equal(0, 0, 0, 0);
        cards.Select(c => c.ScheduledDays).Should().Equal(0, 0, 0, 16);
        cards.Select(c => c.State)
            .Should().Equal(State.Learning, State.Learning, State.Learning, State.Review);
    }

    [Theory]
    [InlineData(true, 48.26549438)]
    [InlineData(false, 48.065163)]
    public void Next_ShouldReachTheUpstreamMemoryState_AfterSixReviews(bool enableShortTerm, double expected)
    {
        Rating[] ratings =
            [Rating.Again, Rating.Good, Rating.Good, Rating.Good, Rating.Good, Rating.Good];
        int[] intervals = [0, 0, 1, 3, 8, 21];

        var f = Create(enableShortTerm);
        var card = Defaults.CreateEmptyCard();
        var now = TestParameters.Epoch;
        for (var i = 0; i < ratings.Length; i++)
        {
            now = now.AddDays(intervals[i]);
            card = f.Next(card, now, ratings[i]).Card;
        }

        card.Stability.Should().BeApproximately(expected, 1e-4);
        card.Difficulty.Should().BeApproximately(7.10441712, 1e-4);
    }
}
