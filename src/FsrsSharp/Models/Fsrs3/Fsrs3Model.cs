using FsrsSharp.Kit;

namespace FsrsSharp.Models.Fsrs3;

/// <summary>The FSRS-3 model definition (ts-fsrs <c>FSRS3Model</c>). A stateless singleton.</summary>
public sealed class Fsrs3Model : IModel<FsrsState>
{
    public static Fsrs3Model Instance { get; } = new();

    public static FieldKey<IReadOnlyList<double>> Weights { get; } =
        new("weights", v => ModelSupport.AsNumberList(v) is null ? Fsrs3Parameters.ConfigMessage : null);

    private static readonly FieldKey[] Keys = [Weights];

    private Fsrs3Model() { }

    public string Name => "fsrs-3";

    public IReadOnlyList<FieldKey> ConfigKeys => Keys;

    /// <summary>Upstream's config schema parse: validates <c>weights</c> (no defaults, no migration or clipping).</summary>
    public FieldBag ParseConfig(FieldBag config) =>
        ToBag(Fsrs3Parameters.ParseConfig(ModelSupport.Raw(config, Weights)));

    public FsrsState DefaultMemoryState(FieldBag config) => new(0, 0);

    public string? ValidateMemoryState(FsrsState state) => FsrsState.Validate(state);

    public IModelCore<FsrsState> Create(FieldBag config, ModelCreateOptions? options = null)
    {
        options ??= ModelCreateOptions.Default;
        if (options.Bypass) return CreateCore(FromBag(config));
        return CreateInternal(ModelSupport.AsNumberList(ModelSupport.Raw(config, Weights)), options);
    }

    /// <summary>Typed convenience overload of <see cref="Create(FieldBag, ModelCreateOptions?)"/>.</summary>
    public IModelCore<FsrsState> Create(Fsrs3Config config, ModelCreateOptions? options = null)
    {
        options ??= ModelCreateOptions.Default;
        if (options.Bypass) return CreateCore(config);
        return CreateInternal(config.Weights, options);
    }

    private static IModelCore<FsrsState> CreateInternal(IReadOnlyList<double>? rawWeights, ModelCreateOptions options)
    {
        var weights = options.Migrate ? Fsrs3Parameters.Migrate(rawWeights) : rawWeights;
        if (options.Clip && weights is not null) weights = Fsrs3Parameters.Clip(weights);
        if (options.Check)
        {
            if (weights is null) throw new FsrsValidationException(Fsrs3Parameters.ConfigMessage);
            Fsrs3Parameters.Check(weights);
        }
        return CreateCore(Fsrs3Parameters.ParseConfig(weights));
    }

    private static IModelCore<FsrsState> CreateCore(Fsrs3Config config)
    {
        var bounds = Fsrs3Constants.ModelBounds;
        var algo = new Fsrs3Algorithm(config.Weights, bounds);
        return new ModelSupport.Core(config, bounds, algo, algo.NextState, algo.NextInterval, algo.ForgettingCurve);
    }

    private static Fsrs3Config FromBag(FieldBag bag) => new(bag.Get(Weights));

    private static FieldBag ToBag(Fsrs3Config config) => FieldBag.Empty.With(Weights, config.Weights);
}
