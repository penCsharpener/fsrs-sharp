using AwesomeAssertions;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using Xunit;
using static FsrsSharp.Tests.Middlewares.FuzzMonotonicTestSchedulers;

namespace FsrsSharp.Tests.Middlewares.MonotonicInterval;

/// <summary>Port of <c>middlewares/monotonic-interval/schema.spec.ts</c> (config parsing goes through scheduler creation).</summary>
public class MonotonicIntervalSchemaTests
{
    private static Scheduler<FsrsSharp.Models.FsrsState, DateTime> Create(Action<FieldBag.Builder>? configure = null)
    {
        var config = Fsrs6Config();
        configure?.Invoke(config);
        return Fsrs6Definition(MonotonicIntervalMiddleware.Instance).Create(config);
    }

    [Fact]
    public void ParsesMaximumInterval() =>
        Create(b => b.SetRaw("maximumInterval", 365d)).Config.Get(MonotonicIntervalMiddleware.MaximumInterval).Should().Be(365);

    [Fact]
    public void DefaultsMaximumIntervalWhenOmitted() =>
        Create().Config.Get(MonotonicIntervalMiddleware.MaximumInterval).Should().Be(36_500);

    [Fact]
    public void RejectsANullConfig()
    {
        var act = () => Fsrs6Definition(MonotonicIntervalMiddleware.Instance).Create((FieldBag)null!);
        act.Should().Throw<FsrsValidationException>();
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidConfig(double maximumInterval)
    {
        var act = () => Create(b => b.SetRaw("maximumInterval", maximumInterval));
        act.Should().Throw<FsrsValidationException>().WithMessage("Expected positive integer maximumInterval");
    }

    [Fact]
    public void RejectsANullMaximumInterval()
    {
        var act = () => Create(b => b.SetRaw("maximumInterval", null));
        act.Should().Throw<FsrsValidationException>();
    }
}
