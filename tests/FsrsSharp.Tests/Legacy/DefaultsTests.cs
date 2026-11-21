using AwesomeAssertions;
using FsrsSharp.Legacy;
using FsrsSharp.Middlewares;
using FsrsSharp.Models.Fsrs6;
using Xunit;

namespace FsrsSharp.Tests.Legacy;

/// <summary>Port of <c>default.test.ts</c>: parameter migration (FSRS-4.5 and FSRS-5 weight sets upgraded to
/// FSRS-6), validation, and the w17/w18 ceiling that stops repeated lapses from *increasing* short-term
/// stability. This is the path any weight set imported from an existing Anki collection travels down.</summary>
public class DefaultsTests
{
    [Fact]
    public void GeneratorParameters_ShouldConvertFsrs45ToFsrs6()
    {
        var parameters = Defaults.GeneratorParameters(new FsrsParametersInput { W = TestParameters.W45 });

        parameters.W.Should().Equal(
            0.4, 0.6, 2.4, 5.8,
            6.81,       // w4 = w5*2 + w4 = 0.94*2 + 4.93
            0.44675014, // w5 = ln(w5*3 + 1)/3 = ln(3.82)/3
            1.36,       // w6 = w6 + 0.5
            0.01, 1.49, 0.14, 0.94, 2.18, 0.05, 0.34, 1.26, 0.29, 2.61,
            0.0, 0.0, 0.0, Constants.Fsrs5DefaultDecay);
    }

    [Fact]
    public void GeneratorParameters_ShouldConvertFsrs5ToFsrs6()
    {
        var parameters = Defaults.GeneratorParameters(new FsrsParametersInput { W = TestParameters.W5 });

        parameters.W.Should().Equal(TestParameters.W5.Concat([0.0, Constants.Fsrs5DefaultDecay]));
    }

    [Fact]
    public void GeneratorParameters_ShouldMigrateWithoutClipping_AndFsrsClipsOnConstruction()
    {
        var w = TestParameters.W5;

        // The 19 -> 21 migration appends w19 = 0, which is below the short-term floor of 0.01; only the
        // Fsrs constructor (and the live parameters setter) clip it.
        Defaults.GeneratorParameters(new FsrsParametersInput { W = w }).W[19].Should().Be(0);
        new Fsrs(new FsrsParametersInput { W = w, EnableShortTerm = true }).Parameters.W[19].Should().Be(0.01);
        new Fsrs(new FsrsParametersInput { W = w }).Parameters.W[19].Should().Be(0.01);
        new Fsrs(new FsrsParametersInput { W = w, EnableShortTerm = false }).Parameters.W[19].Should().Be(0);

        var f = new Fsrs(new FsrsParametersInput { EnableShortTerm = true });
        f.Parameters.W = w;
        f.Parameters.W[19].Should().Be(0.01);
    }

    [Fact]
    public void GeneratorParameters_ShouldRejectAnUnrecognisedWeightLength()
    {
        var generate = () => Defaults.GeneratorParameters(new FsrsParametersInput { W = [0.40255] });

        generate.Should().Throw<FsrsValidationException>().WithMessage("Invalid parameters length*");
    }

