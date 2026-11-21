using AwesomeAssertions;
using FsrsSharp.Legacy;
using Xunit;

namespace FsrsSharp.Tests.Legacy;

/// <summary>Port of the "get retrievability" group shared by <c>FSRS-5.test.ts</c> and <c>FSRS-6.test.ts</c>,
/// plus the self-consistency property that makes <c>RequestRetention</c> mean what its name says.</summary>
public class RetrievabilityTests
{
    [Fact]
    public void Retrievability_ShouldBeZero_ForACardThatHasNeverBeenReviewed()
    {
        var f = new Fsrs();

        f.Retrievability(Defaults.CreateEmptyCard(TestParameters.FixedClock), TestParameters.FixedClock)
            .Should().Be(0);
    }

    [Fact]
    public void Retrievability_ShouldMatchUpstream_AtEachGradesOwnDueDate()
    {
        var f = new Fsrs();
        var card = Defaults.CreateEmptyCard(TestParameters.Utc(2023, 12, 1, 4, 0, 0));
        var scheduling = f.Repeat(card, TestParameters.Utc(2023, 12, 1, 4, 5, 0));

        double[] expected = [1, 1, 1, 0.9024733];

        // Again/Hard/Good are still on a minutes-long learning step, so zero whole days have elapsed and the
        // curve hasn't moved; only Easy, which graduated to a real interval, has decayed.
        LegacyHelpers.Grades.Select((grade, i) =>
        {
            var item = scheduling[grade];
            f.Retrievability(item.Card, item.Card.Due).Should().Be(expected[i]);
            return grade;
        }).Should().HaveCount(4);
    }

    [Fact]
    public void Retrievability_ShouldStayFinite_ThroughRepeatedLapses()
    {
        // Upstream's "loop Again": a card that is failed over and over drives stability towards S_MIN, and
        // the forgetting curve must not divide its way to NaN down there.
        var f = new Fsrs();
        var card = Defaults.CreateEmptyCard(TestParameters.FixedClock);
        var now = TestParameters.FixedClock;

        for (var i = 0; i < 5; i++)
        {
            card = f.Next(card, now, Rating.Again).Card;
            now = card.Due;

            double.IsNaN(f.Retrievability(card, now)).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(0.9)]
    [InlineData(0.8)]
    public void Retrievability_ShouldLandOnTheRequestedRetention_AtTheScheduledInterval(double requestRetention)
    {
        // Long-term scheduler so a single Good graduates straight to Review with a real multi-day interval —
        // the short-term one would leave the card on a 10-minute learning step with nothing to measure.
        var f = TestParameters.Create(enableShortTerm: false, requestRetention);
        var reviewed = f.Next(Defaults.CreateEmptyCard(), TestParameters.FixedClock, Rating.Good).Card;

        var atDue = reviewed.LastReview!.Value.AddDays(reviewed.ScheduledDays);

        // 0.02 rather than something tighter: the scheduled interval is a whole number of days, so it can only
        // ever approximate the exact instant the curve crosses the requested retention.
        f.Retrievability(reviewed, atDue).Should().BeApproximately(requestRetention, 0.02);
    }
}
