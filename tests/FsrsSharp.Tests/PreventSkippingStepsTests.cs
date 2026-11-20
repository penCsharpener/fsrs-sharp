using AwesomeAssertions;
using Xunit;

namespace FsrsSharp.Tests;

/// <summary>Port of <c>fixed/prevent-skipping.test.ts</c> — upstream's regression guard for ts-fsrs#311, where
/// a long step ladder could jump more than one rung at a time. With six learning steps and three relearning
/// steps configured, Good has to advance exactly one step per review and graduate only from the last one.</summary>
public class PreventSkippingStepsTests
{
    private static Fsrs Create() => new(new FsrsParametersInput
    {
        EnableFuzz = false,
        EnableShortTerm = true,
        LearningSteps = ["1m", "10m", "30m", "1h", "6h", "12h"],
        RelearningSteps = ["10m", "1h", "6h"],
    });

    [Fact]
    public void Learning_ShouldWalkEveryStepWithoutSkipping()
    {
        var f = Create();
        var empty = Defaults.CreateEmptyCard(TestParameters.Epoch);
        var card = f.Next(empty, empty.Due, Rating.Again).Card;

        card.State.Should().Be(State.Learning);
        card.LearningSteps.Should().Be(0);

        double[] goodMinutes = [10, 30, 60, 360, 720];
        for (var i = 0; i < goodMinutes.Length; i++)
        {
            var previousDue = card.Due;
            card = f.Next(card, card.Due, Rating.Good).Card;

            card.LearningSteps.Should().Be(i + 1);
            card.State.Should().Be(State.Learning);
            (card.Due - previousDue).Should().Be(TimeSpan.FromMinutes(goodMinutes[i]));
        }

        card.LearningSteps.Should().Be(5);
        var graduated = f.Next(card, card.Due, Rating.Good).Card;
        graduated.State.Should().Be(State.Review);
        graduated.LearningSteps.Should().Be(0);
    }

    [Fact]
    public void Relearning_ShouldWalkEveryStepWithoutSkipping()
    {
        var f = Create();
        var empty = Defaults.CreateEmptyCard(TestParameters.Epoch);
        var card = f.Next(empty, empty.Due, Rating.Easy).Card;
        card.State.Should().Be(State.Review);

        card = f.Next(card, card.Due, Rating.Again).Card;
        card.State.Should().Be(State.Relearning);
        card.LearningSteps.Should().Be(0);

        double[] goodMinutes = [60, 360];
        for (var i = 0; i < goodMinutes.Length; i++)
        {
            var previousDue = card.Due;
            card = f.Next(card, card.Due, Rating.Good).Card;

            card.LearningSteps.Should().Be(i + 1);
            card.State.Should().Be(State.Relearning);
            (card.Due - previousDue).Should().Be(TimeSpan.FromMinutes(goodMinutes[i]));
        }

        card.LearningSteps.Should().Be(2);
        var graduated = f.Next(card, card.Due, Rating.Good).Card;
        graduated.State.Should().Be(State.Review);
        graduated.LearningSteps.Should().Be(0);
    }
}