    [Fact]
    public void CheckParameters_ShouldAcceptEveryRecognisedLength()
    {
        var w = Constants.DefaultW.ToArray();

        Defaults.CheckParameters(w).Should().Equal(w);
        Defaults.CheckParameters(w.Take(19).ToArray()).Should().HaveCount(19);
        Defaults.CheckParameters(w.Take(17).ToArray()).Should().HaveCount(17);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(18)]
    [InlineData(20)]
    public void CheckParameters_ShouldRejectAnUnrecognisedLength(int length)
    {
        var w = Constants.DefaultW.Take(length).ToArray();

        var check = () => Defaults.CheckParameters(w);

        check.Should().Throw<FsrsValidationException>().WithMessage("Invalid parameter length*");
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    public void CheckParameters_ShouldRejectANonFiniteWeight(double value)
    {
        var w = Constants.DefaultW.ToArray();
        w[5] = value;

        var check = () => Defaults.CheckParameters(w);

        check.Should().Throw<FsrsValidationException>().WithMessage("Non-finite*");
    }

    [Fact]
    public void MigrateFsrs6Parameters_ShouldReturnTheDefaults_ForNull()
    {
        Fsrs6Parameters.Migrate(null).Should().Equal(Constants.DefaultW);
    }

    [Fact]
    public void MigrateFsrs6Parameters_ShouldThrow_ForAnUnrecognisedLength()
    {
        var migrate = () => Fsrs6Parameters.Migrate([1, 2, 3]);

        migrate.Should().Throw<FsrsValidationException>().WithMessage("Invalid parameters length*");
    }

    [Fact]
    public void MigrateFsrs6Parameters_ShouldPassThroughAnFsrs6Vector()
    {
        Fsrs6Parameters.Migrate(Constants.DefaultW).Should().Equal(Constants.DefaultW);
    }

    [Fact]
    public void MigrateFsrs6Parameters_ShouldBeIndependentOfShortTermClipping()
    {
        var w = Constants.DefaultW.ToArray();
        w[19] = 0; // only legal once the short-term floor of 0.01 is lifted

        Fsrs6Parameters.Migrate(w).Should().Equal(w);
    }

    [Fact]
    public void MigrateFsrs6Parameters_ShouldPassANonFiniteWeightThroughUnchanged()
    {
        var w = Defaults.GeneratorParameters().W;
        w[0] = double.NaN;

        Fsrs6Parameters.Migrate(w.ToArray()).Should().Equal(w);
    }

    [Fact]
    public void ClipFsrs6Parameters_ShouldTightenTheW17W18Ceiling_ForMoreThanOneRelearningStep()
    {
        var w = Constants.DefaultW.ToArray();
        w[17] = double.MaxValue;
        w[18] = double.MaxValue;

        var clipped = Fsrs6Parameters.Clip(w, 2);

        clipped[17].Should().Be(0.60045935);
        clipped[18].Should().Be(0.60045935);
        (clipped[17] * clipped[18]).Should().BeLessThanOrEqualTo(0.36056);
    }

    [Fact]
    public void ClipFsrs6Parameters_ShouldClampW11W13W14BeforeComputingTheCeiling()
    {
        // Raw zeros would put log() at -Infinity and leave the ceiling NaN, which silently leaves w17/w18
        // uncapped — the exact failure this clamp exists to prevent.
        var w = Constants.DefaultW.ToArray();
        w[11] = 0;
        w[13] = 0;
        w[14] = 0;
        w[17] = double.MaxValue;
        w[18] = double.MaxValue;

        var clipped = Fsrs6Parameters.Clip(w, 2);

        double.IsFinite(clipped[17]).Should().BeTrue();
        double.IsFinite(clipped[18]).Should().BeTrue();
        clipped[17].Should().Be(2.0);
        clipped[18].Should().Be(2.0);
    }

    [Fact]
    public void ClipFsrs6Parameters_ShouldFallBackToTheFloor_WhenTheCeilingComputationGoesNegative()
    {
        var w = Constants.DefaultW.ToArray();
        w[11] = 5.0;
        w[13] = 0.9;
        w[14] = 4.0;
        w[17] = double.MaxValue;
        w[18] = double.MaxValue;

        var clipped = Fsrs6Parameters.Clip(w, 2);

        // value ≈ -1.333, sqrt(max(value, 0)) = 0, clamped up to the 0.01 floor.
        clipped[17].Should().Be(0.01);
        clipped[18].Should().Be(0.01);
    }

    [Fact]
    public void ClipFsrs6Parameters_ShouldApplyTheSqrtCeiling_ForFourRelearningSteps()
    {
        var w = Constants.DefaultW.ToArray();
        w[11] = 0.1;
        w[13] = 0.1;
        w[14] = 0.5;
        w[17] = 2.0;
        w[18] = 2.0;

        var clipped = Fsrs6Parameters.Clip(w, 4);

        clipped[17].Should().BeApproximately(1.09394076, 1e-5);
        clipped[18].Should().BeApproximately(1.09394076, 1e-5);
        (clipped[17] * clipped[18]).Should().BeLessThanOrEqualTo(1.19671);
    }

    [Fact]
    public void ClipFsrs6Parameters_ShouldSkipTheCeilingUpdate_ForA17WeightVector()
    {
        // FSRS-4.5 has no w17/w18 at all, so the ceiling update must be a no-op rather than an index error.
        var clipped = Fsrs6Parameters.Clip(TestParameters.W45, 2);

        clipped.Should().HaveCount(17);
        clipped.Should().Equal(TestParameters.W45);
    }

    [Fact]
    public void ClipFsrs6Parameters_ShouldUseTheDefaultCeiling_ForASingleRelearningStep()
    {
        var w = Constants.DefaultW.Select((v, i) => i is 17 or 18 ? 2.5 : v).ToArray();

        var clipped = Fsrs6Parameters.Clip(w, 1);

        clipped[17].Should().Be(2.0);
        clipped[18].Should().Be(2.0);
    }

    [Theory]
    [InlineData(2023, 10, 3)]
    [InlineData(2024, 1, 1)]
    public void CreateEmptyCard_ShouldBeDueImmediatelyWithEverythingZeroed(int year, int month, int day)
    {
        var now = TestParameters.Utc(year, month, day);

        var card = Defaults.CreateEmptyCard(now);

        card.Due.Should().Be(now);
        card.Stability.Should().Be(0);
        card.Difficulty.Should().Be(0);
        card.ScheduledDays.Should().Be(0);
        card.LearningSteps.Should().Be(0);
        card.Reps.Should().Be(0);
        card.Lapses.Should().Be(0);
        card.State.Should().Be(State.New);
        card.LastReview.Should().BeNull();
    }
}
