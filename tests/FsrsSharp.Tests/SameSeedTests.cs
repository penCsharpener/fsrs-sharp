using AwesomeAssertions;
using Xunit;

namespace FsrsSharp.Tests;

/// <summary>Port of <c>fixed/same-seed.test.ts</c> — upstream's regression guard for ts-fsrs#113, where fuzzing
/// leaked state between calls and the same card reviewed at the same instant came back with different due
/// dates. The seed is derived from the review time, the rep count and D*S, so scheduling the same review twice
/// has to land on the same day every time; anything else makes a scheduler that can't be re-run.</summary>
public class SameSeedTests
{
    private static readonly DateTime MockNow = TestParameters.Utc(2024, 8, 15);

    [Theory]
    [InlineData(true, 2024, 8, 16)]
    [InlineData(false, 2024, 8, 18)]
    public void Fuzzing_ShouldProduceTheSameDueDate_EveryTimeTheSameReviewIsScheduled(
        bool enableShortTerm, int year, int month, int day)
    {
        var card = new Fsrs(new FsrsParametersInput { EnableShortTerm = enableShortTerm })
            .Next(Defaults.CreateEmptyCard(MockNow), MockNow, Rating.Good).Card;
        var scheduler = new Fsrs(new FsrsParametersInput
        {
            EnableFuzz = true,
            EnableShortTerm = enableShortTerm,
        });
        var tomorrow = TestParameters.Utc(year, month, day);

        var dueDates = Enumerable.Range(0, 100)
            .Select(_ => scheduler.Next(card, tomorrow, Rating.Good).Card.Due)
            .Distinct();

        dueDates.Should().ContainSingle();
    }

    [Fact]
    public void Fuzzing_ShouldStillProduceDifferentDueDates_ForDifferentCards()
    {
        // The mirror of the case above: determinism must come from the seed, not from the fuzz being a no-op.
        var scheduler = new Fsrs(new FsrsParametersInput { EnableFuzz = true, EnableShortTerm = false });

        var dueOffsets = Enumerable.Range(0, 40)
            .Select(i =>
            {
                var card = Defaults.CreateEmptyCard(MockNow);
                card.Reps = i; // feeds the default seed strategy
                var reviewed = scheduler.Next(card, MockNow, Rating.Easy).Card;
                return reviewed.ScheduledDays;
            })
            .Distinct();

        dueOffsets.Should().HaveCountGreaterThan(1);
    }
}
