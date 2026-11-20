using AwesomeAssertions;
using Xunit;

namespace FsrsSharp.Tests;

/// <summary>Port of the portable half of <c>help.test.ts</c>. The <c>formatDate</c> and <c>TypeConvert</c>
/// groups have no counterpart here on purpose: this port takes <see cref="DateTime"/> directly instead of
/// ts-fsrs's <c>DateInput</c> union, so there is no string/number parsing layer to test.</summary>
public class HelpersTests
{
    [Fact]
    public void Grades_ShouldBeTheFourGradableRatingsInOrder()
    {
        Helpers.Grades.Should().Equal(Rating.Again, Rating.Hard, Rating.Good, Rating.Easy);
    }

    [Fact]
    public void JsRound_ShouldRoundHalfTowardsPositiveInfinity()
    {
        // Not .NET's banker's rounding: 0.5 -> 1 and 1.5 -> 2, matching JavaScript's Math.round. Exact
        // interval parity with ts-fsrs depends on this.
        Helpers.JsRound(0.5).Should().Be(1);
        Helpers.JsRound(1.5).Should().Be(2);
        Helpers.JsRound(2.5).Should().Be(3);
        Helpers.JsRound(2.4).Should().Be(2);
    }

    [Fact]
    public void DateScheduler_ShouldOffsetByMinutesByDefault()
    {
        var now = TestParameters.Utc(2023, 1, 1, 12);

        Helpers.DateScheduler(now, 30).Should().Be(TestParameters.Utc(2023, 1, 1, 12, 30));
    }

    [Fact]
    public void DateScheduler_ShouldOffsetByDays_WhenAsked()
    {
        var now = TestParameters.Utc(2023, 1, 1, 12);

        Helpers.DateScheduler(now, 3, isDay: true).Should().Be(TestParameters.Utc(2023, 1, 4, 12));
    }

    [Fact]
    public void DateScheduler_ShouldAcceptANegativeOffset()
    {
        var now = TestParameters.Utc(2023, 1, 1, 12);

        Helpers.DateScheduler(now, -15).Should().Be(TestParameters.Utc(2023, 1, 1, 11, 45));
    }

    [Fact]
    public void DateDiff_ShouldCountWholeMinutes()
    {
        var now = TestParameters.Utc(2023, 11, 25, 12, 30);
        var pre = TestParameters.Utc(2023, 11, 25, 12, 0);

        Helpers.DateDiff(now, pre, Helpers.DiffUnit.Minutes).Should().Be(30);
    }

    [Fact]
    public void DateDiff_ShouldFloorTowardsNegativeInfinity_ForANegativeDifference()
    {
        var now = TestParameters.Utc(2023, 11, 25, 12, 0);
        var pre = TestParameters.Utc(2023, 11, 25, 12, 30);

        Helpers.DateDiff(now, pre, Helpers.DiffUnit.Minutes).Should().Be(-30);
    }

    [Fact]
    public void DateDiff_ShouldCountWholeDays()
    {
        var now = TestParameters.Utc(2022, 12, 30, 12, 30);
        var pre = TestParameters.Utc(2022, 12, 29, 12, 30);

        Helpers.DateDiff(now, pre, Helpers.DiffUnit.Days).Should().Be(1);
    }

    [Fact]
    public void DateDiffInDays_ShouldIgnoreTimeOfDay()
    {
        // Calendar days, not 24-hour blocks: 23:59 to 00:01 the next morning is one day, not zero.
        Helpers.DateDiffInDays(TestParameters.Utc(2023, 1, 1, 23, 59), TestParameters.Utc(2023, 1, 2, 0, 1))
            .Should().Be(1);
        Helpers.DateDiffInDays(TestParameters.Utc(2023, 1, 1, 0, 1), TestParameters.Utc(2023, 1, 1, 23, 59))
            .Should().Be(0);
    }

    [Fact]
    public void GetFuzzRange_ShouldStayOrdered_WhenTheIntervalDoesNotExceedElapsedDays()
    {
        // Upstream's regression case for the branch that raises min_ivl above elapsed_days: it must not be
        // taken here, and min must never end up above max.
        var (minIvl, maxIvl) = Helpers.GetFuzzRange(5, 5, 100);

        minIvl.Should().BeLessThanOrEqualTo(maxIvl);
    }
}
