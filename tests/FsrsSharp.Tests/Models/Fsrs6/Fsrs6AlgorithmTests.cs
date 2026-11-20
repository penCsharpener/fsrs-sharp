using AwesomeAssertions;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs6;
using Xunit;

namespace FsrsSharp.Tests.Models.Fsrs6;

/// <summary>Port of <c>models/fsrs-6/algorithm.spec.ts</c>.</summary>
public class Fsrs6AlgorithmTests
{
    [Fact]
    public void RejectsAnInvalidFsrs6WeightCount()
    {
        var act = () => new Fsrs6Algorithm([], true, Fsrs6Constants.ModelBounds);
        act.Should().Throw<FsrsValidationException>().WithMessage("FSRS6Algorithm requires exactly 21 weights*");
    }

    [Fact]
    public void ClampsInitialStabilityToTheFsrs6ModelBounds()
    {
        var algorithm = new Fsrs6Algorithm(
            [double.NegativeInfinity, double.PositiveInfinity, .. Fsrs6Constants.DefaultWeights.Skip(2)],
            true,
            Fsrs6Constants.ModelBounds);

        algorithm.InitStability(Rating.Again).Should().Be(Fsrs6Constants.ModelBounds["sMin"]);
        algorithm.InitStability(Rating.Hard).Should().Be(Fsrs6Constants.ModelBounds["sMax"]);
    }

    [Fact]
    public void KeepsTheMemoryStateForAManualRating()
    {
        var algorithm = new Fsrs6Algorithm([.. Fsrs6Constants.DefaultWeights], true, Fsrs6Constants.ModelBounds);
        var memoryState = new FsrsState(10, 5);

        algorithm.NextState(memoryState, 1, Rating.Manual).Should().Be(memoryState);
    }
}
