using FsrsSharp.Kit;

namespace FsrsSharp.Models.Fsrs5;

/// <summary>The FSRS-5 model definition (ts-fsrs <c>FSRS5Model</c>). A stateless singleton.</summary>
public sealed class Fsrs5Model : IModel<FsrsState>
{
    public static Fsrs5Model Instance { get; } = new();

    public static FieldKey<IReadOnlyList<double>> Weights { get; } =
        new("weights", v => ModelSupport.AsNumberList(v) is null ? Fsrs5Parameters.ConfigMessage : null);

    public static FieldKey<bool> EnableShortTerm { get; } = new("enableShortTerm");

    private static readonly FieldKey[] Keys = [Weights, EnableShortTerm];

    private Fsrs5Model() { }

    public string Name => "fsrs-5";

    public IReadOnlyList<FieldKey> ConfigKeys => Keys;

    /// <summary>Upstream's config schema parse: validates the two fields (no defaults, no migration or clipping).</summary>
    public FieldBag ParseConfig(FieldBag config) =>
        ToBag(Fsrs5Parameters.ParseConfig(ModelSupport.Raw(config, Weights), ModelSupport.Raw(config, EnableShortTerm)));

    public FsrsState DefaultMemoryState(FieldBag config) => new(0, 0);

    public string? ValidateMemoryState(FsrsState state) => FsrsState.Validate(state);

    public IModelCore<FsrsState> Create(FieldBag config, ModelCreateOptions? options = null)
    {
        options ??= ModelCreateOptions.Default;
        if (options.Bypass) return CreateCore(FromBag(config));
        return CreateInternal(
            ModelSupport.AsNumberList(ModelSupport.Raw(config, Weights)),
            ModelSupport.AsBool(ModelSupport.Raw(config, EnableShortTerm)),
            options);
    }

    /// <summary>Typed convenience overload of <see cref="Create(FieldBag, ModelCreateOptions?)"/>.</summary>
    public IModelCore<FsrsState> Create(Fsrs5Config config, ModelCreateOptions? options = null)
    {
        options ??= ModelCreateOptions.Default;
        if (options.Bypass) return CreateCore(config);
        return CreateInternal(config.Weights, config.EnableShortTerm, options);
    }

    private static IModelCore<FsrsState> CreateInternal(IReadOnlyList<double>? rawWeights, bool? enableShortTerm, ModelCreateOptions options)
    {
        var weights = options.Migrate ? Fsrs5Parameters.Migrate(rawWeights) : rawWeights;
        if (options.Clip && weights is not null) weights = Fsrs5Parameters.Clip(weights);
        if (options.Check)
        {
            if (weights is null) throw new FsrsValidationException(Fsrs5Parameters.ConfigMessage);
            Fsrs5Parameters.Check(weights);
        }
        return CreateCore(Fsrs5Parameters.ParseConfig(weights, enableShortTerm));
    }

    private static IModelCore<FsrsState> CreateCore(Fsrs5Config config)
    {
        var bounds = Fsrs5Constants.ModelBounds;
        var algo = new Fsrs5Algorithm(config.Weights, config.EnableShortTerm, bounds);
        return new ModelSupport.Core(config, bounds, algo, algo.NextState, algo.NextInterval, algo.ForgettingCurve);
    }

    private static Fsrs5Config FromBag(FieldBag bag) => new(bag.Get(Weights), bag.Get(EnableShortTerm));

    private static FieldBag ToBag(Fsrs5Config config) => FieldBag.Empty
        .With(Weights, config.Weights)
        .With(EnableShortTerm, config.EnableShortTerm);
}
