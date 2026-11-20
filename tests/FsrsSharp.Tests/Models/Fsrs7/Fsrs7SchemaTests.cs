using Xunit;
using AwesomeAssertions;
using FsrsSharp.Kit;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs7;

namespace FsrsSharp.Tests.Models.Fsrs7;

/// <summary>Port of ts-fsrs <c>models/fsrs-7/schema.spec.ts</c>.</summary>
public class Fsrs7SchemaTests
{
    private static IReadOnlyList<double> Defaults => Fsrs7Constants.DefaultWeights;

    private static FieldBag Raw(object? weights) =>
        FieldBag.From([new KeyValuePair<string, object?>("weights", weights)]);

    [Fact]
    public void DecaySchemaAcceptsFiniteNumbersWithinInclusiveBoundsWithoutCoercion()
    {
        foreach (var value in new[] { 0.01, 0.5, 0.95 })
            Fsrs7Schema.Decay.Validate(value).Should().BeNull();
        foreach (var value in new[] { 0.01 - 2.220446049250313e-16, 0.95 + 2.220446049250313e-16, -0.5, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Fsrs7Schema.Decay.Validate(value).Should().Be("Expected finite decay in the range [0.01, 0.95]");
        // Non-numbers (upstream: '0.5', null, undefined) are rejected by the typed key.
        Fsrs7Schema.Decay.Validate("0.5").Should().NotBeNull();
        Fsrs7Schema.Decay.Validate(null).Should().NotBeNull();
    }

    [Fact]
    public void ConfigSchemaAcceptsThe34DefaultWeights()
    {
        var config = Fsrs7Schema.ParseConfig(Raw(Defaults));
        config.Weights.Should().Equal(Defaults);
        Fsrs7Model.Instance.ParseConfig(Raw(Defaults)).Get(Fsrs7Model.Weights).Should().Equal(Defaults);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(19)]
    [InlineData(21)]
    [InlineData(33)]
    [InlineData(35)]
    public void RejectsWrongWeightCountsAtTheSchemaBoundary(int length)
    {
        var config = Raw(Enumerable.Repeat(1.0, length).ToArray());
        var schema = () => Fsrs7Schema.ParseConfig(config);
        schema.Should().Throw<FsrsValidationException>().WithMessage("*exactly 34 finite weights*");
        // ParseConfig is what the scheduler runs at its boundary.
        var model = () => Fsrs7Model.Instance.ParseConfig(config);
        model.Should().Throw<FsrsValidationException>().WithMessage("*exactly 34 finite weights*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(19)]
    [InlineData(21)]
    [InlineData(33)]
    [InlineData(35)]
    public void RejectsWrongWeightCountsAtTheSchedulerBoundary(int length)
    {
        var config = new FieldBag.Builder { { Fsrs7Model.Weights, Enumerable.Repeat(1.0, length).ToArray() } };
        var create = () => SchedulerDefinition.Define(Fsrs7Model.Instance, FsrsSharp.Chrono.DateTimeChrono.Instance).Create(config);
        create.Should().Throw<FsrsValidationException>().WithMessage("*exactly 34 finite weights*");
    }

    public static TheoryData<object?> BadWeights =>
    [
        double.NaN, double.PositiveInfinity, double.NegativeInfinity, "1", null!,
    ];

    [Theory]
    [MemberData(nameof(BadWeights))]
    public void RejectsANonFiniteOrNonNumberWeight(object? value)
    {
        var weights = new List<object?> { value };
        weights.AddRange(Defaults.Skip(1).Cast<object?>());
        var act = () => Fsrs7Schema.ParseConfig(Raw(weights));
        act.Should().Throw<FsrsValidationException>().WithMessage("*34 finite weights*");
    }

    [Fact]
    public void RejectsAnInvalidConfigShape()
    {
        var shapes = new FieldBag?[] { null, FieldBag.Empty, Raw("invalid") };
        foreach (var config in shapes)
        {
            var act = () => Fsrs7Schema.ParseConfig(config);
            act.Should().Throw<FsrsValidationException>().WithMessage("*FSRS7 config*");
        }
    }

    [Fact]
    public void MemoryStateSchemaRejectsNonFiniteFields()
    {
        // Upstream also rejects a missing stabilityFast; the record makes that unrepresentable.
        Fsrs7Model.Instance.ValidateMemoryState(new Fsrs7State(1, double.NaN, 5)).Should().NotBeNull();
        Fsrs7Model.Instance.ValidateMemoryState(new Fsrs7State(1, 0.8, 5)).Should().BeNull();
    }
}
