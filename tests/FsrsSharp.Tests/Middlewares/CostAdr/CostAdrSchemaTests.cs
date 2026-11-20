using AwesomeAssertions;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using Xunit;

namespace FsrsSharp.Tests.Middlewares.CostAdr;

/// <summary>Port of <c>middlewares/cost-adr/schema.spec.ts</c>.</summary>
public class CostAdrSchemaTests
{
    private static readonly CostAdrPolicy Policy = CostAdrConstants.DefaultPolicy;

    private static FieldBag Config(CostAdrPolicy? policy, double goalCostWeight) =>
        new FieldBag.Builder()
            .Set(CostAdrMiddleware.Policy, policy!)
            .Set(CostAdrMiddleware.GoalCostWeight, goalCostWeight)
            .ToFieldBag();

    [Theory]
    [InlineData(-64d)]
    [InlineData(64d)]
    public void AcceptsCoefficientBoundary(double coefficient)
    {
        var coefficients = Enumerable.Repeat(coefficient, 15).ToArray();

        CostAdrSchema.ParsePolicy(Policy with { Coefficients = coefficients }).Coefficients.Should().Equal(coefficients);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(0.5)]
    [InlineData(1024d)]
    public void AcceptsGoalCostWeight(double goalCostWeight) =>
        CostAdrSchema.ParseConfig(Config(Policy, goalCostWeight)).GoalCostWeight.Should().Be(goalCostWeight);

    [Fact]
    public void ValidatesAndCopiesPolicyDataWithoutRetainingCallerOwnedArrays()
    {
        var input = new CostAdrPolicy(Policy.Coefficients.ToArray(), 0, 1024, 0.3, 0.995, new CostAdrBounds(0.0001, 36500, 1, 10));

        var parsed = CostAdrSchema.ParseConfig(Config(input, CostAdrConstants.DefaultGoalCostWeight));

        parsed.Policy.Should().BeEquivalentTo(input);
        parsed.GoalCostWeight.Should().Be(CostAdrConstants.DefaultGoalCostWeight);
        parsed.Policy.Coefficients.Should().NotBeSameAs(input.Coefficients);
        parsed.Policy.Bounds.Should().NotBeSameAs(input.Bounds);
    }

    public static TheoryData<string, CostAdrPolicy?> InvalidPolicies()
    {
        var p = Policy;
        double[] WithFirst(double value) => [value, .. p.Coefficients.Skip(1)];
        var data = new TheoryData<string, CostAdrPolicy?>
        {
            { "null", null },
            { "coefficients null", p with { Coefficients = null! } },
            { "coefficients [1]", p with { Coefficients = [1] } },
            { "coefficients NaN", p with { Coefficients = Enumerable.Repeat(double.NaN, 15).ToArray() } },
            { "coefficient -64.01", p with { Coefficients = WithFirst(-64.01) } },
            { "coefficient 64.01", p with { Coefficients = WithFirst(64.01) } },
            { "costWeightMin NaN", p with { CostWeightMin = double.NaN } },
            { "costWeightMax Infinity", p with { CostWeightMax = double.PositiveInfinity } },
            { "costWeightMin -1", p with { CostWeightMin = -1 } },
            { "costWeightMax 0", p with { CostWeightMax = 0 } },
            { "costWeight log1p equal", p with { CostWeightMin = 1e16, CostWeightMax = 1e16 + 2 } },
            { "retentionMin NaN", p with { RetentionMin = double.NaN } },
            { "retentionMax Infinity", p with { RetentionMax = double.PositiveInfinity } },
            { "retentionMin 0", p with { RetentionMin = 0 } },
            { "retentionMax 0.3", p with { RetentionMax = 0.3 } },
            { "retentionMax 1", p with { RetentionMax = 1 } },
            { "bounds null", p with { Bounds = null! } },
            { "sMin Infinity", p with { Bounds = p.Bounds with { SMin = double.PositiveInfinity } } },
            { "sMax Infinity", p with { Bounds = p.Bounds with { SMax = double.PositiveInfinity } } },
            { "dMin Infinity", p with { Bounds = p.Bounds with { DMin = double.PositiveInfinity } } },
            { "dMax Infinity", p with { Bounds = p.Bounds with { DMax = double.PositiveInfinity } } },
            { "sMin 0", p with { Bounds = p.Bounds with { SMin = 0 } } },
            { "sMax 0.0001", p with { Bounds = p.Bounds with { SMax = 0.0001 } } },
            { "log(sMax) equal", p with { Bounds = p.Bounds with { SMin = 1e-307, SMax = 1.0000000000000001e-307 } } },
            { "dMax 1", p with { Bounds = p.Bounds with { DMax = 1 } } },
            { "dMax - dMin overflow", p with { Bounds = p.Bounds with { DMin = -double.MaxValue, DMax = double.MaxValue } } },
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidPolicies))]
    public void RejectsInvalidPolicy(string name, CostAdrPolicy? input)
    {
        _ = name;

        var act = () => CostAdrSchema.ParsePolicy(input);

        act.Should().Throw<FsrsValidationException>().WithMessage("*Cost ADR*");
        CostAdrMiddleware.Policy.Validate(input).Should().Contain("Cost ADR");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    [InlineData(1024.01)]
    public void RejectsInvalidGoalCostWeight(double goalCostWeight)
    {
        var act = () => CostAdrSchema.ParseConfig(Config(Policy, goalCostWeight));

        act.Should().Throw<FsrsValidationException>().WithMessage("Expected finite goalCostWeight in [0, 1024]");
        CostAdrMiddleware.GoalCostWeight.Validate(goalCostWeight).Should().NotBeNull();
    }

    [Fact]
    public void RejectsAnInvalidPolicyInTheConfig()
    {
        var empty = new CostAdrPolicy([], 0, 0, 0, 0, new CostAdrBounds(0, 0, 0, 0));

        var act = () => CostAdrSchema.ParseConfig(Config(empty, 64));

        act.Should().Throw<FsrsValidationException>();
    }
}
