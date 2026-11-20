using Xunit;
using System.Text.Json;
using AwesomeAssertions;
using FsrsSharp.Models.Fsrs7;

namespace FsrsSharp.Tests.Models.Fsrs7;

/// <summary>Port of ts-fsrs <c>models/fsrs-7/parameters.spec.ts</c>.</summary>
public class Fsrs7ParametersTests
{
    // Reference: fsrs-rs c9562d6 and srs-benchmark b4b0254, evaluated in float32.
    private static readonly (double[] Input, double[] Expected)[] ReferenceClips =
        JsonDocument.Parse(Fsrs7ReferenceData.ClipsJson).RootElement.EnumerateArray()
            .Select(e => (
                e.GetProperty("input").EnumerateArray().Select(x => x.GetDouble()).ToArray(),
                e.GetProperty("expected").EnumerateArray().Select(x => x.GetDouble()).ToArray()))
            .ToArray();

    public static TheoryData<int> ClipCaseIndexes => [0, 1, 2];

    private static IReadOnlyList<double> Defaults => Fsrs7Constants.DefaultWeights;

    [Fact]
    public void ExportsAll34BoxBoundsThroughTheModelEntryPoint()
    {
        var bounds = Fsrs7Constants.ParameterBounds();
        bounds.Should().HaveCount(34);
        bounds.Select(b => b.Min).Should().Equal(ReferenceClips[0].Expected);
        bounds.Select(b => b.Max).Should().Equal(ReferenceClips[1].Expected);
    }

    [Fact]
    public void IsolatesClippingAndValidationFromMutationsToReturnedBounds()
    {
        var original = Fsrs7Constants.ParameterBounds();
        var bounds = Fsrs7Constants.ParameterBounds();
        bounds.Should().NotBeSameAs(original);

        bounds[0] = (50, 50);
        bounds[1] = (50, 50);

        Fsrs7Constants.ParameterBounds().Should().Equal(original);
        foreach (var (input, expected) in ReferenceClips)
        {
            Fsrs7Parameters.Clip(input).Should().Equal(expected);
            Fsrs7Parameters.Check(expected).Should().BeSameAs(expected);
        }
        var act = () => Fsrs7Parameters.Check(Enumerable.Repeat(50.0, 34).ToArray());
        act.Should().Throw<FsrsValidationException>();
    }

    [Fact]
    public void CopiesDefaultsAndExplicitWeightsWithoutChangingTheSource()
    {
        Fsrs7Parameters.Migrate().Should().Equal(Defaults);
        Fsrs7Parameters.Migrate([]).Should().Equal(Defaults);
        Fsrs7Parameters.Migrate(Defaults).Should().NotBeSameAs(Defaults);
        Fsrs7Parameters.Check(Defaults).Should().BeSameAs(Defaults);
        Fsrs7Parameters.Clip(Defaults).Should().Equal(Defaults);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(19)]
    [InlineData(21)]
    [InlineData(33)]
    [InlineData(35)]
    public void RejectsIncompatibleParameterLayouts(int length)
    {
        var weights = Enumerable.Repeat(1.0, length).ToArray();
        var migrate = () => Fsrs7Parameters.Migrate(weights);
        migrate.Should().Throw<FsrsValidationException>().WithMessage("*expected 34*");
        var check = () => Fsrs7Parameters.Check(weights);
        check.Should().Throw<FsrsValidationException>();
        Fsrs7Parameters.Clip(weights).Should().HaveCount(length);
    }

    [Theory]
    [MemberData(nameof(ClipCaseIndexes))]
    public void ClipsExactlyLikeFsrsRsIncludingDependentBounds(int index)
    {
        var (input, expected) = ReferenceClips[index];
        var before = input.ToArray();
        Fsrs7Parameters.Clip(input).Should().Equal(expected);
        input.Should().Equal(before);
        Fsrs7Parameters.Check(expected).Should().BeSameAs(expected);
        Fsrs7Parameters.Clip(expected).Should().Equal(expected);
    }

    [Fact]
    public void FloorsBase2AtHalfWhenBase1SitsBelowIt()
    {
        var weights = Defaults.ToArray();
        weights[25] = 0.3;
        weights[26] = 0.2;
        Fsrs7Parameters.Clip(weights)[26].Should().Be(0.5);
        Fsrs7Parameters.Migrate(weights).Should().Equal(weights);
        var act = () => Fsrs7Parameters.Check(weights);
        act.Should().Throw<FsrsValidationException>();
    }

    [Fact]
    public void RaisesBase2ToBase1OnceBase1ExceedsTheHalfFloor()
    {
        var weights = Defaults.ToArray();
        weights[25] = 0.7;
        weights[26] = 0.6;
        Fsrs7Parameters.Clip(weights)[26].Should().Be(0.7);
        var act = () => Fsrs7Parameters.Check(weights);
        act.Should().Throw<FsrsValidationException>();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RepairsNonFiniteParametersWithTheLowerBoundLikeClampSafe(double value)
    {
        for (var i = 0; i < 34; i++)
        {
            var weights = Defaults.ToArray();
            weights[i] = value;
            var check = () => Fsrs7Parameters.Check(weights);
            check.Should().Throw<FsrsValidationException>();
            var clipped = Fsrs7Parameters.Clip(weights);
            clipped.Should().OnlyContain(x => double.IsFinite(x));
            var checkClipped = () => Fsrs7Parameters.Check(clipped);
            checkClipped.Should().NotThrow();
        }
    }
}
