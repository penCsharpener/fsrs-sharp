using AwesomeAssertions;
using FsrsSharp.Kit;
using FsrsSharp.Models.Fsrs5;
using FsrsSharp.Models.Fsrs6;
using Xunit;

namespace FsrsSharp.Tests.Models.Fsrs6;

/// <summary>Port of <c>models/fsrs-6/model.spec.ts</c>.</summary>
public class Fsrs6ModelTests
{
    private static Fsrs6Config Config(IReadOnlyList<double> weights, bool enableShortTerm = true) => new(weights, enableShortTerm, 0);

    [Fact]
    public void ExposesTheUnderlyingAlgorithmInstance() =>
        Fsrs6Model.Instance.Create(Config(Fsrs6Constants.DefaultWeights)).Algorithm.Should().BeOfType<Fsrs6Algorithm>();

    [Fact]
    public void ValidatesConfigWithSchemaBeforeCreatingModelRuntime()
    {
        var bag = new FieldBag.Builder().Set(Fsrs6Model.Weights, Fsrs6Constants.DefaultWeights).Set(Fsrs6Model.EnableShortTerm, true);
        bag.SetRaw("numRelearningSteps", "1");
        var act = () => Fsrs6Model.Instance.Create(bag.ToFieldBag());
        act.Should().Throw<FsrsValidationException>();
    }

    [Theory]
    [InlineData(true, 0.01)]
    [InlineData(false, 0.0)]
    public void ClipsMigratedFsrs5WeightsWhenEnableShortTermIs(bool enableShortTerm, double expectedW19)
    {
        var model = Fsrs6Model.Instance.Create(Config([.. Fsrs5Constants.DefaultWeights], enableShortTerm));

        ((Fsrs6Config)model.Config).Weights[19].Should().Be(expectedW19);
    }

    [Fact]
    public void BypassesParameterClippingWhenRequested()
    {
        var config = Config(Fsrs6Parameters.Migrate(Fsrs5Constants.DefaultWeights));

        var model = Fsrs6Model.Instance.Create(config, new ModelCreateOptions(Bypass: true));

        model.Config.Should().BeSameAs(config);
        ((Fsrs6Config)model.Config).Weights[19].Should().Be(0);
    }

    [Fact]
    public void CanMigrateWithoutClippingOrChecking()
    {
        var model = Fsrs6Model.Instance.Create(Config(Fsrs5Constants.DefaultWeights), new ModelCreateOptions(Clip: false, Check: false));

        var weights = ((Fsrs6Config)model.Config).Weights;
        weights.Should().HaveCount(21);
        weights[19].Should().Be(0);
    }

    [Fact]
    public void ChecksParameterBoundsWhenRequested()
    {
        var weights = Fsrs6Constants.DefaultWeights.ToArray();
        weights[0] = 0;

        var act = () => Fsrs6Model.Instance.Create(Config(weights), new ModelCreateOptions(Migrate: false, Clip: false, Check: true));

        act.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS6 weights within model bounds.");
    }

    [Fact]
    public void ParseConfigValidatesAndRoundTripsThroughBypassCreate()
    {
        var bag = Fsrs6Model.Instance.ParseConfig(new FieldBag.Builder
        {
            { Fsrs6Model.Weights, Fsrs6Constants.DefaultWeights },
            { Fsrs6Model.EnableShortTerm, true },
            { Fsrs6Model.NumRelearningSteps, 2 },
        }.ToFieldBag());

        var model = Fsrs6Model.Instance.Create(bag, new ModelCreateOptions(Bypass: true));

        ((Fsrs6Config)model.Config).NumRelearningSteps.Should().Be(2);
        var empty = () => Fsrs6Model.Instance.ParseConfig(FieldBag.Empty);
        empty.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS6 config");
    }

    [Fact]
    public void ExposesNameAndConfigKeys()
    {
        Fsrs6Model.Instance.Name.Should().Be("fsrs-6");
        Fsrs6Model.Instance.ConfigKeys.Select(k => k.Name).Should().Equal("weights", "enableShortTerm", "numRelearningSteps");
    }
}
