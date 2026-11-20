using AwesomeAssertions;
using Xunit;

namespace FsrsSharp.Tests;

/// <summary>Port of <c>elapsed_days.test.ts</c> (upstream's regression case for ts-fsrs#44). The review log's
/// elapsed-days field has to be the gap since the *previous* review, counted in whole days — zero for the very
/// first review of a card, and zero again for repeats later the same day, however many hours apart.</summary>
public class ElapsedDaysTests
{
    [Fact]
    public void ReviewLog_ShouldReportWholeDaysSinceThePreviousReview()
    {
        var f = new Fsrs();
        var card = Defaults.CreateEmptyCard(TestParameters.Utc(2023, 10, 18, 14, 32, 3, 370));

        (DateTime ReviewedAt, Rating Rating, int ExpectedElapsedDays)[] reviews =
        [
            (TestParameters.Utc(2023, 11, 5, 8, 27, 2, 605), Rating.Good, 0),   // first review of the card
            (TestParameters.Utc(2023, 11, 8, 15, 2, 9, 791), Rating.Again, 3),  // three days later
            (TestParameters.Utc(2023, 11, 8, 15, 2, 30, 799), Rating.Again, 0), // 21 seconds later
            (TestParameters.Utc(2023, 11, 8, 15, 4, 8, 739), Rating.Good, 0),   // 98 seconds later
        ];

        foreach (var (reviewedAt, rating, expectedElapsedDays) in reviews)
        {
            var item = f.Repeat(card, reviewedAt)[rating];

            item.Log.ElapsedDays.Should().Be(expectedElapsedDays);
            item.Log.ElapsedDays.Should().Be(card.LastReview is { } last
                ? (int)Helpers.DateDiff(reviewedAt, last, Helpers.DiffUnit.Days)
                : 0);

            card = item.Card;
        }
    }
}
