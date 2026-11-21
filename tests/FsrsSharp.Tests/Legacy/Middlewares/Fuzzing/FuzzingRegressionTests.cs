using AwesomeAssertions;
using FsrsSharp.Legacy;
using Xunit;

namespace FsrsSharp.Tests.Legacy.Middlewares.Fuzzing;

/// <summary>Port of <c>legacy/__tests__/middlewares/fuzzing/regression.spec.ts</c> (upstream's #113): fuzzing is
/// deterministic for identical scheduling.</summary>
public class FuzzingRegressionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReturnsTheSameDueTime_ForRepeatedIdenticalScheduling(bool enableShortTerm)
    {
        var firstReview = new DateTime(2024, 8, 15, 0, 0, 0, DateTimeKind.Utc);
        var reviewTime = new DateTime(2024, 8, 18, 0, 0, 0, DateTimeKind.Utc);
        var initial = new Fsrs(new FsrsParametersInput { EnableShortTerm = enableShortTerm })
            .Next(Defaults.CreateEmptyCard(), firstReview, Rating.Good).Card;
        var scheduler = new Fsrs(new FsrsParametersInput { EnableFuzz = true, EnableShortTerm = enableShortTerm });

        var dueTimes = Enumerable.Range(0, 100)
            .Select(_ => scheduler.Next(initial, reviewTime, Rating.Good).Card.Due)
            .ToList();

        dueTimes.Should().AllSatisfy(due => due.Should().Be(dueTimes[0]));
    }
}
