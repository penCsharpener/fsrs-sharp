using FsrsSharp.Kit;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs7;

namespace FsrsSharp;

/// <summary>
/// Widens <see cref="Fsrs7Model"/> to <c>IModel&lt;FsrsState&gt;</c> so every version shares one scheduler type
/// (upstream's runtime presets are widened the same way). Memory states stay <see cref="Fsrs7State"/> instances at
/// runtime; any other state is rejected by <see cref="ValidateMemoryState"/>.
/// </summary>
internal sealed class Fsrs7ModelAdapter : IModel<FsrsState>
{
    public static Fsrs7ModelAdapter Instance { get; } = new();

    private const string InvalidState = "Expected FSRS7 memory state with finite stability, stabilityFast and difficulty";

    private Fsrs7ModelAdapter() { }

    public string Name => Fsrs7Model.Instance.Name;

    public IReadOnlyList<FieldKey> ConfigKeys => Fsrs7Model.Instance.ConfigKeys;

    public FieldBag ParseConfig(FieldBag config) => Fsrs7Model.Instance.ParseConfig(config);

    public FsrsState DefaultMemoryState(FieldBag config) => Fsrs7Model.Instance.DefaultMemoryState(config);

    public string? ValidateMemoryState(FsrsState state) =>
        state is Fsrs7State fsrs7 ? Fsrs7Model.Instance.ValidateMemoryState(fsrs7) : InvalidState;

    public IModelCore<FsrsState> Create(FieldBag config, ModelCreateOptions? options = null) =>
        new Core(Fsrs7Model.Instance.Create(config, options));

    private static Fsrs7State Narrow(FsrsState state) =>
        state as Fsrs7State ?? throw new FsrsValidationException(InvalidState);

    private sealed class Core(IModelCore<Fsrs7State> inner) : IModelCore<FsrsState>
    {
        public object Config => inner.Config;
        public ModelBounds Bounds => inner.Bounds;
        public object Algorithm => inner.Algorithm;

        public FsrsState Step(ModelStepInput<FsrsState> input) =>
            inner.Step(new ModelStepInput<Fsrs7State>(
                input.MemoryState is null ? null : Narrow(input.MemoryState),
                input.Rating,
                input.ElapsedDays,
                input.Retrievability));

        public double NextInterval(FsrsState memoryState, double desiredRetention) =>
            inner.NextInterval(Narrow(memoryState), desiredRetention);

        public double ForgettingCurve(FsrsState memoryState, double elapsedDays) =>
            inner.ForgettingCurve(Narrow(memoryState), elapsedDays);

        public IReadOnlyList<FsrsState> Forward(IReadOnlyList<ModelReview> history, FsrsState? initialState = null) =>
            [.. inner.Forward(history, initialState is null ? null : Narrow(initialState))];
    }
}
