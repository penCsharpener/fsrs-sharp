using AwesomeAssertions;
using FsrsSharp.Kit;
using FsrsSharp.Legacy;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs6;
using Xunit;

namespace FsrsSharp.Tests.Legacy.Impl;

/// <summary>Port of <c>impl/basic_scheduler.test.ts</c> and <c>impl/abstract_scheduler.test.ts</c>: previewing
/// all four grades has to agree, field for field, with applying each one individually — from every state a
/// card can be in. Upstream also exercises the preview object's <c>Symbol.iterator</c>; the equivalent here is
/// <see cref="RecordLog.AsEnumerable"/>, whose order is part of the contract.</summary>
public class BasicSchedulerTests
{
    private static readonly FsrsParameters Params = Defaults.GeneratorParameters(new FsrsParametersInput { W = TestParameters.W });

    private static readonly IModelCore<FsrsState> Model = Fsrs6Model.Instance.Create(
        new Fsrs6Config(Params.W, Params.EnableShortTerm, Params.RelearningSteps.Length));

    private static Card CardIn(State state)
    {
        var card = Defaults.CreateEmptyCard(TestParameters.Epoch);
        return state switch
        {
            State.New => card,
            // Again on a new card lands in Learning; Easy graduates straight to Review.
            State.Learning => new BasicScheduler(card, TestParameters.Epoch, Model, Params).Review(Rating.Again).Card,
            State.Review => new BasicScheduler(card, TestParameters.Epoch, Model, Params).Review(Rating.Easy).Card,
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
        var scheduler = new BasicScheduler(card, TestParameters.Epoch, Model, Params);

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
        var scheduler = new BasicScheduler(CardIn(state), TestParameters.Epoch, Model, Params);

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
        var scheduler = new BasicScheduler(CardIn(state), TestParameters.Epoch, Model, Params);

        var review = () => scheduler.Review(Rating.Manual);

        review.Should().Throw<FsrsValidationException>().WithMessage("Invalid grade*");
    }

    [Fact]
    public void Indexer_ShouldRejectAGradeOutsideOneToFour()
    {
        var preview = new BasicScheduler(CardIn(State.New), TestParameters.Epoch, Model, Params).Preview();

        var read = () => preview[Rating.Manual];

        read.Should().Throw<FsrsValidationException>().WithMessage("Invalid grade*");
    }
}
