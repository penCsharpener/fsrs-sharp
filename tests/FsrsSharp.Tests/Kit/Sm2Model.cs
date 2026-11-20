using FsrsSharp.Kit;

namespace FsrsSharp.Tests.Kit;

/// <summary>Port of srs-kit's <c>model/sm2.test.ts</c> memory state.</summary>
public sealed record Sm2State(double Interval, double EaseFactor, double ReviewStep);

public sealed record Sm2Config(IReadOnlyList<double> Weights);

/// <summary>
/// Port of srs-kit's SM-2 test model (<c>model/sm2.test.ts</c>): the model the kit tests run on, so
/// the scheduler engine is exercised without FSRS. The created core records its calls and lets tests
/// replace <c>Step</c> / <c>NextInterval</c> (the TS tests use <c>vi.spyOn</c>).
/// </summary>
public class Sm2Model : IModel<Sm2State>
{
    public static readonly IReadOnlyList<double> DefaultWeights = [1, 6, 2.5, 0.02, 7, 0.18];

    public static ModelBounds Bounds { get; } = new(new Dictionary<string, double>
    {
        ["iMin"] = 0.01,
        ["iMax"] = 36500,
        ["eMin"] = 1.3,
        ["eMax"] = 10.0,
        ["rMin"] = 0,
        ["rMax"] = 36500,
    });


    public static FieldKey<IReadOnlyList<double>> Weights { get; } = new("weights", ValidateWeights);

    public static FieldBag DefaultConfig { get; } = new FieldBag.Builder { { Weights, DefaultWeights } }.ToFieldBag();

    public static Sm2Model Instance { get; } = new();

    private static string? ValidateWeights(IReadOnlyList<double> weights) =>
        weights is { Count: 6 } && weights.All(double.IsFinite) ? null : "Expected SM2 config with 6 finite number weights";

    public string Name => "sm2";
    public IReadOnlyList<FieldKey> ConfigKeys { get; } = [Weights];

    /// <summary>How often <see cref="ParseConfig"/> ran (the scheduler must parse the model config exactly once).</summary>
    public int ParseConfigCalls { get; private set; }

    public FieldBag ParseConfig(FieldBag config)
    {
        ParseConfigCalls++;
        if (!config.TryGet(Weights, out var weights)) throw new FsrsValidationException("Expected SM2 config with 6 finite number weights");
        var error = ValidateWeights(weights);
        if (error is not null) throw new FsrsValidationException(error);
        return new FieldBag.Builder { { Weights, weights } }.ToFieldBag();
    }

    public Sm2State DefaultMemoryState(FieldBag config) => new(0, config.Get(Weights)[2], 0);

    public string? ValidateMemoryState(Sm2State state) =>
        double.IsNaN(state.Interval) || double.IsNaN(state.EaseFactor) || double.IsNaN(state.ReviewStep)
            ? "Expected SM2 memory state"
            : null;

    public IModelCore<Sm2State> Create(FieldBag config, ModelCreateOptions? options = null)
    {
        options ??= ModelCreateOptions.Default;
        return new Sm2Core(options.Bypass ? config : ParseConfig(config));
    }
}

public class Sm2Core : IModelCore<Sm2State>
{
    private readonly IReadOnlyList<double> _w;

    public Sm2Core(FieldBag config)
    {
        Config = new Sm2Config(config.Get(Sm2Model.Weights));
        _w = ((Sm2Config)Config).Weights;
    }

    public object Config { get; }
    public ModelBounds Bounds => Sm2Model.Bounds;
    public object Algorithm { get; } = new();

    public List<ModelStepInput<Sm2State>> StepCalls { get; } = [];
    public List<(Sm2State State, double Elapsed)> CurveCalls { get; } = [];
    public List<(Sm2State State, double Retention)> IntervalCalls { get; } = [];
    public Func<ModelStepInput<Sm2State>, Sm2State>? StepOverride { get; set; }
    public Func<Sm2State, double, double>? NextIntervalOverride { get; set; }

    private static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);

    public Sm2State Step(ModelStepInput<Sm2State> input)
    {
        StepCalls.Add(input);
        if (StepOverride is not null) return StepOverride(input);
        var (interval, easeFactor, reviewStep) = input.MemoryState ?? new Sm2State(0, _w[2], 0);
        var rating = (int)input.Rating;
        var success = rating > 1;
        var newReviewStep = success ? reviewStep + 1 : 1;
        var newInterval = newReviewStep == 1 ? _w[0] : newReviewStep == 2 ? _w[1] : interval * easeFactor;
        var q = rating + 1;
        var newEf = easeFactor - _w[3] * Math.Pow(q - _w[4], 2) + _w[5];
        return new Sm2State(
            Clamp(newInterval, Sm2Model.Bounds["iMin"], Sm2Model.Bounds["iMax"]),
            Clamp(newEf, Sm2Model.Bounds["eMin"], Sm2Model.Bounds["eMax"]),
            newReviewStep);
    }

    public double NextInterval(Sm2State memoryState, double desiredRetention)
    {
        IntervalCalls.Add((memoryState, desiredRetention));
        if (NextIntervalOverride is not null) return NextIntervalOverride(memoryState, desiredRetention);
        return Math.Max(Helpers.JsRound(memoryState.Interval * Math.Log(desiredRetention) / Math.Log(0.9)), 1);
    }

    public double ForgettingCurve(Sm2State memoryState, double elapsedDays)
    {
        CurveCalls.Add((memoryState, elapsedDays));
        return Math.Pow(0.9, elapsedDays / memoryState.Interval);
    }

    public IReadOnlyList<Sm2State> Forward(IReadOnlyList<ModelReview> history, Sm2State? initialState = null)
    {
        var states = new List<Sm2State>();
        var state = initialState;
        foreach (var review in history)
        {
            state = Step(new ModelStepInput<Sm2State>(state, review.Rating, review.DeltaT));
            states.Add(state);
        }

        return states;
    }
}
