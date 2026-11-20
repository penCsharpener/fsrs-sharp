using FsrsSharp.Kit;

namespace FsrsSharp.Models.Fsrs6;

/// <summary>The FSRS-6 model definition (ts-fsrs <c>FSRS6Model</c>). A stateless singleton.</summary>
public sealed class Fsrs6Model : IModel<FsrsState>
{
    public static Fsrs6Model Instance { get; } = new();

    public static FieldKey<IReadOnlyList<double>> Weights { get; } =
        new("weights", v => ModelSupport.AsNumberList(v) is null ? Fsrs6Parameters.ConfigMessage : null);

    public static FieldKey<bool> EnableShortTerm { get; } = new("enableShortTerm");

    public static FieldKey<int> NumRelearningSteps { get; } =
        new("numRelearningSteps", v => v >= 0 ? null : Fsrs6Parameters.ConfigMessage);

    private static readonly FieldKey[] Keys = [Weights, EnableShortTerm, NumRelearningSteps];

    private Fsrs6Model() { }

    public string Name => "fsrs-6";

    public IReadOnlyList<FieldKey> ConfigKeys => Keys;

    /// <summary>Upstream's config schema parse: validates the three fields (no defaults, no migration or clipping).</summary>
    public FieldBag ParseConfig(FieldBag config)
    {
        var parsed = Fsrs6Parameters.ParseConfig(
            ModelSupport.Raw(config, Weights), ModelSupport.Raw(config, EnableShortTerm), ModelSupport.Raw(config, NumRelearningSteps));
        return ToBag(parsed);
    }

    public FsrsState DefaultMemoryState(FieldBag config) => new(0, 0);

    public string? ValidateMemoryState(FsrsState state) => FsrsState.Validate(state);

    public IModelCore<FsrsState> Create(FieldBag config, ModelCreateOptions? options = null)
    {
        options ??= ModelCreateOptions.Default;
        if (options.Bypass) return CreateCore(FromBag(config));
        return CreateInternal(
            ModelSupport.AsNumberList(ModelSupport.Raw(config, Weights)),
            ModelSupport.AsBool(ModelSupport.Raw(config, EnableShortTerm)),
            ModelSupport.AsNumber(ModelSupport.Raw(config, NumRelearningSteps)),
            options);
    }

    /// <summary>Typed convenience overload of <see cref="Create(FieldBag, ModelCreateOptions?)"/>.</summary>
    public IModelCore<FsrsState> Create(Fsrs6Config config, ModelCreateOptions? options = null)
    {
        options ??= ModelCreateOptions.Default;
        if (options.Bypass) return CreateCore(config);
        return CreateInternal(config.Weights, config.EnableShortTerm, config.NumRelearningSteps, options);
    }

    private static IModelCore<FsrsState> CreateInternal(
        IReadOnlyList<double>? rawWeights, bool? enableShortTerm, double? numRelearningSteps, ModelCreateOptions options)
    {
        var weights = options.Migrate ? Fsrs6Parameters.Migrate(rawWeights) : rawWeights;
        if (options.Clip && weights is not null)
            weights = Fsrs6Parameters.Clip(weights, numRelearningSteps ?? 0, enableShortTerm ?? true);
        if (options.Check)
        {
            if (weights is null) throw new FsrsValidationException(Fsrs6Parameters.ConfigMessage);
            Fsrs6Parameters.Check(weights, numRelearningSteps ?? 0, enableShortTerm ?? true);
        }
        return CreateCore(Fsrs6Parameters.ParseConfig(weights, enableShortTerm, numRelearningSteps));
    }

    private static IModelCore<FsrsState> CreateCore(Fsrs6Config config)
    {
        var bounds = Fsrs6Constants.ModelBounds;
        var algo = new Fsrs6Algorithm(config.Weights, config.EnableShortTerm, bounds);
        return new ModelSupport.Core(config, bounds, algo, algo.NextState, algo.NextInterval, algo.ForgettingCurve);
    }

    private static Fsrs6Config FromBag(FieldBag bag) => new(
        bag.Get(Weights), bag.Get(EnableShortTerm), bag.Get(NumRelearningSteps));

    private static FieldBag ToBag(Fsrs6Config config) => FieldBag.Empty
        .With(Weights, config.Weights)
        .With(EnableShortTerm, config.EnableShortTerm)
        .With(NumRelearningSteps, config.NumRelearningSteps);
}
