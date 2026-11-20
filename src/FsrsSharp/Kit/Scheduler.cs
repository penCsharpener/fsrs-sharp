namespace FsrsSharp.Kit;

/// <summary>Entry point for building custom schedulers (ts-fsrs <c>defineScheduler</c>).</summary>
public static class SchedulerDefinition
{
    /// <summary>
    /// Starts a reusable scheduler definition from a model and a chrono. Add policies with
    /// <see cref="SchedulerDefinition{TState,TTime}.Use"/>, then call
    /// <see cref="SchedulerDefinition{TState,TTime}.Create"/> once per user/collection config.
    /// </summary>
    public static SchedulerDefinition<TState, TTime> Define<TState, TTime>(IModel<TState> model, IChrono<TTime> chrono)
        where TState : class
        where TTime : struct => new(model, chrono, []);
}

/// <summary>
/// An immutable, program-level scheduler preset: model + chrono + ordered middlewares.
/// </summary>
public sealed class SchedulerDefinition<TState, TTime>
    where TState : class
    where TTime : struct
{
    internal SchedulerDefinition(IModel<TState> model, IChrono<TTime> chrono, IReadOnlyList<Middleware> middlewares)
    {
        Model = model;
        Chrono = chrono;
        Middlewares = middlewares;
    }

    public IModel<TState> Model { get; }
    public IChrono<TTime> Chrono { get; }
    public IReadOnlyList<Middleware> Middlewares { get; }

    /// <summary>Returns a new definition with <paramref name="middlewares"/> appended (outermost first).</summary>
    public SchedulerDefinition<TState, TTime> Use(params ReadOnlySpan<Middleware> middlewares) =>
        new(Model, Chrono, [.. Middlewares, .. middlewares]);

    /// <summary>
    /// Validates <paramref name="config"/> against every declared config key (model, chrono,
    /// middlewares), fills defaults, and creates an independent scheduler.
    /// </summary>
    /// <exception cref="FsrsValidationException">The config is invalid.</exception>
    public Scheduler<TState, TTime> Create(FieldBag config) => new(this, config);

    /// <inheritdoc cref="Create(FieldBag)"/>
    public Scheduler<TState, TTime> Create(FieldBag.Builder config) => Create(config.ToFieldBag());
}

/// <summary>
/// A configured scheduler (srs-kit <c>BaseScheduler</c>). Thread-safe and immutable; every
/// operation validates its input card/revlog and returns new objects.
/// </summary>
public sealed partial class Scheduler<TState, TTime>
    where TState : class
    where TTime : struct
{
    public SchedulerDefinition<TState, TTime> Definition { get; }

    /// <summary>The parsed config (defaults filled in).</summary>
    public FieldBag Config { get; }

    public IModelCore<TState> Model { get; }
    public IChronoCore<TTime> Chrono { get; }

    /// <summary>Creates a new card due at <paramref name="now"/> (default: <see cref="IChronoCore{TTime}.Now"/>).</summary>
    /// <param name="input">Values for middleware <see cref="Middleware.CardInitInputKeys"/>.</param>
    public partial Card<TState> NewCard(TTime? now = null, FieldBag? input = null);

    /// <summary>Applies <paramref name="grade"/> to <paramref name="card"/> at <paramref name="now"/>.</summary>
    public partial ScheduleResult<TState> Review(Card<TState> card, Rating grade, TTime? now = null);

    /// <summary>What each grade would produce (lazy, cached per grade).</summary>
    public partial SchedulePreview<TState> Preview(Card<TState> card, TTime? now = null);

    /// <summary>Undoes the review recorded in <paramref name="revlog"/>.</summary>
    public partial Card<TState> Rollback(Card<TState> card, ReviewLog<TState> revlog);

    /// <summary>Resets a card to new (middlewares decide which stats survive).</summary>
    public partial Card<TState> Forget(Card<TState> card, TTime? now = null);

    /// <summary>
    /// Resolves the configured interval policy for an already computed post-rating state without
    /// advancing the card. Runs only <see cref="Middleware.NextInterval{TState1,TTime1}"/> handlers.
    /// </summary>
    public partial double NextInterval(TState memoryState, double desiredRetention, NextIntervalQuery<TState> context);

    /// <summary>Replays <paramref name="history"/> from <paramref name="initialCard"/> (default: a new card at the first review time).</summary>
    public partial IReadOnlyList<ScheduleResult<TState>> Forward(IReadOnlyList<SchedulerReview<TTime>> history, Card<TState>? initialCard = null);
}
