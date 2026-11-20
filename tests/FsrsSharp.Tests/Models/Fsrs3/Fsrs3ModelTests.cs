using AwesomeAssertions;
using FsrsSharp.Kit;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs3;
using Xunit;

namespace FsrsSharp.Tests.Models.Fsrs3;

/// <summary>Port of <c>models/fsrs-3/model.spec.ts</c>.</summary>
public class Fsrs3ModelTests
{
    private static Fsrs3Model Model => Fsrs3Model.Instance;
    private static Fsrs3Config Config(IReadOnlyList<double>? weights = null) => new(weights ?? Fsrs3Constants.DefaultWeights);

    [Fact]
    public void ReturnsTheDefaultEmptyMemoryState() =>
        Model.DefaultMemoryState(FieldBag.Empty.With(Fsrs3Model.Weights, Fsrs3Constants.DefaultWeights)).Should().Be(new FsrsState(0, 0));

    [Fact]
    public void BindsStepForgettingCurveIntervalAndForwardToTheFsrs3Algorithm()
    {
        var model = Model.Create(Config());

        var initial = model.Step(new ModelStepInput<FsrsState>(null, Rating.Good, 0));

        initial.Should().Be(new FsrsState(4.4073, 4.8527));
        model.NextInterval(initial, 0.9).Should().Be(4);
        model.ForgettingCurve(initial, 1).Should().Be(0.97637757);

        model.Forward([new ModelReview(Rating.Good, 0), new ModelReview(Rating.Good, 1)]).Should().Equal(
            new FsrsState(4.4073, 4.8527),
            new FsrsState(7.4609786, 4.8527));
    }

    [Fact]
    public void ExposesTheUnderlyingAlgorithmInstance() =>
        Model.Create(Config()).Algorithm.Should().BeOfType<Fsrs3Algorithm>();

    [Fact]
    public void ValidatesConfigWithSchemaBeforeCreatingModelRuntime()
    {
        var invalid = new FieldBag.Builder();
        invalid.SetRaw("weights", "invalid");
        var act = () => Model.Create(invalid.ToFieldBag(), new ModelCreateOptions(Migrate: false, Check: false));
        act.Should().Throw<FsrsValidationException>();
    }

    [Fact]
    public void MigratesFsrs3WeightsWhenRequested()
    {
        var weights = Fsrs3Constants.DefaultWeights.ToArray();
        weights[0] = 0;

        var model = Model.Create(Config(weights), new ModelCreateOptions(Migrate: true, Check: false));

        ((Fsrs3Config)model.Config).Weights.Should().Equal(Fsrs3Parameters.Clip(Fsrs3Parameters.Migrate(weights)));
    }

    [Fact]
    public void BypassesCreateTimeParsingWhenRequested()
    {
        var config = Config([.. Fsrs3Constants.DefaultWeights]);

        Model.Create(config, new ModelCreateOptions(Bypass: true)).Config.Should().BeSameAs(config);
    }

    [Fact]
    public void SkipsParameterBoundsChecksWhenRequested()
    {
        var weights = Fsrs3Constants.DefaultWeights.ToArray();
        weights[0] = 0;

        var model = Model.Create(Config(weights), new ModelCreateOptions(Migrate: false, Clip: false, Check: false));

        ((Fsrs3Config)model.Config).Weights[0].Should().Be(0);
    }

    [Fact]
    public void ChecksParameterBoundsWhenRequested()
    {
        var weights = Fsrs3Constants.DefaultWeights.ToArray();
        weights[0] = 0;

        var act = () => Model.Create(Config(weights), new ModelCreateOptions(Migrate: false, Clip: false, Check: true));

        act.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS3 weights within model bounds.");
    }

    [Fact]
    public void ExposesNameConfigKeysAndParseConfig()
    {
        Model.Name.Should().Be("fsrs-3");
        Model.ConfigKeys.Select(k => k.Name).Should().Equal("weights");
        var empty = () => Model.ParseConfig(FieldBag.Empty);
        empty.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS3 config");
    }
}
