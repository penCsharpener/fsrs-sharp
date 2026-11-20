using FsrsSharp.Kit;

namespace FsrsSharp.Models.Fsrs4;

/// <summary>The FSRS-4 model definition (ts-fsrs <c>FSRS4Model</c>). A stateless singleton.</summary>
public sealed class Fsrs4Model : IModel<FsrsState>
{
    public static Fsrs4Model Instance { get; } = new();

    public static FieldKey<IReadOnlyList<double>> Weights { get; } =
        new("weights", v => ModelSupport.AsNumberList(v) is null ? Fsrs4Parameters.ConfigMessage : null);

    private static readonly FieldKey[] Keys = [Weights];

    private Fsrs4Model() { }

    public string Name => "fsrs-4";

    public IReadOnlyList<FieldKey> ConfigKeys => Keys;

    /// <summary>Upstream's config schema parse: validates <c>weights</c> (no defaults, no migration or clipping).</summary>
    public FieldBag ParseConfig(FieldBag config) =>
        ToBag(Fsrs4Parameters.ParseConfig(ModelSupport.Raw(config, Weights)));

    public FsrsState DefaultMemoryState(FieldBag config) => new(0, 0);

    public string? ValidateMemoryState(FsrsState state) => FsrsState.Validate(state);

    public IModelCore<FsrsState> Create(FieldBag config, ModelCreateOptions? options = null)
    {
        options ??= ModelCreateOptions.Default;
        if (options.Bypass) return CreateCore(FromBag(config));
        return CreateInternal(ModelSupport.AsNumberList(ModelSupport.Raw(config, Weights)), options);
    }

    /// <summary>Typed convenience overload of <see cref="Create(FieldBag, ModelCreateOptions?)"/>.</summary>
    public IModelCore<FsrsState> Create(Fsrs4Config config, ModelCreateOptions? options = null)
    {
        options ??= ModelCreateOptions.Default;
        if (options.Bypass) return CreateCore(config);
        return CreateInternal(config.Weights, options);
    }

    private static IModelCore<FsrsState> CreateInternal(IReadOnlyList<double>? rawWeights, ModelCreateOptions options)
    {
        var weights = options.Migrate ? Fsrs4Parameters.Migrate(rawWeights) : rawWeights;
        if (options.Clip && weights is not null) weights = Fsrs4Parameters.Clip(weights);
        if (options.Check)
        {
            if (weights is null) throw new FsrsValidationException(Fsrs4Parameters.ConfigMessage);
            Fsrs4Parameters.Check(weights);
        }
        return CreateCore(Fsrs4Parameters.ParseConfig(weights));
    }

    private static IModelCore<FsrsState> CreateCore(Fsrs4Config config)
    {
        var bounds = Fsrs4Constants.ModelBounds;
        var algo = new Fsrs4Algorithm(config.Weights, bounds);
        return new ModelSupport.Core(config, bounds, algo, algo.NextState, algo.NextInterval, algo.ForgettingCurve);
    }

    private static Fsrs4Config FromBag(FieldBag bag) => new(bag.Get(Weights));

    private static FieldBag ToBag(Fsrs4Config config) => FieldBag.Empty.With(Weights, config.Weights);
}
