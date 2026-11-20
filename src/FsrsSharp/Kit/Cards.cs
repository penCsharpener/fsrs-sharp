namespace FsrsSharp.Kit;

/// <summary>
/// A card as seen by a <see cref="Scheduler{TState,TTime}"/>. The core fields every scheduler owns
/// are typed properties; everything contributed by the chrono (<c>dueAt</c>, ...) and middlewares
/// (<c>reps</c>, <c>lapses</c>, <c>learningStep</c>, ...) lives in <see cref="Fields"/> and is
/// surfaced through extension properties (<c>card.DueAt</c>, <c>card.Reps</c>, ...).
/// Immutable: every operation returns a new card.
/// </summary>
public sealed record Card<TState>(TState MemoryState, State State, ScheduleStatus ScheduleStatus, FieldBag Fields)
    where TState : class
{
    public T Get<T>(FieldKey<T> key) => Fields.Get(key);
    public Card<TState> With<T>(FieldKey<T> key, T value) => this with { Fields = Fields.With(key, value) };
}

/// <summary>
/// The review log written by <see cref="Scheduler{TState,TTime}.Review"/>. Records the card as it was
/// <em>before</em> the review (memory state, state, schedule status, chrono and middleware fields)
/// plus the <see cref="Rating"/> given, so that <see cref="Scheduler{TState,TTime}.Rollback"/> can
/// restore it.
/// </summary>
public sealed record ReviewLog<TState>(TState MemoryState, Rating Rating, State State, ScheduleStatus ScheduleStatus, FieldBag Fields)
    where TState : class
{
    public T Get<T>(FieldKey<T> key) => Fields.Get(key);
    public ReviewLog<TState> With<T>(FieldKey<T> key, T value) => this with { Fields = Fields.With(key, value) };
}

/// <summary>Mutable card under construction inside a middleware pipeline (upstream's result draft).</summary>
public sealed class CardDraft<TState> where TState : class
{
    public TState? MemoryState { get; set; }
    public State? State { get; set; }
    public ScheduleStatus? ScheduleStatus { get; set; }
    public FieldBag.Builder Fields { get; } = new();

    public T Get<T>(FieldKey<T> key) => Fields.Get(key);
    public void Set<T>(FieldKey<T> key, T value) => Fields.Set(key, value);
}

/// <summary>Mutable review log under construction inside a middleware pipeline.</summary>
public sealed class RevlogDraft<TState> where TState : class
{
    public TState? MemoryState { get; set; }
    public Rating? Rating { get; set; }
    public State? State { get; set; }
    public ScheduleStatus? ScheduleStatus { get; set; }
    public FieldBag.Builder Fields { get; } = new();

    public T Get<T>(FieldKey<T> key) => Fields.Get(key);
    public void Set<T>(FieldKey<T> key, T value) => Fields.Set(key, value);
}

/// <summary>The outcome of one review: the next card and the log to store.</summary>
public sealed record ScheduleResult<TState>(Card<TState> Card, ReviewLog<TState> Revlog) where TState : class;

/// <summary>One grade's outcome in a <see cref="SchedulePreview{TState}"/>.</summary>
public sealed record PreviewItem<TState>(Rating Grade, Card<TState> Card, ReviewLog<TState> Revlog) where TState : class;

/// <summary>A historic review replayed by <see cref="Scheduler{TState,TTime}.Forward"/>.</summary>
public readonly record struct SchedulerReview<TTime>(Rating Rating, TTime ReviewTime) where TTime : struct;

/// <summary>Context for a standalone <see cref="Scheduler{TState,TTime}.NextInterval"/> query.</summary>
/// <param name="Card">The card before the review.</param>
/// <param name="Grade">The grade that produced the post-rating memory state.</param>
/// <param name="ElapsedDays">Elapsed days for that review.</param>
public sealed record NextIntervalQuery<TState>(Card<TState> Card, Rating Grade, double ElapsedDays) where TState : class;

/// <summary>
/// Results for Again, Hard, Good and Easy (srs-kit's lazy <c>PreviewResult</c>). Each grade is
/// computed on first access and cached.
/// </summary>
public sealed class SchedulePreview<TState> : IReadOnlyList<PreviewItem<TState>> where TState : class
{
    private readonly Func<Rating, PreviewItem<TState>> _compute;
    private readonly PreviewItem<TState>?[] _cache = new PreviewItem<TState>?[4];

    internal SchedulePreview(Func<Rating, PreviewItem<TState>> compute) => _compute = compute;

    /// <summary>The result for <paramref name="grade"/>.</summary>
    public PreviewItem<TState> this[Rating grade]
    {
        get
        {
            Grades.Validate(grade);
            var i = (int)grade - 1;
            return _cache[i] ??= _compute(grade);
        }
    }

    /// <summary>Positional access: 0 = Again ... 3 = Easy.</summary>
    public PreviewItem<TState> this[int index] =>
        (uint)index < 4 ? this[(Rating)(index + 1)] : throw new ArgumentOutOfRangeException(nameof(index));

    public int Count => 4;

    public IEnumerator<PreviewItem<TState>> GetEnumerator()
    {
        foreach (var grade in Grades.All) yield return this[grade];
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
