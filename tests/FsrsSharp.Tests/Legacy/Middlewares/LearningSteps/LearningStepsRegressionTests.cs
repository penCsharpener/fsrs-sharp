using AwesomeAssertions;
using FsrsSharp.Legacy;
using Xunit;

namespace FsrsSharp.Tests.Legacy.Middlewares.LearningSteps;

/// <summary>Port of <c>legacy/__tests__/middlewares/learning-steps/regression.spec.ts</c> (upstream's #311).</summary>
public class LearningStepsRegressionTests
{
    private static Fsrs CreateScheduler() => new(new FsrsParametersInput
    {
        EnableFuzz = false,
        EnableShortTerm = true,
        LearningSteps = ["1m", "10m", "30m", "1h", "6h", "12h"],
        RelearningSteps = ["10m", "1h", "6h"],
    });

    [Fact]
    public void ExhaustsAllLearningStepsWithoutSkipping()
    {
        var scheduler = CreateScheduler();
        var emptyCard = Defaults.CreateEmptyCard();
        var card = scheduler.Next(emptyCard, emptyCard.Due, Rating.Again).Card;
        card.State.Should().Be(State.Learning);
        card.LearningSteps.Should().Be(0);

        int[] minutesPerStep = [10, 30, 60, 360, 720];
        for (var index = 0; index < minutesPerStep.Length; index++)
        {
            var previousDue = card.Due;
            card = scheduler.Next(card, card.Due, Rating.Good).Card;
            card.LearningSteps.Should().Be(index + 1);
            card.State.Should().Be(State.Learning);
            (card.Due - previousDue).Should().Be(TimeSpan.FromMinutes(minutesPerStep[index]));
        }

        var finalCard = scheduler.Next(card, card.Due, Rating.Good).Card;
        finalCard.State.Should().Be(State.Review);
        finalCard.LearningSteps.Should().Be(0);
    }

    [Fact]
    public void ExhaustsAllRelearningStepsWithoutSkipping()
    {
        var scheduler = CreateScheduler();
        var emptyCard = Defaults.CreateEmptyCard();
        var card = scheduler.Next(emptyCard, emptyCard.Due, Rating.Easy).Card;
        card = scheduler.Next(card, card.Due, Rating.Again).Card;
        card.State.Should().Be(State.Relearning);
        card.LearningSteps.Should().Be(0);

        int[] minutesPerStep = [60, 360];
        for (var index = 0; index < minutesPerStep.Length; index++)
        {
            var previousDue = card.Due;
            card = scheduler.Next(card, card.Due, Rating.Good).Card;
            card.LearningSteps.Should().Be(index + 1);
            card.State.Should().Be(State.Relearning);
            (card.Due - previousDue).Should().Be(TimeSpan.FromMinutes(minutesPerStep[index]));
        }

        var finalCard = scheduler.Next(card, card.Due, Rating.Good).Card;
        finalCard.State.Should().Be(State.Review);
        finalCard.LearningSteps.Should().Be(0);
    }
}
