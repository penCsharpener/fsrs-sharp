using AwesomeAssertions;
using FsrsSharp.Kit;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs4;
using Xunit;
using static FsrsSharp.Tests.Models.ModelTestHelpers;

namespace FsrsSharp.Tests.Models.Fsrs4;

/// <summary>Port of <c>models/fsrs-4/model.spec.ts</c>.</summary>
public class Fsrs4ModelTests
{
    private static readonly IReadOnlyList<double> Weights = Fsrs4Constants.DefaultWeights;
    private static Fsrs4Model Model => Fsrs4Model.Instance;

    private static IModelCore<FsrsState> Create() => Model.Create(new Fsrs4Config(Weights));

    [Fact]
    public void CreatesModelRuntimeWithSchemaValidationAndCreateOptions()
    {
        var model = Create();
        var bypassConfig = new Fsrs4Config([.. Weights]);

        Model.DefaultMemoryState(FieldBag.Empty.With(Fsrs4Model.Weights, Weights)).Should().Be(new FsrsState(0, 0));
        Model.Create(bypassConfig, new ModelCreateOptions(Bypass: true)).Config.Should().BeSameAs(bypassConfig);

        var invalid = new FieldBag.Builder();
        invalid.SetRaw("weights", "invalid");
        var act1 = () => Model.Create(invalid.ToFieldBag(), new ModelCreateOptions(Migrate: false, Check: false));
        act1.Should().Throw<FsrsValidationException>();

        var act2 = () => Model.Create(
            new Fsrs4Config([0, .. Weights.Skip(1)]), new ModelCreateOptions(Migrate: false, Clip: false, Check: true));
        act2.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS4 weights within model bounds.");

        var extra = new FieldBag.Builder().Set(Fsrs4Model.Weights, Weights);
        extra.SetRaw("enableShortTerm", true);
        ((Fsrs4Config)Model.Create(extra.ToFieldBag()).Config).Weights.Should().Equal(((Fsrs4Config)model.Config).Weights);
    }

    [Fact]
    public void ExposesTheFsrs4ModelRuntimeMethods()
    {
        var model = Create();
        var initialState = model.Step(new ModelStepInput<FsrsState>(null, Rating.Easy, 0));
        var states = model.Forward([new ModelReview(Rating.Again, 0), new ModelReview(Rating.Good, 1)]);

        double.IsFinite(initialState.Stability).Should().BeTrue();
        double.IsFinite(initialState.Difficulty).Should().BeTrue();
        double.IsFinite(model.NextInterval(initialState, 0.9)).Should().BeTrue();
        double.IsFinite(model.ForgettingCurve(initialState, 14)).Should().BeTrue();
        states.Should().HaveCount(2);
    }

    [Fact]
    public void ExposesTheUnderlyingAlgorithmInstance() => Create().Algorithm.Should().BeOfType<Fsrs4Algorithm>();

    [Fact]
    public void UsesFsrs4DefaultWeights() =>
        Fsrs4Constants.DefaultWeights.Should().Equal(
            0.4, 0.9, 2.3, 10.9, 4.93, 0.94, 0.86, 0.01, 1.49, 0.14, 0.94, 2.18, 0.05, 0.34, 1.26, 0.29, 2.61);

    [Fact]
    public void CalculatesTheFsrs4ForgettingCurveAndIntervalModifier()
    {
        var model = Create();
        var state = new FsrsState(10, 5);

        Fsrs4Algorithm.ComputeForgettingCurve(10, 10).Should().Be(0.9);
        model.ForgettingCurve(state, 10).Should().Be(0.9);
        model.NextInterval(state, 0.9).Should().Be(10);
        model.NextInterval(state, 0.8).Should().Be(23);
    }

    [Fact]
    public void CalculatesInitialMemoryStatesWithFsrs4Formulas()
    {
        var model = Create();

        model.Step(new ModelStepInput<FsrsState>(null, Rating.Again, 0)).Should().Be(new FsrsState(0.4, 6.81));
        model.Step(new ModelStepInput<FsrsState>(null, Rating.Hard, 0)).Should().Be(new FsrsState(0.9, 5.87));
        model.Step(new ModelStepInput<FsrsState>(null, Rating.Good, 0)).Should().Be(new FsrsState(2.3, 4.93));
        model.Step(new ModelStepInput<FsrsState>(null, Rating.Easy, 0)).Should().Be(new FsrsState(10.9, 3.99));
    }

    [Fact]
    public void CalculatesReviewMemoryStatesWithFsrs4Formulas()
    {
        var model = Create();
        var initial = model.Step(new ModelStepInput<FsrsState>(null, Rating.Good, 0));
        var reviewed = model.Step(new ModelStepInput<FsrsState>(initial, Rating.Good, 2));
        var lapsed = model.Step(new ModelStepInput<FsrsState>(reviewed, Rating.Again, 6));

        CloseTo(reviewed.Stability, 7.06007207, 8);
        CloseTo(reviewed.Difficulty, 4.93, 8);
        CloseTo(lapsed.Stability, 2.31826584, 8);
        CloseTo(lapsed.Difficulty, 6.6328, 8);
    }

    [Fact]
    public void ForwardsReviewHistory()
    {
        var model = Create();
        var history = new List<ModelReview> { new(Rating.Good, 0), new(Rating.Good, 2), new(Rating.Again, 6) };

        model.Forward(history)[^1].Should().Be(new FsrsState(2.31826584, 6.6328));
    }

    [Fact]
    public void ExposesNameConfigKeysAndParseConfig()
    {
        Model.Name.Should().Be("fsrs-4");
        Model.ConfigKeys.Select(k => k.Name).Should().Equal("weights");
        var empty = () => Model.ParseConfig(FieldBag.Empty);
        empty.Should().Throw<FsrsValidationException>().WithMessage("Expected FSRS4 config");
    }
}
