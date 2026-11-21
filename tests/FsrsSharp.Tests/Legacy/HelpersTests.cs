using AwesomeAssertions;
using FsrsSharp.Legacy;
using Xunit;

namespace FsrsSharp.Tests.Legacy;

/// <summary>Port of the portable half of <c>help.test.ts</c>. The <c>formatDate</c> and <c>TypeConvert</c>
/// groups have no counterpart here on purpose: this port takes <see cref="DateTime"/> directly instead of
/// ts-fsrs's <c>DateInput</c> union, so there is no string/number parsing layer to test.</summary>
public class HelpersTests
{
    [Fact]
    public void Grades_ShouldBeTheFourGradableRatingsInOrder()
    {
        LegacyHelpers.Grades.Should().Equal(Rating.Again, Rating.Hard, Rating.Good, Rating.Easy);
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

        LegacyHelpers.DateScheduler(now, 30).Should().Be(TestParameters.Utc(2023, 1, 1, 12, 30));
    }

    [Fact]
    public void DateScheduler_ShouldOffsetByDays_WhenAsked()
    {
        var now = TestParameters.Utc(2023, 1, 1, 12);

        LegacyHelpers.DateScheduler(now, 3, isDay: true).Should().Be(TestParameters.Utc(2023, 1, 4, 12));
    }

    [Fact]
    public void DateScheduler_ShouldAcceptANegativeOffset()
    {
        var now = TestParameters.Utc(2023, 1, 1, 12);

        LegacyHelpers.DateScheduler(now, -15).Should().Be(TestParameters.Utc(2023, 1, 1, 11, 45));
    }

    [Fact]
    public void DateDiff_ShouldCountWholeMinutes()
    {
        var now = TestParameters.Utc(2023, 11, 25, 12, 30);
        var pre = TestParameters.Utc(2023, 11, 25, 12, 0);

        LegacyHelpers.DateDiff(now, pre, LegacyHelpers.DiffUnit.Minutes).Should().Be(30);
    }

    [Fact]
    public void DateDiff_ShouldFloorTowardsNegativeInfinity_ForANegativeDifference()
    {
        var now = TestParameters.Utc(2023, 11, 25, 12, 0);
        var pre = TestParameters.Utc(2023, 11, 25, 12, 30);

        LegacyHelpers.DateDiff(now, pre, LegacyHelpers.DiffUnit.Minutes).Should().Be(-30);
    }

    [Fact]
    public void DateDiff_ShouldCountWholeDays()
    {
        var now = TestParameters.Utc(2022, 12, 30, 12, 30);
        var pre = TestParameters.Utc(2022, 12, 29, 12, 30);

        LegacyHelpers.DateDiff(now, pre, LegacyHelpers.DiffUnit.Days).Should().Be(1);
    }

    [Fact]
    public void DateDiffInDays_ShouldIgnoreTimeOfDay()
    {
        // Calendar days, not 24-hour blocks: 23:59 to 00:01 the next morning is one day, not zero.
        LegacyHelpers.DateDiffInDays(TestParameters.Utc(2023, 1, 1, 23, 59), TestParameters.Utc(2023, 1, 2, 0, 1))
            .Should().Be(1);
        LegacyHelpers.DateDiffInDays(TestParameters.Utc(2023, 1, 1, 0, 1), TestParameters.Utc(2023, 1, 1, 23, 59))
            .Should().Be(0);
    }

    [Fact]
    public void GetFuzzRange_ShouldKeepTheLegacyReturnShape()
    {
        LegacyHelpers.GetFuzzRange(10, 5, 36_500).Should().Be((8, 12));
    }

    [Fact]
    public void FormatDate_ShouldFormatTheLocalFields()
    {
        var now = new DateTime(2022, 12, 30, 12, 30, 0, 0, DateTimeKind.Local);

        LegacyHelpers.FormatDate(now).Should().Be("2022-12-30 12:30:00");
        LegacyHelpers.FormatDate(now.ToUniversalTime()).Should().Be("2022-12-30 12:30:00");
    }

    [Fact]
    public void DateScheduler_ShouldTruncateToWholeMilliseconds_LikeAJsDate()
    {
        // 1.5 s offsets are exact; a sub-millisecond remainder is dropped, not rounded.
        var now = TestParameters.Utc(2023, 1, 1, 12);

        LegacyHelpers.DateScheduler(now, 1.5 / 60).Should().Be(now.AddMilliseconds(1500));
        LegacyHelpers.DateScheduler(now, 0.0000333).Should().Be(now.AddMilliseconds(1));
    }
}
