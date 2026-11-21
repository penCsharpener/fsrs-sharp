using System.Globalization;
using AwesomeAssertions;
using FsrsSharp.Legacy;
using Xunit;

namespace FsrsSharp.Tests.Legacy;

/// <summary>Port of <c>alea.test.ts</c>.</summary>
public class AleaTests
{
    private static double[] Take(string seed, int count)
    {
        var generator = new Alea(seed);
        return Enumerable.Range(0, count).Select(_ => generator.Next()).ToArray();
    }

    [Fact]
    public void Alea_ShouldGiveTheSameStateForTheSameSeed()
    {
        var a = new Alea("1").State;
        var b = new Alea("3").State;
        var c = new Alea("1").State;

        a.Should().Be(c);
        a.Should().NotBe(b);
    }

    [Fact]
    public void Alea_ShouldProduceUpstreamsKnownSequence_ForSeed12345()
    {
        Take("12345", 3).Should().Equal(
            0.27138191112317145, 0.19615925149992108, 0.6810678059700876);
    }

    [Fact]
    public void Int32_ShouldBeAnInt32()
    {
        var generator = new Alea("int32test");
        var value = generator.Int32();
        ((long)value).Should().BeInRange(int.MinValue, int.MaxValue);
    }

    [Fact]
    public void Int32_ShouldMatchUpstreamsKnownValues_ForSeed12345()
    {
        var generator = new Alea("12345");
        Enumerable.Range(0, 3).Select(_ => generator.Int32()).Should().Equal(1165576433, 842497570, -1369803343);
    }

    [Fact]
    public void Double_ShouldBeInTheUnitInterval()
    {
        var generator = new Alea("doubletest");
        generator.Double().Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(1);
    }

    [Fact]
    public void Double_ShouldMatchUpstreamsKnownValues_ForSeed12345()
    {
        var generator = new Alea("12345");
        Enumerable.Range(0, 3).Select(_ => generator.Double()).Should().Equal(
            0.27138191116884325, 0.6810678062004586, 0.3407802057882554);
    }

    [Fact]
    public void ImportState_ShouldReproduceTheGenerator()
    {
        var prng1 = new Alea(Random.Shared.NextDouble().ToString("R", CultureInfo.InvariantCulture));
        prng1.Next();
        prng1.Next();
        prng1.Next();

        var prng4 = new Alea(null) { State = prng1.State };
        prng4.State.Should().Be(prng1.State);
        for (var i = 0; i < 10_000; i++)
        {
            var a = prng1.Next();
            var b = prng4.Next();
            a.Should().Be(b);
            a.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(1);
        }
    }

    [Fact]
    public void State_ShouldAdvanceWithEveryDraw()
    {
        var generator = new Alea("statetest");
        var state1 = generator.State;
        var next1 = generator.Next();
        var state2 = generator.State;
        var next2 = generator.Next();

        state1.S0.Should().NotBe(state2.S0);
        state1.S1.Should().NotBe(state2.S1);
        state1.S2.Should().NotBe(state2.S2);
        next1.Should().NotBe(next2);
    }

    [Fact]
    public void Next_ShouldHandleANegativeS2()
    {
        var generator = new Alea("12345") { State = new AleaState(0, 0, 0, -0.5) };
        var result = generator.Next();

        result.Should().Be(0);
        generator.State.Should().Be(new AleaState(0, 0, -0.5, 0));
    }

    [Theory]
    // Upstream picked these three seeds precisely because each one drives a different branch of the Mash
    // constructor negative (s0, s1 and s2 respectively) and so exercises the "+= 1" correction.
    [InlineData("1727015666066", 0.6320083506871015, 1828249, 0.5888567129150033, 0.5074866858776659)]
    [InlineData("Seedp5fxh9kf4r0", 0.14867847645655274, 1776946, 0.6778371171094477, 0.0770602801349014)]
    [InlineData("NegativeS2Seed", 0.830770346801728, 952982, 0.25224833423271775, 0.9213257452938706)]
    public void Alea_ShouldProduceUpstreamsFirstValueAndState_ForSeedsThatDriveTheConstructorNegative(
        string seed, double expected, double c, double s0, double s1)
    {
        var generator = new Alea(seed);
        if (seed == "Seedp5fxh9kf4r0") generator.State.S1.Should().BeGreaterThan(0);

        generator.Next().Should().Be(expected);
        generator.State.Should().Be(new AleaState(c, s0, s1, expected));
    }

    [Fact]
    public void Alea_ShouldHandleASeedWhereS1IsZero()
    {
        var state = new Alea("seed-19140").State;
        state.S1.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(0.05);
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
