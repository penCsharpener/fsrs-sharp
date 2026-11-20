using AwesomeAssertions;
using FsrsSharp.Kit;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs4Dot5;
using Xunit;

namespace FsrsSharp.Tests.Models.Fsrs4Dot5;

/// <summary>Port of <c>models/fsrs-4dot5/model.spec.ts</c>.</summary>
public class Fsrs4Dot5ModelTests
{
    private static readonly IReadOnlyList<double> Weights = Fsrs4Dot5Constants.DefaultWeights;
    private static Fsrs4Dot5Model Model => Fsrs4Dot5Model.Instance;

    [Fact]
    public void CreatesModelRuntimeWithSchemaValidationAndCreateOptions()
    {
        var model = Model.Create(new Fsrs4Dot5Config(Weights));
        var bypassConfig = new Fsrs4Dot5Config([.. Weights]);

        Model.DefaultMemoryState(FieldBag.Empty.With(Fsrs4Dot5Model.Weights, Weights)).Should().Be(new FsrsState(0, 0));
        Model.Create(bypassConfig, new ModelCreateOptions(Bypass: true)).Config.Should().BeSameAs(bypassConfig);

        var invalid = new FieldBag.Builder();
        invalid.SetRaw("weights", "invalid");
        var act1 = () => Model.Create(invalid.ToFieldBag(), new ModelCreateOptions(Migrate: false, Check: false));
        act1.Should().Throw<FsrsValidationException>();

        var act2 = () => Model.Create(
            new Fsrs4Dot5Config([0, .. Weights.Skip(1)]), new ModelCreateOptions(Migrate: false, Clip: false, Check: true));
        act2.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS4.5 weights within model bounds.");

        ((Fsrs4Dot5Config)Model.Create(new Fsrs4Dot5Config([.. Weights, 1, 1])).Config).Weights.Should().Equal(Weights);

        var extra = new FieldBag.Builder().Set(Fsrs4Dot5Model.Weights, Weights);
        extra.SetRaw("enableShortTerm", true);
        ((Fsrs4Dot5Config)Model.Create(extra.ToFieldBag()).Config).Weights.Should().Equal(((Fsrs4Dot5Config)model.Config).Weights);
    }

    [Fact]
    public void ExposesTheUnderlyingAlgorithmInstance() =>
        Model.Create(new Fsrs4Dot5Config(Weights)).Algorithm.Should().BeOfType<Fsrs4Dot5Algorithm>();

    [Fact]
    public void ExposesTheFsrs45ModelRuntimeMethods()
    {
        var model = Model.Create(new Fsrs4Dot5Config(Weights));
        var initialState = model.Step(new ModelStepInput<FsrsState>(null, Rating.Easy, 0));
        var states = model.Forward([new ModelReview(Rating.Again, 0), new ModelReview(Rating.Good, 1)]);

        double.IsFinite(initialState.Stability).Should().BeTrue();
        double.IsFinite(initialState.Difficulty).Should().BeTrue();
        double.IsFinite(model.NextInterval(initialState, 0.9)).Should().BeTrue();
        double.IsFinite(model.ForgettingCurve(initialState, 14)).Should().BeTrue();
        states.Should().HaveCount(2);
    }

    [Fact]
    public void ExposesNameConfigKeysAndParseConfig()
    {
        Model.Name.Should().Be("fsrs-4dot5");
        Model.ConfigKeys.Select(k => k.Name).Should().Equal("weights");
        var empty = () => Model.ParseConfig(FieldBag.Empty);
        empty.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS4.5 config");
        Model.ParseConfig(FieldBag.Empty.With(Fsrs4Dot5Model.Weights, Weights)).Get(Fsrs4Dot5Model.Weights).Should().Equal(Weights);
    }
}
