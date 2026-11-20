using Xunit;
using System.Text.Json;
using AwesomeAssertions;
using FsrsSharp.Kit;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs7;

namespace FsrsSharp.Tests.Models.Fsrs7;

/// <summary>Port of ts-fsrs <c>models/fsrs-7/model.spec.ts</c>.</summary>
public class Fsrs7ModelTests
{
    private const double Epsilon = 2.220446049250313e-16; // Number.EPSILON

    private sealed record ReferenceHistory(
        IReadOnlyList<ModelReview> History,
        IReadOnlyList<Fsrs7State> States,
        IReadOnlyList<Fsrs7State> BenchmarkStates);

    private sealed record ReferenceNext(
        Fsrs7State State,
        IReadOnlyList<(double Retention, double Interval)> Intervals,
        Fsrs7State BenchmarkState);

    private sealed record ReferenceCase(
        Fsrs7State? Input,
        double ElapsedDays,
        double Retrievability,
        IReadOnlyList<ReferenceNext> Next,
        double BenchmarkRetrievability);

    private static Fsrs7State ParseState(JsonElement e) => new(
        e.GetProperty("stability").GetDouble(),
        e.GetProperty("stabilityFast").GetDouble(),
        e.GetProperty("difficulty").GetDouble());

    // Reference: fsrs-rs c9562d6 and srs-benchmark b4b0254, evaluated in float32.
    private static readonly ReferenceHistory[] ReferenceHistories =
        JsonDocument.Parse(Fsrs7ReferenceData.HistoriesJson).RootElement.EnumerateArray()
            .Select(e => new ReferenceHistory(
                e.GetProperty("history").EnumerateArray()
                    .Select(h => new ModelReview((Rating)h.GetProperty("rating").GetInt32(), h.GetProperty("deltaT").GetDouble()))
                    .ToArray(),
                e.GetProperty("states").EnumerateArray().Select(ParseState).ToArray(),
                e.GetProperty("benchmarkStates").EnumerateArray().Select(ParseState).ToArray()))
            .ToArray();

    private static readonly ReferenceCase[] ReferenceCases =
        JsonDocument.Parse(Fsrs7ReferenceData.CasesJson).RootElement.EnumerateArray()
            .Select(e => new ReferenceCase(
                e.GetProperty("input").ValueKind == JsonValueKind.Null ? null : ParseState(e.GetProperty("input")),
                e.GetProperty("elapsedDays").GetDouble(),
                e.GetProperty("retrievability").GetDouble(),
                e.GetProperty("next").EnumerateArray()
                    .Select(n => new ReferenceNext(
                        ParseState(n.GetProperty("state")),
                        n.GetProperty("intervals").EnumerateArray()
                            .Select(i => (i.GetProperty("retention").GetDouble(), i.GetProperty("interval").GetDouble()))
                            .ToArray(),
                        ParseState(n.GetProperty("benchmarkState"))))
                    .ToArray(),
                e.GetProperty("benchmarkRetrievability").GetDouble()))
            .ToArray();

    public static TheoryData<int> HistoryIndexes => [0, 1, 2];
    public static TheoryData<int> CaseIndexes => [0, 1, 2, 3, 4, 5];

    private static readonly Fsrs7ModelCore Model = Fsrs7Model.Instance.Create(new Fsrs7Config(Fsrs7Constants.DefaultWeights));

    private static Fsrs7State S(double stability, double fast, double difficulty) => new(stability, fast, difficulty);

    // Both independent references use float32; JS evaluates the formulas in float64.
    private static void ExpectState(Fsrs7State actual, Fsrs7State expected)
    {
        void Check(string key, double a, double e) =>
            Math.Abs(a - e).Should().BeLessThanOrEqualTo(Math.Max(1e-7, Math.Abs(e) * 1e-4), $"{key}: {a} vs {e}");
        Check("stability", actual.Stability, expected.Stability);
        Check("stabilityFast", actual.StabilityFast, expected.StabilityFast);
        Check("difficulty", actual.Difficulty, expected.Difficulty);
    }

    private static Func<Fsrs7State> Step(Fsrs7State state, double elapsedDays, Rating rating, double? retrievability = null) =>
        () => Model.Step(new ModelStepInput<Fsrs7State>(state, rating, elapsedDays, retrievability));

    // --- FSRS7Model reference parity ---------------------------------------------------------

    [Fact]
    public void ExposesTheDefaultConfigurationAndAlgorithm()
    {
        Model.Config.Weights.Should().Equal(Fsrs7Constants.DefaultWeights);
        Model.Algorithm.Should().BeOfType<Fsrs7Algorithm>();
        ((IModelCore<Fsrs7State>)Model).Algorithm.Should().BeSameAs(Model.Algorithm);
        Fsrs7Model.Instance.Name.Should().Be("fsrs-7");
    }

