using AwesomeAssertions;
using Xunit;

namespace FsrsSharp.Tests;

/// <summary>Port of the portable half of <c>alea.test.ts</c> — the known-value vectors. This is the file the
/// port's own README flags as its weakest spot ("the alea PRNG ... untested"), and it matters for exactly one
/// reason: <see cref="FsrsAlgorithm.ApplyFuzz"/> feeds a seed string into this generator, so if the port
/// diverges by a single bit then a fuzzed interval diverges from ts-fsrs's too.
///
/// Upstream also exercises <c>state()</c>, <c>importState()</c>, <c>int32()</c> and <c>double()</c> (fract53);
/// none of those are ported — the library only ever needs the plain <c>alea()</c> call — so there is nothing
/// here to test them against. Upstream seeds with numbers (<c>alea(12345)</c>), which its Mash hashes as
/// <c>String(seed)</c>; the C# constructor takes that string directly.</summary>
public class AleaTests
{
    private static double[] Take(string seed, int count)
    {
        var generator = new Alea(seed);
        return Enumerable.Range(0, count).Select(_ => generator.Next()).ToArray();
    }

    [Fact]
    public void Alea_ShouldProduceUpstreamsKnownSequence_ForSeed12345()
    {
        Take("12345", 3).Should().Equal(
            0.27138191112317145, 0.19615925149992108, 0.6810678059700876);
    }

    [Theory]
    // Upstream picked these three seeds precisely because each one drives a different branch of the Mash
    // constructor negative (s0, s1 and s2 respectively) and so exercises the "+= 1" correction.
    [InlineData("1727015666066", 0.6320083506871015)]
    [InlineData("Seedp5fxh9kf4r0", 0.14867847645655274)]
    [InlineData("NegativeS2Seed", 0.830770346801728)]
    public void Alea_ShouldProduceUpstreamsFirstValue_ForSeedsThatDriveTheConstructorNegative(
        string seed, double expected)
    {
        Take(seed, 1).Single().Should().Be(expected);
    }

    [Fact]
    public void Alea_ShouldBeDeterministicPerSeed()
    {
        Take("1", 5).Should().Equal(Take("1", 5));
        Take("1", 5).Should().NotEqual(Take("3", 5));
    }

    [Fact]
    public void Alea_ShouldStayInTheUnitInterval()
    {
        var generator = new Alea("statetest");

        for (var i = 0; i < 10_000; i++)
        {
            var value = generator.Next();
            value.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(1);
        }
    }
}
