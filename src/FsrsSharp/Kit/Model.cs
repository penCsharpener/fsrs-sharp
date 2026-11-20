namespace FsrsSharp.Kit;

/// <summary>Input to one memory-state transition (srs-kit <c>ModelStepInput</c>).</summary>
/// <param name="MemoryState">The state before the review, or <c>null</c> for a card never reviewed.</param>
/// <param name="Rating">The grade pressed (never <see cref="FsrsSharp.Rating.Manual"/>).</param>
/// <param name="ElapsedDays">Days since the previous review (fractional for fractional-day chronos).</param>
/// <param name="Retrievability">Pre-computed retrievability at <paramref name="ElapsedDays"/>, if known.</param>
public readonly record struct ModelStepInput<TState>(
    TState? MemoryState,
    Rating Rating,
    double ElapsedDays,
    double? Retrievability = null) where TState : class;

/// <summary>One entry of a review history fed to <see cref="IModelCore{TState}.Forward"/>.</summary>
public readonly record struct ModelReview(Rating Rating, double DeltaT);

/// <summary>
/// Numeric bounds of a model's memory state, keyed with the upstream names (e.g. <c>sMin</c>,
/// <c>sMax</c>, <c>dMin</c>, <c>dMax</c> for FSRS-6; FSRS-7 adds <c>stabilityFastMin</c> and
/// <c>stabilityFastMax</c>).
/// </summary>
public sealed class ModelBounds(IReadOnlyDictionary<string, double> values) : IReadOnlyDictionary<string, double>
{
    public double this[string key] => values[key];
    public IEnumerable<string> Keys => values.Keys;
    public IEnumerable<double> Values => values.Values;
    public int Count => values.Count;
    public bool ContainsKey(string key) => values.ContainsKey(key);
    public bool TryGetValue(string key, out double value) => values.TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<string, double>> GetEnumerator() => values.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// A created, configured model instance (srs-kit <c>ModelCore</c>): pure memory-state math with no
/// knowledge of time representation or scheduling policy.
/// </summary>
/// <typeparam name="TState">
/// The memory state. Always an immutable reference type (a sealed record); the scheduler relies on
/// reference identity to map a stepped state back to the grade that produced it, like upstream.
/// </typeparam>
public interface IModelCore<TState> where TState : class
{
    /// <summary>The parsed model configuration (model-specific type, e.g. <c>Fsrs6Config</c>).</summary>
    object Config { get; }

    ModelBounds Bounds { get; }

    /// <summary>The underlying algorithm object (model-specific, e.g. <c>Fsrs6Algorithm</c>).</summary>
    object Algorithm { get; }

    TState Step(ModelStepInput<TState> input);

    /// <summary>The model's base interval in days for <paramref name="desiredRetention"/>.</summary>
    double NextInterval(TState memoryState, double desiredRetention);

    /// <summary>Probability of recall after <paramref name="elapsedDays"/>. May be NaN for degenerate states.</summary>
    double ForgettingCurve(TState memoryState, double elapsedDays);

    /// <summary>Replays <paramref name="history"/> and returns the state after each review.</summary>
    IReadOnlyList<TState> Forward(IReadOnlyList<ModelReview> history, TState? initialState = null);
}

/// <summary>Options for <see cref="IModel{TState}.Create"/> (srs-kit <c>ModelCreate</c> ctx flags).</summary>
/// <param name="Bypass">
/// The config was already parsed by the scheduler: skip validation, migration, clipping and checks.
/// </param>
public sealed record ModelCreateOptions(bool Migrate = true, bool Clip = true, bool Check = true, bool Bypass = false)
{
    public static ModelCreateOptions Default { get; } = new();
}

/// <summary>
/// A model definition (srs-kit <c>Model</c>): its name, config schema, memory-state schema, default
/// state, and a factory for configured <see cref="IModelCore{TState}"/> instances. Implementations
/// are stateless singletons (<c>Fsrs6Model.Instance</c>, ...).
/// </summary>
public interface IModel<TState> where TState : class
{
    string Name { get; }

    /// <summary>Config fields this model reads from the scheduler config (e.g. <c>weights</c>).</summary>
    IReadOnlyList<FieldKey> ConfigKeys { get; }

    /// <summary>
    /// Parses and normalizes the model part of a scheduler config: fills defaults, validates, and —
    /// where upstream's config schema does so — migrates/clips weights. Throws
    /// <see cref="FsrsValidationException"/> on invalid input. The result is what
    /// <see cref="Create"/> receives with <see cref="ModelCreateOptions.Bypass"/>.
    /// </summary>
    FieldBag ParseConfig(FieldBag config);

    /// <summary>Memory state of a brand-new card.</summary>
    TState DefaultMemoryState(FieldBag config);

    /// <summary>Returns <c>null</c> when <paramref name="state"/> is valid for this model, else an error.</summary>
    string? ValidateMemoryState(TState state);

    IModelCore<TState> Create(FieldBag config, ModelCreateOptions? options = null);
}