    [Theory]
    [MemberData(nameof(HistoryIndexes))]
    public void ReplaysEveryMemoryStateInAHistory(int index)
    {
        var (reviews, states, benchmarkStates) = ReferenceHistories[index];
        var actual = Model.Forward(reviews);
        actual.Should().HaveCount(states.Count);
        for (var i = 0; i < actual.Count; i++)
        {
            ExpectState(actual[i], states[i]);
            ExpectState(actual[i], benchmarkStates[i]);
        }
        var split = reviews.Count / 2;
        Model.Forward(reviews.Skip(split).ToArray(), actual[split - 1]).Should().Equal(actual.Skip(split));
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void MatchesAllFourGradesAtTheReferenceElapsedDays(int index)
    {
        var test = ReferenceCases[index];
        if (test.Input is { } input)
        {
            // toBeCloseTo(x, 5): |a - b| < 5e-6
            Math.Abs(Model.ForgettingCurve(input, test.ElapsedDays) - test.Retrievability).Should().BeLessThan(5e-6);
            Math.Abs(Model.ForgettingCurve(input, test.ElapsedDays) - test.BenchmarkRetrievability).Should().BeLessThan(5e-6);
        }
        for (var i = 0; i < test.Next.Count; i++)
        {
            var expected = test.Next[i];
            var actual = Step(test.Input!, test.ElapsedDays, (Rating)(i + 1)).Invoke();
            ExpectState(actual, expected.State);
            ExpectState(actual, expected.BenchmarkState);
            foreach (var (retention, interval) in expected.Intervals)
            {
                // Use the reference state to isolate interval inversion from recurrence drift.
                var result = Model.NextInterval(expected.State, retention);
                Math.Abs(result - interval).Should().BeLessThanOrEqualTo(Math.Max(1e-8, interval * 2e-3),
                    $"interval at {retention}: {result} vs {interval}");
            }
        }
    }

    [Fact]
    public void HasDistinctFastAndSlowTracesAndDoesNotRoundFractionalElapsedDays()
    {
        var state = S(10, 8, 5);
        var step = (double elapsedDays) => Step(state, elapsedDays, Rating.Good).Invoke();
        step(0.5).Should().NotBe(step(0));
        step(0.5).Should().NotBe(step(1));
        Model.ForgettingCurve(state with { StabilityFast = 0.01 }, 0.5).Should().NotBe(Model.ForgettingCurve(state, 0.5));
        Math.Abs(Model.ForgettingCurve(state, 0) - 0.99999).Should().BeLessThan(5e-11);
    }

    [Fact]
    public void KeepsARepeatedLapseUnderTwoMinutesButGivesTheFollowingGoodOverTwoHoursAt65PercentRetention()
    {
        var states = Model.Forward(ReferenceHistories[1].History);
        var again = Model.NextInterval(states[3], 0.65) * 1440;
        again.Should().BeGreaterThan(0);
        again.Should().BeLessThan(2);
        (Model.NextInterval(states[4], 0.65) * 1440).Should().BeGreaterThan(120);
    }

    [Fact]
    public void DoesNotTreatW20AsAForgettingCurveDecayParameter()
    {
        var weights = Fsrs7Constants.DefaultWeights.ToArray();
        weights[20] = 0.001;
        var a = Fsrs7Model.Instance.Create(new Fsrs7Config(weights.ToArray()));
        weights[20] = 5;
        var b = Fsrs7Model.Instance.Create(new Fsrs7Config(weights.ToArray()));
        var state = S(12, 9.6, 5);
        a.ForgettingCurve(state, 10).Should().Be(b.ForgettingCurve(state, 10));
    }

    [Fact]
    public void UsesCachedMixedRetrievabilityOnlyForSlowStabilityAndDifficulty()
    {
        var state = S(10, 8, 5);
        var normal = Step(state, 0.5, Rating.Again).Invoke();
        Step(state, 0.5, Rating.Again, Model.ForgettingCurve(state, 0.5)).Invoke().Should().Be(normal);
        Step(state, 0.5, Rating.Again, 0.1).Invoke().Difficulty.Should().BeLessThan(normal.Difficulty);
    }

    [Fact]
    public void InvertsTheCurveForHighRetentionIncludingSubsecondAndSaturatedIntervals()
    {
        foreach (var stability in new[] { 0.0001, 0.01, 1, 100, 36500 })
        {
            foreach (var retention in new[] { 0.1, 0.65, 0.9, 0.99, 0.9998 })
            {
                var state = S(stability, stability * 0.8, 5);
                var interval = Model.NextInterval(state, retention);
                interval.Should().BeGreaterThanOrEqualTo(0);
                interval.Should().BeLessThanOrEqualTo(36500);
                if (interval < 36499.99)
                    Math.Abs(Model.ForgettingCurve(state, interval) - retention).Should().BeLessThanOrEqualTo(1e-3);
            }
        }
        Model.NextInterval(S(1, 0.8, 5), 1 - Epsilon).Should().Be(0);
    }

    // --- FSRS7Model validation and create controls ---------------------------------------------

    [Fact]
    public void RequiresSuppliedRetrievabilityToBeStrictlyBetweenZeroAndOne()
    {
        var memoryState = S(10, 8, 5);
        foreach (var retrievability in new[] { 0, 1, -0.1, 1.1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Step(memoryState, 1, Rating.Good, retrievability).Should().Throw<FsrsValidationException>()
                .WithMessage("Retrievability should be in the range (0,1)");
        }
        foreach (var retrievability in new[] { double.Epsilon, 0.5, 1 - Epsilon })
            Step(memoryState, 1, Rating.Good, retrievability).Should().NotThrow();
        Step(memoryState, 1, Rating.Good).Invoke().Should().Be(
            Step(memoryState, 1, Rating.Good, Model.ForgettingCurve(memoryState, 1)).Invoke());
    }

    [Fact]
    public void SupportsDefaultsEmptyHistoriesAndManualNoOpWithoutMutatingInputs()
    {
        var initial = Fsrs7Model.Instance.DefaultMemoryState(FieldBag.Empty);
        initial.Should().Be(S(0, 0, 0));
        Model.Forward([]).Should().BeEmpty();
        Model.Algorithm.NextState(initial, 0, Rating.Manual).Should().Be(initial);
        Model.Algorithm.NextState(null, 0, Rating.Manual).Should().Be(initial);
        var frozen = S(10, 8, 5);
        Step(frozen, 0.1, Rating.Again).Invoke();
        frozen.Should().Be(S(10, 8, 5));
    }

    [Fact]
    public void TreatsANullMemoryStateAsANewCard()
    {
        var viaNull = Model.Step(new ModelStepInput<Fsrs7State>(null, Rating.Good, 0));
        viaNull.Should().Be(Model.Step(new ModelStepInput<Fsrs7State>(S(0, 0, 0), Rating.Good, 0)));
    }

    [Fact]
    public void KeepsMigrateClipCheckAndBypassIndependent()
    {
        var instance = Fsrs7Model.Instance;
        instance.Create(new Fsrs7Config([])).Config.Weights.Should().Equal(Fsrs7Constants.DefaultWeights);

        var config = new Fsrs7Config(Fsrs7Constants.DefaultWeights.Select((w, i) => i == 0 ? 0 : w).ToArray());
        var noClip = () => instance.Create(config, new ModelCreateOptions(Clip: false));
        noClip.Should().Throw<FsrsValidationException>().WithMessage("*Expected FSRS7 weights*");
        instance.Create(config, new ModelCreateOptions(Migrate: false)).Config.Weights[0].Should().Be(0.0001);
        instance.Create(config, new ModelCreateOptions(Clip: false, Check: false)).Config.Weights[0].Should().Be(0);
        instance.Create(config, new ModelCreateOptions(Bypass: true)).Config.Should().BeSameAs(config);

        var noMigrate = () => instance.Create(new Fsrs7Config([]), new ModelCreateOptions(Migrate: false));
        noMigrate.Should().Throw<FsrsValidationException>();
        var bad = () => instance.Create(FieldBag.From([new KeyValuePair<string, object?>("weights", "bad")]));
        bad.Should().Throw<FsrsValidationException>();
    }

    [Fact]
    public void CreatesFromAFieldBagAndAcceptsParsedConfigWithBypass()
    {
        var instance = Fsrs7Model.Instance;
        var bag = FieldBag.Empty.With(Fsrs7Model.Weights, Fsrs7Constants.DefaultWeights);
        instance.Create(bag).Config.Should().BeEquivalentTo(new Fsrs7Config(Fsrs7Constants.DefaultWeights));
        instance.Create(FieldBag.Empty).Config.Should().BeEquivalentTo(new Fsrs7Config(Fsrs7Constants.DefaultWeights));
        var parsed = instance.ParseConfig(bag);
        parsed.Keys.Should().Equal("weights");
        instance.Create(parsed, new ModelCreateOptions(Bypass: true)).Config.Should().BeOfType<Fsrs7Config>()
            .Which.Weights.Should().Equal(Fsrs7Constants.DefaultWeights);
        instance.ConfigKeys.Should().ContainSingle().Which.Name.Should().Be("weights");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(1.01)]
    public void RejectsInvalidRetention(double retention)
    {
        var act = () => Model.NextInterval(S(1, 1, 5), retention);
        act.Should().Throw<FsrsValidationException>().WithMessage("Desired retention rate should be in the range (0,1)");
    }

    [Fact]
    public void RejectsMissingOrNonFiniteFastStabilityRatherThanDroppingIt()
    {
        // Upstream also parses a state without stabilityFast; the record makes that unrepresentable.
        Fsrs7Model.Instance.ValidateMemoryState(S(1, double.NaN, 5)).Should().NotBeNull();
        Step(S(1, 0, 5), 0, Rating.Good).Should().Throw<FsrsValidationException>();
        foreach (var grade in new[] { -1, 5 })
        {
            var act = () => Model.Algorithm.NextState(null, 0, (Rating)grade);
            act.Should().Throw<FsrsValidationException>();
        }
        var ctor = () => new Fsrs7Algorithm([], Fsrs7Constants.ModelBounds);
        ctor.Should().Throw<FsrsValidationException>();
    }
}
