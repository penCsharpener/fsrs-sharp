using AwesomeAssertions;
using Xunit;

namespace FsrsSharp.Tests;

/// <summary>Port of the portable half of <c>strategies/seed.test.ts</c>. Upstream reaches into private fields
/// to assert which seed string was installed; there is no equivalent hook here, so what's checked instead is
/// the behaviour those assertions exist to protect — with fuzzing on, the seed decides the interval, so two
/// cards that differ only in their id must be able to fuzz differently, and the same card must fuzz the same
/// way every time.</summary>
public class SeedStrategyTests
{
    private static Fsrs Fuzzing() => new(new FsrsParametersInput
    {
        EnableFuzz = true,
        EnableShortTerm = false,
    });

    private static Card CardWithStability(double stability, int reps)
    {
        var card = Defaults.CreateEmptyCard(TestParameters.Epoch);
        card.State = State.Review;
        card.Stability = stability;
        card.Difficulty = 5;
        card.Reps = reps;
        card.LastReview = TestParameters.Epoch;
        return card;
    }

    [Fact]
    public void DefaultSeedStrategy_ShouldBeStableForTheSameReview()
    {
        var f = Fuzzing();
        var card = CardWithStability(50, reps: 3);
        var now = TestParameters.Epoch.AddDays(50);

        var dueDates = Enumerable.Range(0, 20)
            .Select(_ => f.Next(card, now, Rating.Good).Card.Due)
            .Distinct();

        dueDates.Should().ContainSingle();
    }

    [Fact]
    public void UseSeedStrategy_ShouldChangeTheFuzzedInterval()
    {
        var card = CardWithStability(50, reps: 3);
        var now = TestParameters.Epoch.AddDays(50);

        var withDefault = Fuzzing().Next(card, now, Rating.Good).Card.ScheduledDays;

        // A constant seed is the crudest possible custom strategy, and enough to prove the hook is wired: if
        // the seed were ignored, this could not differ from the default.
        var intervals = Enumerable.Range(0, 40)
            .Select(i => Fuzzing().UseSeedStrategy(_ => i.ToString()).Next(card, now, Rating.Good).Card.ScheduledDays)
            .ToList();

        intervals.Distinct().Should().HaveCountGreaterThan(1);
        intervals.Should().Contain(days => days != withDefault);
    }

    [Fact]
    public void WithCardId_ShouldFuzzDifferentCardsDifferently()
    {
        var now = TestParameters.Epoch.AddDays(50);
        var ids = Enumerable.Range(1, 40).ToList();

        var intervals = ids.Select(id =>
        {
            var card = CardWithStability(50, reps: 3);
            var f = Fuzzing().UseSeedStrategy(Strategies.WithCardId(_ => id));
            return f.Next(card, now, Rating.Good).Card.ScheduledDays;
        }).ToList();

        intervals.Distinct().Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void WithCardId_ShouldFallBackToTheRepCount_WhenThereIsNoId()
    {
        // Upstream's "card_id is undefined/null" cases: a null selector result seeds from 0 plus the reps,
        // rather than throwing or silently producing an unseeded generator.
        var now = TestParameters.Epoch.AddDays(50);
        var card = CardWithStability(50, reps: 3);

        var withNullId = Fuzzing().UseSeedStrategy(Strategies.WithCardId(_ => null))
            .Next(card, now, Rating.Good).Card.Due;
        var withZeroId = Fuzzing().UseSeedStrategy(Strategies.WithCardId(_ => 0))
            .Next(card, now, Rating.Good).Card.Due;

        withNullId.Should().Be(withZeroId);
    }

    [Fact]
    public void UseSeedStrategy_ShouldRestoreTheDefault_WhenPassedNull()
    {
        var card = CardWithStability(50, reps: 3);
        var now = TestParameters.Epoch.AddDays(50);
        var expected = Fuzzing().Next(card, now, Rating.Good).Card.Due;

        var f = Fuzzing().UseSeedStrategy(_ => "fixed-seed");
        f.UseSeedStrategy(null);

        f.Next(card, now, Rating.Good).Card.Due.Should().Be(expected);
    }
}
