using AwesomeAssertions;
using Xunit;

namespace FsrsSharp.Tests;

/// <summary>Port of <c>constant.test.ts</c> (plus <c>version.test.ts</c>). Upstream's "can not be overwritten"
/// cases rely on JavaScript's <c>Object.freeze</c>, which C# has no equivalent of for a <c>double[]</c>; what
/// they actually protect against — one caller's parameter tweak leaking into everyone else's defaults — is
/// asserted here as defensive copying instead.</summary>
public class ConstantsTests
{
    [Fact]
    public void Defaults_ShouldMatchUpstream()
    {
        Constants.DefaultRequestRetention.Should().Be(0.9);
        Constants.DefaultMaximumInterval.Should().Be(36500);
        Constants.DefaultEnableFuzz.Should().BeFalse();
        Constants.DefaultEnableShortTerm.Should().BeTrue();
        Constants.DefaultW.Should().Equal(TestParameters.W);
        Constants.Fsrs5DefaultDecay.Should().Be(0.5);
        Constants.Fsrs6DefaultDecay.Should().Be(0.1542);
        Constants.SMin.Should().Be(0.001);
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
    public void GeneratorParameters_ShouldClampWeightsToTheMinimum()
    {
        var parameters = Defaults.GeneratorParameters(new FsrsParametersInput { W = new double[21] });

        var minimums = Constants.ClampParameters(Constants.W17W18Ceiling).Select(range => range.Min);
        parameters.W.Should().Equal(minimums);
    }

    [Fact]
    public void GeneratorParameters_ShouldClampWeightsToTheMaximum()
    {
        var w = Enumerable.Repeat(double.MaxValue, 21).ToArray();

        var parameters = Defaults.GeneratorParameters(new FsrsParametersInput { W = w });

        var maximums = Constants.ClampParameters(Constants.W17W18Ceiling).Select(range => range.Max);
        parameters.W.Should().Equal(maximums);
    }

    [Fact]
    public void DefaultW_ShouldNotBeReachableThroughGeneratedParameters()
    {
        // Upstream freezes `default_w` so `default_w[4] = 0.5` throws. C# can't freeze an array, so the
        // property that actually matters is asserted instead: mutating what you were handed must not
        // corrupt the defaults for everyone else.
        var parameters = Defaults.GeneratorParameters();
        parameters.W.Should().NotBeSameAs(Constants.DefaultW);

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

    [Fact]
    public void FsrsVersion_ShouldNameTheAlgorithmItImplements()
    {
        // Upstream's version.test.ts pins FSRSVersion against its own package.json; this port has no such
        // pairing, so what's checked is the part that would actually mislead a reader if it went stale.
        Constants.FsrsVersion.Should().Contain("FSRS-6.0");
    }
}
