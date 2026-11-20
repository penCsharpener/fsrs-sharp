using AwesomeAssertions;
using FsrsSharp.Models.Fsrs4Dot5;
using Xunit;

namespace FsrsSharp.Tests.Models.Fsrs4Dot5;

/// <summary>Port of <c>models/fsrs-4dot5/parameters.spec.ts</c>.</summary>
public class Fsrs4Dot5ParametersTests
{
    private static readonly IReadOnlyList<double> Weights = Fsrs4Dot5Constants.DefaultWeights;

    [Fact]
    public void UsesTheFsrs45ParameterBoundsFromTheReferenceImplementation() =>
        Fsrs4Dot5Parameters.Clip(Enumerable.Repeat(double.PositiveInfinity, 17).ToArray())
            .Should().Equal(100, 100, 100, 100, 10, 5, 5, 0.8, 6, 0.8, 5, 6, 0.4, 0.9, 4, 1, 10);

    [Fact]
    public void MigratesFsrs45WeightsWithoutRejectingLongerParameterArrays()
    {
        Fsrs4Dot5Parameters.Migrate(null).Should().Equal(Weights);
        Fsrs4Dot5Parameters.Migrate([1, 2, 3]).Should().Equal(1, 2, 3);
        Fsrs4Dot5Parameters.Migrate([.. Weights, 1, 1]).Should().Equal(Weights);

        double[] outOfBounds = [0, .. Weights.Skip(1)];
        Fsrs4Dot5Parameters.Migrate(outOfBounds).Should().Equal(outOfBounds);
    }

    [Fact]
    public void ChecksParameterBoundsAfterMigration()
    {
        var short3 = () => Fsrs4Dot5Parameters.Check([1, 2, 3]);
        short3.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS4.5 weights within model bounds.");
        var zero = () => Fsrs4Dot5Parameters.Check([0, .. Weights.Skip(1)]);
        zero.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS4.5 weights within model bounds.");
    }
}
