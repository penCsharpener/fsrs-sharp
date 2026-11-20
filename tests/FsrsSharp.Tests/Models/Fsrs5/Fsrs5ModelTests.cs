using AwesomeAssertions;
using FsrsSharp.Kit;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs5;
using Xunit;

namespace FsrsSharp.Tests.Models.Fsrs5;

/// <summary>Port of <c>models/fsrs-5/model.spec.ts</c>.</summary>
public class Fsrs5ModelTests
{
    private static Fsrs5Config Config(IReadOnlyList<double>? weights = null) => new(weights ?? Fsrs5Constants.DefaultWeights, true);

    [Fact]
    public void ReturnsTheDefaultEmptyMemoryState()
    {
        var bag = new FieldBag.Builder { { Fsrs5Model.Weights, Fsrs5Constants.DefaultWeights }, { Fsrs5Model.EnableShortTerm, true } }.ToFieldBag();
        Fsrs5Model.Instance.DefaultMemoryState(bag).Should().Be(new FsrsState(0, 0));
    }

    [Fact]
    public void BindsStepForgettingCurveIntervalAndForwardToTheFsrs5Algorithm()
    {
        var model = Fsrs5Model.Instance.Create(Config());

        var initial = model.Step(new ModelStepInput<FsrsState>(null, Rating.Easy, 0));

        initial.Should().Be(new FsrsState(15.69105, 3.22450159));
        model.NextInterval(initial, 0.9).Should().Be(16);
        model.ForgettingCurve(initial, 16).Should().Be(0.89832125);

        var history = new List<ModelReview> { new(Rating.Again, 0), new(Rating.Good, 0), new(Rating.Good, 1) };
        var expected = model.Step(new ModelStepInput<FsrsState>(
            model.Step(new ModelStepInput<FsrsState>(
                model.Step(new ModelStepInput<FsrsState>(null, Rating.Again, 0)), Rating.Good, 0)),
            Rating.Good, 1));
        model.Forward(history)[^1].Should().Be(expected);
    }

    [Fact]
    public void ExposesTheUnderlyingAlgorithmInstance() =>
        Fsrs5Model.Instance.Create(Config()).Algorithm.Should().BeOfType<Fsrs5Algorithm>();

    [Fact]
    public void ValidatesConfigWithSchemaBeforeCreatingModelRuntime()
    {
        var bag = new FieldBag.Builder().Set(Fsrs5Model.Weights, Fsrs5Constants.DefaultWeights);
        bag.SetRaw("enableShortTerm", "yes");
        var act = () => Fsrs5Model.Instance.Create(bag.ToFieldBag());
        act.Should().Throw<FsrsValidationException>();
    }

    [Fact]
    public void MigratesFsrs45WeightsWhenRequested()
    {
        var weights = Fsrs5Constants.DefaultWeights.Take(17).ToArray();
        var model = Fsrs5Model.Instance.Create(new Fsrs5Config(weights, true), new ModelCreateOptions(Migrate: true));

        ((Fsrs5Config)model.Config).Weights.Should().Equal(Fsrs5Parameters.Migrate(weights));
    }

    [Fact]
    public void BypassesCreateTimeParsingWhenRequested()
    {
        var config = Config([.. Fsrs5Constants.DefaultWeights]);

        var model = Fsrs5Model.Instance.Create(config, new ModelCreateOptions(Bypass: true));

        model.Config.Should().BeSameAs(config);
    }

    [Fact]
    public void BypassesWithAParsedFieldBag()
    {
        var bag = Fsrs5Model.Instance.ParseConfig(
            new FieldBag.Builder { { Fsrs5Model.Weights, Fsrs5Constants.DefaultWeights }, { Fsrs5Model.EnableShortTerm, false } }.ToFieldBag());

        var model = Fsrs5Model.Instance.Create(bag, new ModelCreateOptions(Bypass: true));

        model.Config.Should().Be(new Fsrs5Config(Fsrs5Constants.DefaultWeights, false));
    }

    [Fact]
    public void SkipsParameterBoundsChecksWhenRequested()
    {
        var weights = Fsrs5Constants.DefaultWeights.ToArray();
        weights[0] = 0;

        var model = Fsrs5Model.Instance.Create(Config(weights), new ModelCreateOptions(Migrate: false, Clip: false, Check: false));

        ((Fsrs5Config)model.Config).Weights[0].Should().Be(0);
    }

    [Fact]
    public void ChecksParameterBoundsWhenRequested()
    {
        var weights = Fsrs5Constants.DefaultWeights.ToArray();
        weights[0] = 0;

        var act = () => Fsrs5Model.Instance.Create(Config(weights), new ModelCreateOptions(Migrate: false, Clip: false, Check: true));

        act.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS5 weights within model bounds.");
    }

    [Fact]
    public void ParseConfigRequiresBothFieldsAndDoesNotMigrateOrClip()
    {
        var empty = () => Fsrs5Model.Instance.ParseConfig(FieldBag.Empty);
        empty.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS5 config");

        var short17 = Fsrs5Constants.DefaultWeights.Take(17).ToArray();
        var parsed = Fsrs5Model.Instance.ParseConfig(
            new FieldBag.Builder { { Fsrs5Model.Weights, short17 }, { Fsrs5Model.EnableShortTerm, true } }.ToFieldBag());
        parsed.Get(Fsrs5Model.Weights).Should().Equal(short17);
    }

    [Fact]
    public void ExposesNameAndConfigKeys()
    {
        Fsrs5Model.Instance.Name.Should().Be("fsrs-5");
        Fsrs5Model.Instance.ConfigKeys.Select(k => k.Name).Should().Equal("weights", "enableShortTerm");
    }
}
