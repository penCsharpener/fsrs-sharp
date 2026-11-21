using AwesomeAssertions;
using FsrsSharp.Legacy;
using Xunit;

namespace FsrsSharp.Tests.Legacy;

/// <summary>Port of <c>constant.test.ts</c>.</summary>
public class ConstantsTests
{
    [Fact]
    public void Defaults_ShouldMatchUpstream()
    {
        Constants.DefaultRequestRetention.Should().Be(0.9);
        Constants.DefaultMaximumInterval.Should().Be(36500);
        Constants.DefaultEnableFuzz.Should().BeFalse();
        Constants.DefaultEnableShortTerm.Should().BeTrue();
        Constants.DefaultW.Should().HaveCount(TestParameters.W.Length);
        Constants.DefaultW.Should().Equal(TestParameters.W);
        Constants.Fsrs5DefaultDecay.Should().Be(0.5);
        Constants.Fsrs6DefaultDecay.Should().Be(0.1542);
        Constants.SMin.Should().Be(0.001);
        Constants.SMax.Should().Be(36500);
        Constants.InitSMax.Should().Be(100);
    }

    [Fact]
    public void GeneratorParameters_ShouldUseTheDefaults_WhenNothingIsOverridden()
    {
        var parameters = Defaults.GeneratorParameters();

        parameters.RequestRetention.Should().Be(Constants.DefaultRequestRetention);
        parameters.MaximumInterval.Should().Be(Constants.DefaultMaximumInterval);
        parameters.EnableFuzz.Should().Be(Constants.DefaultEnableFuzz);
        parameters.W.Should().Equal(TestParameters.W);
        parameters.LearningSteps.Should().Equal("1m", "10m");
        parameters.RelearningSteps.Should().Equal("10m");
    }

    [Fact]
    public void ShouldClampWeightsToTheMinimum_OnlyWhenTheSchedulerIsBuilt()
    {
        var w = new double[21];
        var minimums = Constants.ClampParameters(Constants.W17W18Ceiling).Select(range => range.Min);

        Defaults.GeneratorParameters(new FsrsParametersInput { W = w }).W.Should().Equal(w);
        new Fsrs(new FsrsParametersInput { W = w }).Parameters.W.Should().Equal(minimums);
    }

    [Fact]
    public void ShouldClampWeightsToTheMaximum_OnlyWhenTheSchedulerIsBuilt()
    {
        var w = Enumerable.Repeat(double.MaxValue, 21).ToArray();
        var maximums = Constants.ClampParameters(Constants.W17W18Ceiling).Select(range => range.Max);

        Defaults.GeneratorParameters(new FsrsParametersInput { W = w }).W.Should().Equal(w);
        new Fsrs(new FsrsParametersInput { W = w }).Parameters.W.Should().Equal(maximums);
    }

    [Fact]
    public void DefaultW_ShouldNotBeReachableThroughGeneratedParameters()
    {
        // Upstream freezes `default_w` so `default_w[4] = 0.5` throws. Here DefaultW is a read-only list, and
        // mutating what you were handed must not corrupt the defaults for everyone else.
        var parameters = Defaults.GeneratorParameters();

        parameters.W[4] = 0.5;

        Constants.DefaultW[4].Should().Be(TestParameters.W[4]);
        Defaults.GeneratorParameters().W[4].Should().Be(TestParameters.W[4]);
    }

    [Fact]
    public void ClampParameters_ShouldReturnAFreshArrayEachCall()
    {
        // Same reasoning as above, for upstream's "CLAMP_PARAMETERS can not be overwritten".
        var first = Constants.ClampParameters(Constants.Fsrs6DefaultDecay);
        first[4] = (0.5, 0.5);

        Constants.ClampParameters(Constants.Fsrs6DefaultDecay)[4].Should().NotBe((0.5, 0.5));
    }

    [Fact]
    public void ClampParameters_ShouldFloorW19_OnlyWhenShortTermIsEnabled()
    {
        Constants.ClampParameters(Constants.W17W18Ceiling, enableShortTerm: true)[19].Min.Should().Be(0.01);
        Constants.ClampParameters(Constants.W17W18Ceiling, enableShortTerm: false)[19].Min.Should().Be(0.0);
    }
}
