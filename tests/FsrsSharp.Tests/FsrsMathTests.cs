using AwesomeAssertions;
using Xunit;

namespace FsrsSharp.Tests;

/// <summary>
/// Port of <c>help.spec.ts</c>. Upstream's disabled-rounding cases exercise a build-time flag
/// (<c>TS_FSRS_DISABLE_ROUNDING</c>) through the bundler; here the switch is the
/// <c>FSRS_SHARP_DISABLE_ROUNDING</c> environment variable read once per process, so only the
/// default (rounding enabled) behaviour is asserted in-process.
/// </summary>
public class FsrsMathTests
{
    [Fact]
    public void RoundsByDefault()
    {
        FsrsMath.RoundTo(1.23456789, 3).Should().Be(1.235);
        FsrsMath.RoundTo(1.23456789, 0).Should().Be(1);
    }

    [Fact]
    public void ClampsToTheGivenRange()
    {
        FsrsMath.Clamp(5, 1, 3).Should().Be(3);
        FsrsMath.Clamp(-5, 1, 3).Should().Be(1);
        FsrsMath.Clamp(2, 1, 3).Should().Be(2);
    }
}
