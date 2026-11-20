using AwesomeAssertions;
using Xunit;

namespace FsrsSharp.Tests;

/// <summary>Port of <c>impl/basic_scheduler.test.ts</c> and <c>impl/abstract_scheduler.test.ts</c>: previewing
/// all four grades has to agree, field for field, with applying each one individually — from every state a
/// card can be in. Upstream also exercises the preview object's <c>Symbol.iterator</c>; the equivalent here is
/// <see cref="RecordLog.AsEnumerable"/>, whose order is part of the contract.</summary>
public class BasicSchedulerTests
{
    private static readonly FsrsAlgorithm Algorithm = new(new FsrsParametersInput { W = TestParameters.W });

    private static Card CardIn(State state)
    {
        var card = Defaults.CreateEmptyCard(TestParameters.Epoch);
        return state switch
        {
            State.New => card,
            // Again on a new card lands in Learning; Easy graduates straight to Review.
            State.Learning => new BasicScheduler(card, TestParameters.Epoch, Algorithm).Review(Rating.Again).Card,
            State.Review => new BasicScheduler(card, TestParameters.Epoch, Algorithm).Review(Rating.Easy).Card,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
    }

    [Theory]
    [InlineData(State.New)]
    [InlineData(State.Learning)]
    [InlineData(State.Review)]
    public void Preview_ShouldAgreeWithReviewingEachGradeIndividually(State state)
    {
        var card = CardIn(state);
        var scheduler = new BasicScheduler(card, TestParameters.Epoch, Algorithm);

        var preview = scheduler.Preview();

        preview.Again.Should().BeEquivalentTo(scheduler.Review(Rating.Again));
        preview.Hard.Should().BeEquivalentTo(scheduler.Review(Rating.Hard));
        preview.Good.Should().BeEquivalentTo(scheduler.Review(Rating.Good));
        preview.Easy.Should().BeEquivalentTo(scheduler.Review(Rating.Easy));
    }

    [Theory]
    [InlineData(State.New)]
    [InlineData(State.Learning)]
    [InlineData(State.Review)]
    public void Preview_ShouldEnumerateInGradeOrder(State state)
    {
        var scheduler = new BasicScheduler(CardIn(state), TestParameters.Epoch, Algorithm);

        var items = scheduler.Preview().AsEnumerable().ToList();

        items.Should().HaveCount(4);
        items.Select(item => item.Log.Rating)
            .Should().Equal(Rating.Again, Rating.Hard, Rating.Good, Rating.Easy);
        foreach (var item in items)
        {
            item.Should().BeEquivalentTo(scheduler.Review(item.Log.Rating));
        }
    }

    [Theory]
    [InlineData(State.New)]
    [InlineData(State.Learning)]
    [InlineData(State.Review)]
    public void Review_ShouldRejectAGradeOutsideOneToFour(State state)
    {
        var scheduler = new BasicScheduler(CardIn(state), TestParameters.Epoch, Algorithm);

        var review = () => scheduler.Review(Rating.Manual);

        review.Should().Throw<FsrsValidationException>().WithMessage("Invalid grade*");
    }

    [Fact]
    public void Indexer_ShouldRejectAGradeOutsideOneToFour()
    {
        var preview = new BasicScheduler(CardIn(State.New), TestParameters.Epoch, Algorithm).Preview();

        var read = () => preview[Rating.Manual];

        read.Should().Throw<FsrsValidationException>().WithMessage("Invalid grade*");
    }

    [Fact]
    public void FsrsRepeat_ShouldAgreeWithFsrsNext_ForEveryGrade()
    {
        // Upstream's abstract_scheduler test, at the Fsrs level rather than the scheduler's: repeat() is only
        // useful as a preview if it predicts exactly what next() would do.
        var f = TestParameters.Create();
        var card = Defaults.CreateEmptyCard(TestParameters.Epoch);

        var preview = f.Repeat(card, TestParameters.Epoch);

        foreach (var grade in Helpers.Grades)
        {
            preview[grade].Should().BeEquivalentTo(f.Next(card, TestParameters.Epoch, grade));
        }
    }
}
