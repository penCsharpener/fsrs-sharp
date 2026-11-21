using AwesomeAssertions;
using FsrsSharp.Legacy;
using Xunit;

namespace FsrsSharp.Tests.Legacy;

/// <summary>Port of <c>rollback.test.ts</c>. <see cref="Fsrs.Rollback"/> is documented as the exact inverse of
/// <see cref="Fsrs.Next"/>, so reviewing a card and rolling that review back has to reconstruct the card that
/// went in — from a brand-new card and from one already in Review, for every grade. Upstream drives this with
/// a seventeen-weight FSRS-4.5 set, so the migration path is on the hook here too.</summary>
public class RollbackTests
{
    private static Fsrs Create() => new(new FsrsParametersInput
    {
        W = TestParameters.WRollback,
        EnableFuzz = false,
    });

    [Fact]
    public void Rollback_ShouldReconstructANewCard_ForEveryGrade()
    {
        var f = Create();
        var card = Defaults.CreateEmptyCard(TestParameters.Epoch);
        var scheduling = f.Repeat(card, TestParameters.Epoch);

        foreach (var grade in LegacyHelpers.Grades)
        {
            var (reviewed, log) = scheduling[grade];

            f.Rollback(reviewed, log).Should().BeEquivalentTo(card);
        }
    }

    [Fact]
    public void Rollback_ShouldReconstructAReviewCard_ForEveryGrade()
    {
        var f = Create();
        var card = f.Repeat(Defaults.CreateEmptyCard(TestParameters.Epoch), TestParameters.Epoch)[Rating.Easy].Card;
        var scheduling = f.Repeat(card, card.Due);

        foreach (var grade in LegacyHelpers.Grades)
        {
            var (reviewed, log) = scheduling[grade];

            f.Rollback(reviewed, log).Should().BeEquivalentTo(card);
        }
    }

    [Fact]
    public void Rollback_ShouldRefuseAManualRating()
    {
        var f = Create();
        var card = Defaults.CreateEmptyCard(TestParameters.Epoch);
        var (forgotten, log) = f.Forget(card, TestParameters.Epoch);

        // Forget is the one operation that writes a Manual entry, and Manual has no grade to invert — the
        // library is expected to say so rather than silently produce a nonsense card.
        var rollback = () => f.Rollback(forgotten, log);

        rollback.Should().Throw<FsrsValidationException>().WithMessage("Cannot rollback a manual rating");
    }
}
