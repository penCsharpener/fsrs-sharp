using FsrsSharp.Kit;

namespace FsrsSharp.Models.Fsrs7;

/// <summary>A configured FSRS-7 model (ts-fsrs <c>FSRS7ModelCore</c>).</summary>
public sealed class Fsrs7ModelCore : IModelCore<Fsrs7State>
{
    internal Fsrs7ModelCore(Fsrs7Config config, ModelBounds bounds)
    {
        Config = config;
        Bounds = bounds;
        Algorithm = new Fsrs7Algorithm(config.Weights, bounds);
    }

    public Fsrs7Config Config { get; }
    public ModelBounds Bounds { get; }
    public Fsrs7Algorithm Algorithm { get; }

    object IModelCore<Fsrs7State>.Config => Config;
    object IModelCore<Fsrs7State>.Algorithm => Algorithm;

    public Fsrs7State Step(ModelStepInput<Fsrs7State> input) =>
        Algorithm.NextState(input.MemoryState, input.ElapsedDays, input.Rating, input.Retrievability);

    public double NextInterval(Fsrs7State memoryState, double desiredRetention) =>
        Algorithm.NextInterval(memoryState, desiredRetention);

    public double ForgettingCurve(Fsrs7State memoryState, double elapsedDays) =>
        Algorithm.Curve(elapsedDays, memoryState).Retrievability;

    public IReadOnlyList<Fsrs7State> Forward(IReadOnlyList<ModelReview> history, Fsrs7State? initialState = null)
    {
        var states = new List<Fsrs7State>(history.Count);
        var state = initialState;
        foreach (var (rating, deltaT) in history)
        {
            state = Algorithm.NextState(state, deltaT, rating);
            states.Add(state);
        }
        return states;
    }
}

/// <summary>The FSRS-7 model (ts-fsrs <c>FSRS7Model</c>). Stateless singleton.</summary>
public sealed class Fsrs7Model : IModel<Fsrs7State>
{
    public static Fsrs7Model Instance { get; } = new();

    /// <summary>The <c>weights</c> config field.</summary>
    public static FieldKey<IReadOnlyList<double>> Weights => Fsrs7Schema.Weights;

    private Fsrs7Model() { }

    public string Name => "fsrs-7";

    public IReadOnlyList<FieldKey> ConfigKeys { get; } = [Fsrs7Schema.Weights];

    /// <summary>Upstream's scheduler config parse for this model: the config schema only (no migrate/clip/check).</summary>
    public FieldBag ParseConfig(FieldBag config) =>
        FieldBag.Empty.With(Weights, Fsrs7Schema.ParseConfig(config).Weights);

    public Fsrs7State DefaultMemoryState(FieldBag config) => new(0, 0, 0);

    public string? ValidateMemoryState(Fsrs7State state) => Fsrs7State.Validate(state);

    public IModelCore<Fsrs7State> Create(FieldBag config, ModelCreateOptions? options = null)
    {
        options ??= ModelCreateOptions.Default;
        if (options.Bypass) return Create(Fsrs7Schema.ParseConfig(config), options);

        // Upstream passes the config through as-is: a missing weights field migrates to the defaults.
        if (!config.TryGetValue(Weights.Name, out var raw) || raw is null)
            return CreateFromWeights(null, options);
        if (raw is IReadOnlyList<double> weights) return CreateFromWeights(weights, options);
        if (raw is IEnumerable<double> numbers) return CreateFromWeights(numbers.ToArray(), options);
        throw new FsrsValidationException(Fsrs7Schema.ConfigMessage);
    }

    /// <summary>Typed convenience for <see cref="Create(FieldBag, ModelCreateOptions?)"/>.</summary>
    public Fsrs7ModelCore Create(Fsrs7Config config, ModelCreateOptions? options = null)
    {
        options ??= ModelCreateOptions.Default;
        return options.Bypass
            ? new Fsrs7ModelCore(config, Fsrs7Constants.ModelBounds)
            : CreateFromWeights(config.Weights, options);
    }

    private static Fsrs7ModelCore CreateFromWeights(IReadOnlyList<double>? weights, ModelCreateOptions options)
    {
        IReadOnlyList<double>? current = options.Migrate ? Fsrs7Parameters.Migrate(weights) : weights;
        if (current is null) throw new FsrsValidationException(Fsrs7Schema.ConfigMessage);
        if (options.Clip) current = Fsrs7Parameters.Clip(current);
        if (options.Check) Fsrs7Parameters.Check(current);
        return new Fsrs7ModelCore(Fsrs7Schema.ParseWeights(current), Fsrs7Constants.ModelBounds);
    }
}
