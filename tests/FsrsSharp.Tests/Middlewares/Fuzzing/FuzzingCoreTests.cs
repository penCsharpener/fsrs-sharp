using AwesomeAssertions;
using FsrsSharp.Middlewares;
using Xunit;

namespace FsrsSharp.Tests.Middlewares.Fuzzing;

/// <summary>Port of <c>middlewares/fuzzing/core.spec.ts</c>.</summary>
public class FuzzingCoreTests
{
    private static readonly FuzzingConfigInput Config = new(true, 36_500);

    private sealed class FixedTimeProvider(long unixMilliseconds) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
    }

    [Fact]
    public void RoundsWhenFuzzingIsDisabledOrTheIntervalIsTooShort()
    {
        FsrsSharp.Middlewares.Fuzzing.WithFuzzing(3.4, 1, Config with { EnableFuzz = false }, "seed").Should().Be(3);
        FsrsSharp.Middlewares.Fuzzing.WithFuzzing(2.4, 1, Config, "seed").Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HonorsFractionalDaysForShortOrDisabledFuzzIntervals(bool fractionalDays)
    {
        foreach (var enableFuzz in new[] { false, true })
        {
            foreach (var interval in new[] { 0, 1.0 / 86400, 10.0 / 1440, 1, 2.4, 2.5, 3.4 })
            {
                if (enableFuzz && interval >= 2.5) continue;
                FsrsSharp.Middlewares.Fuzzing
                    .WithFuzzing(interval, 0, Config with { EnableFuzz = enableFuzz, FractionalDays = fractionalDays }, "seed")
                    .Should().Be(fractionalDays ? interval : Math.Round(interval, MidpointRounding.AwayFromZero));
            }
        }
    }

    [Fact]
    public void HonorsAnUnsetFractionalDays()
    {
        // fractionalDays=undefined upstream: the default (false) rounds.
        FsrsSharp.Middlewares.Fuzzing.WithFuzzing(2.4, 0, new FuzzingConfigInput(true, 36_500), "seed").Should().Be(2);
        FsrsSharp.Middlewares.Fuzzing.WithFuzzing(1.0 / 86400, 0, new FuzzingConfigInput(false, 36_500), "seed").Should().Be(0);
    }

    [Fact]
    public void StillProducesTheSameIntegerFuzzResultAtAndAbove2Point5Days()
    {
        foreach (var interval in new[] { 2.5, 2.6, 10.25 })
        {
            var integer = FsrsSharp.Middlewares.Fuzzing.WithFuzzing(interval, 0, Config, "boundary-seed");
            var fractional = FsrsSharp.Middlewares.Fuzzing.WithFuzzing(interval, 0, Config with { FractionalDays = true }, "boundary-seed");
            fractional.Should().Be(integer);
            (fractional == Math.Floor(fractional)).Should().BeTrue();
        }
    }

    [Fact]
    public void FuzzesThe2Point5IntervalBoundaryInsideItsRange()
    {
        var range = FsrsSharp.Middlewares.Fuzzing.GetFuzzRange(2.5, 0, Config.MaximumInterval);
        var interval = FsrsSharp.Middlewares.Fuzzing.WithFuzzing(2.5, 0, Config, "boundary-seed");

        interval.Should().BeGreaterThanOrEqualTo(range.MinInterval);
        interval.Should().BeLessThanOrEqualTo(range.MaxInterval);
    }

    [Fact]
    public void AppliesDeterministicFuzzingInsideTheComputedRange()
    {
        var first = FsrsSharp.Middlewares.Fuzzing.WithFuzzing(10, 5, Config, "stable-seed");
        var second = FsrsSharp.Middlewares.Fuzzing.WithFuzzing(10, 5, Config, "stable-seed");
        var range = FsrsSharp.Middlewares.Fuzzing.GetFuzzRange(10, 5, Config.MaximumInterval);

        second.Should().Be(first);
        first.Should().BeGreaterThanOrEqualTo(range.MinInterval);
        first.Should().BeLessThanOrEqualTo(range.MaxInterval);
    }

    [Fact]
    public void UsesTheCurrentTimeWhenNoSeedIsSupplied()
    {
        var clock = new FixedTimeProvider(42);

        FsrsSharp.Middlewares.Fuzzing.WithFuzzing(10, 5, Config, null, clock)
            .Should().Be(FsrsSharp.Middlewares.Fuzzing.WithFuzzing(10, 5, Config, "42"));
    }

    [Fact]
    public void CreatesIntervalRangesFromCustomFuzzRanges()
    {
        var range = FsrsSharp.Middlewares.Fuzzing.GetFuzzRange(30, 9, 365, []);

        range.Should().Be(new FuzzInterval(29, 31));
    }

    [Fact]
    public void UsesFnv1aWithMulberry32AsTheDefaultSeededRng()
    {
        FsrsSharp.Middlewares.Fuzzing.Fnv1aMulberry32Rng("string-card-1")().Should().Be(0.643895001616329);
    }

    [Fact]
    public void CapsTheFuzzRangeAtMaximumInterval()
    {
        var range = FsrsSharp.Middlewares.Fuzzing.GetFuzzRange(737, 98, 365);
        var interval = FsrsSharp.Middlewares.Fuzzing.WithFuzzing(737, 98, new FuzzingConfigInput(true, 365), "seed");

        range.MaxInterval.Should().Be(365);
        interval.Should().BeGreaterThanOrEqualTo(range.MinInterval);
        interval.Should().BeLessThanOrEqualTo(range.MaxInterval);
    }

    [Fact]
    public void KeepsTheElapsedDaysLowerBoundOnlyWhenNeeded()
    {
        var range = FsrsSharp.Middlewares.Fuzzing.GetFuzzRange(5, 5, 100);

        range.MinInterval.Should().BeLessThanOrEqualTo(range.MaxInterval);
    }
}
