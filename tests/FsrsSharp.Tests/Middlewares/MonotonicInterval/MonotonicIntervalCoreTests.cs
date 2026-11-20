using AwesomeAssertions;
using FsrsSharp.Middlewares;
using Xunit;

namespace FsrsSharp.Tests.Middlewares.MonotonicInterval;

/// <summary>Port of <c>middlewares/monotonic-interval/core.spec.ts</c>.</summary>
public class MonotonicIntervalCoreTests
{
    private static double[] Days(double[] candidates, double max = 100) => FsrsSharp.Middlewares.MonotonicInterval.CalculateScheduleDays(candidates, max);

    private static double Day(double[] candidates, double max = 100) => FsrsSharp.Middlewares.MonotonicInterval.CalculateScheduleDay(candidates, max);

    [Fact]
    public void KeepsTheFirstIntervalAndAdvancesEachLaterRating()
    {
        Days([4]).Should().Equal(4);
        Days([4, 2]).Should().Equal(4, 5);
        Days([4, 2, 8]).Should().Equal(4, 5, 8);
        Days([4, 2, 8, 7]).Should().Equal(4, 5, 8, 9);
    }

    [Fact]
    public void PreservesAlreadyMonotonicIntervals() => Days([2, 4, 8, 16]).Should().Equal(2, 4, 8, 16);

    [Fact]
    public void AllowsZeroDayIntervalsToRemainEqual()
    {
        Days([0, 0, 2, 3]).Should().Equal(0, 0, 2, 3);
        Days([0, 0, 0, 0]).Should().Equal(0, 0, 0, 0);
        Days([0, 1, 1, 1]).Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void AllowsEqualIntervalsOnceTheMaximumIsReached()
    {
        Days([98, 99, 100, 101]).Should().Equal(98, 99, 100, 100);
        Days([100, 100, 100]).Should().Equal(100, 100, 100);
    }

    [Fact]
    public void CapsTheFirstIntervalWithoutComparingItWithTheNextRating()
    {
        Days([102]).Should().Equal(100);
        Days([102, 101, 100]).Should().Equal(100, 100, 100);
    }

    [Fact]
    public void DoesNotMutateTheCandidateTuple()
    {
        double[] candidates = [8, 5, 4, 3];

        var result = Days(candidates);
        result[0] = 1;

        candidates.Should().Equal(8, 5, 4, 3);
    }

    [Fact]
    public void ReturnsTheCurrentRatingInterval()
    {
        Day([4]).Should().Be(4);
        Day([4, 2]).Should().Be(5);
        Day([4, 2, 8]).Should().Be(8);
        Day([4, 2, 8, 7]).Should().Be(9);
        Day([0, 0, 2, 3]).Should().Be(3);
        Day([98, 99, 100, 101]).Should().Be(100);
        Day([102]).Should().Be(100);
    }

    [Fact]
    public void KeepsSubDayIntervalsNonDecreasingAndSeparatesEqualSeconds()
    {
        const double second = 1.0 / 86400;
        Day([1.0 / 1440]).Should().Be(1.0 / 1440);
        Day([1.0 / 1440, 5.0 / 1440]).Should().Be(5.0 / 1440);
        Day([5.0 / 1440, 1.0 / 1440]).Should().Be(5.0 / 1440);
        Day([0.5, 0.5 + second / 3]).Should().Be(0.5 + second);
        Day([0.5, 0.5, 0.5]).Should().Be(0.5 + second + second);
        Day([0, 0, 5.0 / 1440]).Should().Be(5.0 / 1440);
        // A higher candidate must not collapse onto an adjusted lower rating.
        Day([0.5, 0.5, 0.5 + 0.6 * second]).Should().Be(0.5 + second + second);
    }

    [Fact]
    public void IgnoresSubDayCandidatesWhenADayIntervalExists()
    {
        Day([1, 5.0 / 1440]).Should().Be(1);
        Day([1.0 / 1440, 5.0 / 1440, 1, 1]).Should().Be(2);
        Day([1, 5.0 / 1440, 10.0 / 1440, 1]).Should().Be(2);
    }
}
