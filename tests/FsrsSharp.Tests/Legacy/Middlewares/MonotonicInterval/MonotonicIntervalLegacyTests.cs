using AwesomeAssertions;
using FsrsSharp.Legacy;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;
using Xunit;

namespace FsrsSharp.Tests.Legacy.Middlewares.MonotonicInterval;

/// <summary>Port of <c>legacy/__tests__/middlewares/monotonic-interval/legacy.spec.ts</c>.</summary>
public class MonotonicIntervalLegacyTests
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);
    private static readonly DateTime Start = TestParameters.Utc(2026, 1, 1);

    private static double RawInterval(Fsrs scheduler, Card card, DateTime now, Rating grade)
    {
        double elapsedDays = card.LastReview is { } last ? LegacyHelpers.DateDiffInDays(last, now) : 0;
        var memoryState = new FsrsState(card.Stability, card.Difficulty);
        var nextMemoryState = scheduler.Model.Step(new(
            memoryState,
            grade,
            elapsedDays,
            card.State == State.New ? null : scheduler.Model.ForgettingCurve(memoryState, elapsedDays)));

        return Helpers.JsRound(scheduler.Model.NextInterval(nextMemoryState, scheduler.Parameters.RequestRetention));
    }

    private static double[] ExpectedLongTermSchedule(Fsrs scheduler, Card card, DateTime now) =>
        FsrsSharp.Middlewares.MonotonicInterval.CalculateScheduleDays(
            Grades.All.Select(g => RawInterval(scheduler, card, now, g)).ToArray(),
            scheduler.Parameters.MaximumInterval);

    private static double[] ExpectedShortTermReviewSchedule(Fsrs scheduler, Card card, DateTime now) =>
        FsrsSharp.Middlewares.MonotonicInterval.CalculateScheduleDays(
            Grades.All.Skip(1).Select(g => RawInterval(scheduler, card, now, g)).ToArray(),
            scheduler.Parameters.MaximumInterval);

    private static void ExpectPublicDaySchedule(Fsrs scheduler, Card card, DateTime now, double[] expected, IReadOnlyList<Rating>? ratings = null)
    {
        ratings ??= Grades.All;
        var record = scheduler.Repeat(card, now);

        ratings.Select(rating => (double)record[rating].Card.ScheduledDays).Should().Equal(expected);

        for (var index = 0; index < ratings.Count; index++)
        {
            var next = scheduler.Next(card, now, ratings[index]);
            next.Should().BeEquivalentTo(record[ratings[index]]);
            next.Card.ScheduledDays.Should().Be((int)expected[index]);
            (next.Card.Due - now).Should().Be(Day * expected[index]);
        }
    }

    [Fact]
    public void KeepsAllLongTermNewCardRatingsOnTheFourGradeChain()
    {
        var scheduler = new Fsrs(new FsrsParametersInput { EnableFuzz = false, EnableShortTerm = false });
        var card = Defaults.CreateEmptyCard(Start);
        var expected = ExpectedLongTermSchedule(scheduler, card, Start);

        expected.Should().Equal(1, 2, 3, 8);
        ExpectPublicDaySchedule(scheduler, card, Start, expected);
    }

    [Fact]
    public void KeepsAllLongTermReviewRatingsOnTheFourGradeChain()
    {
        var scheduler = new Fsrs(new FsrsParametersInput { EnableFuzz = false, EnableShortTerm = false });
        var card = scheduler.Next(Defaults.CreateEmptyCard(Start), Start, Rating.Easy).Card;
        var now = card.Due;
        var expected = ExpectedLongTermSchedule(scheduler, card, now);

        expected.Should().Equal(1, 27, 39, 66);
        ExpectPublicDaySchedule(scheduler, card, now, expected);
    }

    [Fact]
    public void KeepsFuzzingBeforeTheLongTermMonotonicChain()
    {
        var scheduler = new Fsrs(new FsrsParametersInput { EnableFuzz = true, EnableShortTerm = false });
        var card = scheduler.Next(Defaults.CreateEmptyCard(Start), Start, Rating.Easy).Card;
        var now = card.Due;

        ExpectPublicDaySchedule(scheduler, card, now, [1, 28, 40, 68]);
    }

    [Fact]
    public void KeepsAgainOutsideTheShortTermReviewDayChain()
    {
        var scheduler = new Fsrs(new FsrsParametersInput { EnableFuzz = false, EnableShortTerm = true, RelearningSteps = ["10m"] });
        var card = scheduler.Next(Defaults.CreateEmptyCard(Start), Start, Rating.Easy).Card;
        var now = card.Due;
        var expected = ExpectedShortTermReviewSchedule(scheduler, card, now);

        expected.Should().Equal(27, 39, 66);
        ExpectPublicDaySchedule(scheduler, card, now, expected, Grades.All.Skip(1).ToList());

        var record = scheduler.Repeat(card, now);
        var nextAgain = scheduler.Next(card, now, Rating.Again);
        nextAgain.Should().BeEquivalentTo(record[Rating.Again]);
        nextAgain.Card.State.Should().Be(State.Relearning);
        nextAgain.Card.ScheduledDays.Should().Be(0);
        (nextAgain.Card.Due - now).Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void SaturatesLaterLongTermRatingsAtMaximumInterval()
    {
        var scheduler = new Fsrs(new FsrsParametersInput { EnableFuzz = false, EnableShortTerm = false, MaximumInterval = 2 });
        var card = Defaults.CreateEmptyCard(Start);
        var expected = ExpectedLongTermSchedule(scheduler, card, Start);

        expected.Should().Equal(1, 2, 2, 2);
        ExpectPublicDaySchedule(scheduler, card, Start, expected);
    }
}
