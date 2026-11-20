using AwesomeAssertions;
using FsrsSharp.Models.Fsrs6;
using Xunit;

namespace FsrsSharp.Tests.Models.Fsrs6;

/// <summary>Port of <c>models/fsrs-6/parameters.spec.ts</c>.</summary>
public class Fsrs6ParametersTests
{
    [Fact]
    public void RequiresANonNegativeSafeIntegerRelearningStepCount()
    {
        IReadOnlyList<double> weights = [.. Fsrs6Constants.DefaultWeights];
        Fsrs6Parameters.ParseConfig(weights, true, 2).Should().Be(new Fsrs6Config(weights, true, 2));

        foreach (var numRelearningSteps in new[] { -0.5, 0.5, Math.Pow(2, 53) })
        {
            var act = () => Fsrs6Parameters.ParseConfig(weights, true, numRelearningSteps);
            act.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS6 config");
        }
    }

    [Fact]
    public void ValidatesDecay()
    {
        Fsrs6Parameters.ValidateDecay(Fsrs6Constants.Decay).Should().BeNull();
        Fsrs6Parameters.ValidateDecay(0.1).Should().BeNull();
        Fsrs6Parameters.ValidateDecay(0.8).Should().BeNull();
        Fsrs6Parameters.ValidateDecay(0.09).Should().Be("Expected decay between 0.1 and 0.8");
        Fsrs6Parameters.ValidateDecay(0.81).Should().Be("Expected decay between 0.1 and 0.8");
        Fsrs6Parameters.ValidateDecay(double.NaN).Should().Be("Expected decay between 0.1 and 0.8");
    }

    [Fact]
    public void ChecksDefaultWeightsWithDefaultOptions()
    {
        IReadOnlyList<double> weights = [.. Fsrs6Constants.DefaultWeights];

        Fsrs6Parameters.Check(weights).Should().BeSameAs(weights);
        Fsrs6Parameters.Clip(weights).Should().Equal(weights);
    }

    [Fact]
    public void RejectsIncorrectParameterLength()
    {
        var act = () => Fsrs6Parameters.Check(Fsrs6Constants.DefaultWeights.Take(20).ToArray());
        act.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS6 weights within model bounds.");
    }

    [Fact]
    public void MigratesWithoutClipping()
    {
        var weights = Fsrs6Constants.DefaultWeights.ToArray();
        weights[0] = 0;

        Fsrs6Parameters.Migrate(weights).Should().Equal(weights);
        var act = () => Fsrs6Parameters.Check(weights);
        act.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS6 weights within model bounds.");
    }
}
