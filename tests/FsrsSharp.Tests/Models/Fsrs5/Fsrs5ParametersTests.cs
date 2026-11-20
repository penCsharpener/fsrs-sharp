using AwesomeAssertions;
using FsrsSharp.Models.Fsrs5;
using Xunit;

namespace FsrsSharp.Tests.Models.Fsrs5;

/// <summary>Port of <c>models/fsrs-5/parameters.spec.ts</c>.</summary>
public class Fsrs5ParametersTests
{
    [Fact]
    public void ReturnsFsrs5DefaultWeightsWhenInputIsMissing() =>
        Fsrs5Parameters.Migrate(null).Should().Equal(Fsrs5Constants.DefaultWeights);

    [Fact]
    public void Clips19Fsrs5WeightsToModelBounds()
    {
        var weights = Enumerable.Repeat(double.PositiveInfinity, 19).ToArray();

        Fsrs5Parameters.Clip(weights).Should().Equal(
            100, 100, 100, 100, Fsrs5Constants.ModelBounds["dMax"], 4, 4, 0.75, 4.5, 0.8, 3.5, 5, 0.25, 0.9, 4, 1, 6,
            Fsrs5Constants.W17W18Ceiling, Fsrs5Constants.W17W18Ceiling);
    }

    [Fact]
    public void Migrates17WeightsTo19()
    {
        double[] params17 =
        [
            0.4072, 1.1829, 3.1262, 15.4722, 7.2102, 0.5316, 1.0651, 0.0234, 1.616,
            0.1544, 1.0824, 1.9813, 0.0953, 0.2975, 2.2042, 0.2407, 2.9466,
        ];

        Fsrs5Parameters.Migrate(params17).Should().Equal(
            0.4072, 1.1829, 3.1262, 15.4722, 8.2734, 0.31783648, 1.5651, 0.0234,
            1.616, 0.1544, 1.0824, 1.9813, 0.0953, 0.2975, 2.2042, 0.2407, 2.9466, 0, 0);
    }

    [Fact]
    public void TruncatesParametersLongerThanFsrs5Weights()
    {
        double[] parameters = [.. Fsrs5Constants.DefaultWeights, 0.0658, 0.1542];
        Fsrs5Parameters.Migrate(parameters).Should().Equal(Fsrs5Constants.DefaultWeights);
    }

    [Fact]
    public void TruncatesParametersWithoutClipping()
    {
        double[] parameters = [double.PositiveInfinity, .. Fsrs5Constants.DefaultWeights.Skip(1), double.NegativeInfinity, 0.1542];

        var migrated = Fsrs5Parameters.Migrate(parameters);

        migrated.Should().Equal([double.PositiveInfinity, .. Fsrs5Constants.DefaultWeights.Skip(1)]);
        Fsrs5Parameters.Clip(migrated)[0].Should().Be(100);
    }

    [Fact]
    public void RejectsParameterLengthsOutsideFsrs45AndFsrs5()
    {
        var three = () => Fsrs5Parameters.Migrate([1, 2, 3]);
        three.Should().Throw<FsrsValidationException>().WithMessage("Invalid parameters length \"3\", expected 17 or 19.");
        var eighteen = () => Fsrs5Parameters.Migrate(Enumerable.Repeat(1d, 18).ToArray());
        eighteen.Should().Throw<FsrsValidationException>().WithMessage("Invalid parameters length \"18\", expected 17 or 19.");
    }
}
