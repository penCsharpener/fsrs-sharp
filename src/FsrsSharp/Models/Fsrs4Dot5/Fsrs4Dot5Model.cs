using FsrsSharp.Kit;

namespace FsrsSharp.Models.Fsrs4Dot5;

/// <summary>The FSRS-4.5 model definition (ts-fsrs <c>FSRS4Dot5Model</c>). A stateless singleton.</summary>
public sealed class Fsrs4Dot5Model : IModel<FsrsState>
{
    public static Fsrs4Dot5Model Instance { get; } = new();

    public static FieldKey<IReadOnlyList<double>> Weights { get; } =
        new("weights", v => ModelSupport.AsNumberList(v) is null ? Fsrs4Dot5Parameters.ConfigMessage : null);

    private static readonly FieldKey[] Keys = [Weights];

    private Fsrs4Dot5Model() { }

    public string Name => "fsrs-4dot5";

    public IReadOnlyList<FieldKey> ConfigKeys => Keys;

    /// <summary>Upstream's config schema parse: validates <c>weights</c> (no defaults, no migration or clipping).</summary>
    public FieldBag ParseConfig(FieldBag config) =>
        ToBag(Fsrs4Dot5Parameters.ParseConfig(ModelSupport.Raw(config, Weights)));

    public FsrsState DefaultMemoryState(FieldBag config) => new(0, 0);

    public string? ValidateMemoryState(FsrsState state) => FsrsState.Validate(state);

    public IModelCore<FsrsState> Create(FieldBag config, ModelCreateOptions? options = null)
    {
        options ??= ModelCreateOptions.Default;
        if (options.Bypass) return CreateCore(FromBag(config));
        return CreateInternal(ModelSupport.AsNumberList(ModelSupport.Raw(config, Weights)), options);
    }

    /// <summary>Typed convenience overload of <see cref="Create(FieldBag, ModelCreateOptions?)"/>.</summary>
    public IModelCore<FsrsState> Create(Fsrs4Dot5Config config, ModelCreateOptions? options = null)
    {
        options ??= ModelCreateOptions.Default;
        if (options.Bypass) return CreateCore(config);
        return CreateInternal(config.Weights, options);
    }

    private static IModelCore<FsrsState> CreateInternal(IReadOnlyList<double>? rawWeights, ModelCreateOptions options)
    {
        var weights = options.Migrate ? Fsrs4Dot5Parameters.Migrate(rawWeights) : rawWeights;
        if (options.Clip && weights is not null) weights = Fsrs4Dot5Parameters.Clip(weights);
        if (options.Check)
        {
            if (weights is null) throw new FsrsValidationException(Fsrs4Dot5Parameters.ConfigMessage);
            Fsrs4Dot5Parameters.Check(weights);
        }
        return CreateCore(Fsrs4Dot5Parameters.ParseConfig(weights));
    }

    private static IModelCore<FsrsState> CreateCore(Fsrs4Dot5Config config)
    {
        var bounds = Fsrs4Dot5Constants.ModelBounds;
        var algo = new Fsrs4Dot5Algorithm(config.Weights, bounds);
        return new ModelSupport.Core(config, bounds, algo, algo.NextState, algo.NextInterval, algo.ForgettingCurve);
    }

    private static Fsrs4Dot5Config FromBag(FieldBag bag) => new(bag.Get(Weights));

    private static FieldBag ToBag(Fsrs4Dot5Config config) => FieldBag.Empty.With(Weights, config.Weights);
}
