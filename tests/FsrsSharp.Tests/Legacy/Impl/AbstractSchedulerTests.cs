using AwesomeAssertions;
using FsrsSharp.Legacy;
using Xunit;

namespace FsrsSharp.Tests.Legacy.Impl;

/// <summary>Port of <c>legacy/__tests__/impl/abstract_scheduler.test.ts</c>.</summary>
public class AbstractSchedulerTests
{
    [Fact]
    public void Preview_ShouldAgreeWithNext_ForEveryGradeAndEnumerateInGradeOrder()
    {
        // repeat() is only useful as a preview if it predicts exactly what next() would do.
        var f = new Fsrs();
        var now = TestParameters.FixedClock;
        var card = Defaults.CreateEmptyCard(now);

        var preview = f.Repeat(card, now);
        var again = f.Next(card, now, Rating.Again);
        var hard = f.Next(card, now, Rating.Hard);
        var good = f.Next(card, now, Rating.Good);
        var easy = f.Next(card, now, Rating.Easy);

        preview.Again.Should().BeEquivalentTo(again);
        preview.Hard.Should().BeEquivalentTo(hard);
        preview.Good.Should().BeEquivalentTo(good);
        preview.Easy.Should().BeEquivalentTo(easy);
        foreach (var item in preview.AsEnumerable())
        {
            item.Should().BeEquivalentTo(preview[item.Log.Rating]);
        }

        preview.AsEnumerable().Should().BeEquivalentTo(new[] { again, hard, good, easy }, o => o.WithStrictOrdering());
    }
}
