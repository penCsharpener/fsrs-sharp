using AwesomeAssertions;
using FsrsSharp.Legacy;
using Xunit;

namespace FsrsSharp.Tests.Legacy;

/// <summary>Port of <c>forget.test.ts</c>: resetting a card to New, with and without clearing its counters,
/// from every grade's outcome and from an untouched card.</summary>
public class ForgetTests
{
    private static readonly DateTime ForgetAt = TestParameters.Utc(2023, 12, 30, 12, 30, 0);

    private static Fsrs Create() => new(new FsrsParametersInput
    {
        W = TestParameters.WRollback,
        EnableFuzz = false,
    });

    private static void ShouldBeForgotten(Card forgotten, Card original, int reps, int lapses, DateTime? lastReview)
    {
        forgotten.Due.Should().Be(ForgetAt);
        forgotten.Stability.Should().Be(original.Stability);
        forgotten.Difficulty.Should().Be(original.Difficulty);
        forgotten.ScheduledDays.Should().Be(original.ScheduledDays);
        forgotten.LearningSteps.Should().Be(original.LearningSteps);
        forgotten.State.Should().Be(State.New);
        forgotten.Reps.Should().Be(reps);
        forgotten.Lapses.Should().Be(lapses);
        forgotten.LastReview.Should().Be(lastReview);
    }

    [Fact]
    public void Forget_ShouldResetToAnEmptyCard_WhenAskedToResetCounts()
    {
        var f = Create();
        var card = Defaults.CreateEmptyCard(TestParameters.Epoch);
        var scheduling = f.Repeat(card, TestParameters.Epoch);

        foreach (var grade in LegacyHelpers.Grades)
        {
            var reviewed = scheduling[grade].Card;

            var (forgotten, log) = f.Forget(reviewed, ForgetAt, resetCount: true);

            // Everything the empty card had, except that last_review is deliberately kept: the review really
            // did happen, and forgetting the word doesn't unhappen it.
            ShouldBeForgotten(forgotten, card, reps: 0, lapses: 0, lastReview: reviewed.LastReview);
            log.Rating.Should().Be(Rating.Manual);
            var rollback = () => f.Rollback(forgotten, log);
            rollback.Should().Throw<FsrsValidationException>().WithMessage("Cannot rollback a manual rating");
        }
    }

    [Fact]
    public void Forget_ShouldKeepRepsAndLapses_WhenNotAskedToResetThem()
    {
        var f = Create();
        var card = Defaults.CreateEmptyCard(TestParameters.Epoch);
        var scheduling = f.Repeat(card, TestParameters.Epoch);

        foreach (var grade in LegacyHelpers.Grades)
        {
            var reviewed = scheduling[grade].Card;

            var (forgotten, log) = f.Forget(reviewed, ForgetAt);

            ShouldBeForgotten(forgotten, card, reviewed.Reps, reviewed.Lapses, reviewed.LastReview);
            log.Rating.Should().Be(Rating.Manual);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Forget_ShouldBeANoOpBeyondTheDueDate_ForACardThatWasNeverReviewed(bool resetCount)
    {
        var f = Create();
        var card = Defaults.CreateEmptyCard(TestParameters.Epoch);

        var forgotten = f.Forget(card, ForgetAt, resetCount).Card;

        ShouldBeForgotten(forgotten, card, reps: 0, lapses: 0, lastReview: null);
    }
}
