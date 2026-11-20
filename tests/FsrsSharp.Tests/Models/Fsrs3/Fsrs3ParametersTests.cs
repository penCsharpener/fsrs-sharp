using AwesomeAssertions;
using FsrsSharp.Models.Fsrs3;
using Xunit;

namespace FsrsSharp.Tests.Models.Fsrs3;

/// <summary>
/// Port of <c>models/fsrs-3/parameters.spec.ts</c>. The "clips holes in sparse weights as zero" case
/// (a JavaScript <c>new Array(3)</c>) has no C# counterpart: a <c>double</c> list cannot contain holes.
/// </summary>
public class Fsrs3ParametersTests
{
    [Fact]
    public void ReturnsFsrs3DefaultWeightsWhenInputIsMissing() =>
        Fsrs3Parameters.Migrate(null).Should().Equal(Fsrs3Constants.DefaultWeights);

    [Fact]
    public void Clips13Fsrs3WeightsToModelBounds() =>
        Fsrs3Parameters.Clip(Enumerable.Repeat(double.PositiveInfinity, 13).ToArray())
            .Should().Equal(10, 5, 10, -0.1, -0.1, 0.5, 2, -0.15, 1.5, 5, -0.01, 0.9, 2);

    [Fact]
    public void ClipsLowerFsrs3WeightBounds() =>
        Fsrs3Parameters.Clip(Enumerable.Repeat(double.NegativeInfinity, 13).ToArray())
            .Should().Equal(0.1, 0.1, 1, -5, -5, 0.05, 0, -0.8, 0.01, 0.5, -2, 0.01, 0.01);

    [Fact]
    public void LeavesIncompleteWeightsForValidationAfterMigration()
    {
        var weights = Fsrs3Parameters.Migrate([1, 2, 3]);

        weights.Should().Equal(1, 2, 3);
        var act = () => Fsrs3Parameters.Check(weights);
        act.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS3 weights within model bounds.");
    }

    [Fact]
    public void MigratesWithoutClipping()
    {
        var weights = Fsrs3Constants.DefaultWeights.ToArray();
        weights[0] = 0;

        Fsrs3Parameters.Migrate(weights).Should().Equal(weights);
    }
}
