using AwesomeAssertions;
using FsrsSharp.Models.Fsrs4;
using Xunit;

namespace FsrsSharp.Tests.Models.Fsrs4;

/// <summary>
/// Port of <c>models/fsrs-4/parameters.spec.ts</c>. The "clips holes in sparse weights as zero" case
/// (a JavaScript <c>new Array(3)</c>) has no C# counterpart: a <c>double</c> list cannot contain holes.
/// </summary>
public class Fsrs4ParametersTests
{
    private static readonly IReadOnlyList<double> Weights = Fsrs4Constants.DefaultWeights;

    [Fact]
    public void UsesTheFsrs4ParameterBoundsFromTheReferenceImplementation()
    {
        Fsrs4Parameters.Clip(Enumerable.Repeat(double.PositiveInfinity, 17).ToArray())
            .Should().Equal(36500, 36500, 36500, 36500, 10, 5, 5, 0.5, 3, 0.8, 2.5, 5, 0.2, 0.9, 2, 1, 4);
        Fsrs4Parameters.Clip(Enumerable.Repeat(double.NegativeInfinity, 17).ToArray())
            .Should().Equal(0.01, 0.01, 0.01, 0.01, 1, 0.1, 0.1, 0, 0, 0.1, 0.01, 0.5, 0.01, 0.01, 0.01, 0, 1);
    }

    [Fact]
    public void MigratesFsrs4WeightsWithoutFillingMissingParameters()
    {
        Fsrs4Parameters.Migrate(null).Should().Equal(Weights);
        Fsrs4Parameters.Migrate([.. Weights]).Should().Equal(Weights);
        Fsrs4Parameters.Migrate([1, 2, 3]).Should().Equal(1, 2, 3);
        Fsrs4Parameters.Migrate([.. Weights, 1, 1]).Should().Equal(Weights);

        double[] outOfBounds = [0, .. Weights.Skip(1)];
        Fsrs4Parameters.Migrate(outOfBounds).Should().Equal(outOfBounds);
    }

    [Fact]
    public void ChecksParameterBoundsAfterMigration()
    {
        var short3 = () => Fsrs4Parameters.Check([1, 2, 3]);
        short3.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS4 weights within model bounds.");
        var zero = () => Fsrs4Parameters.Check([0, .. Weights.Skip(1)]);
        zero.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS4 weights within model bounds.");
    }
}
