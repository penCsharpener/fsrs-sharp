namespace FsrsSharp.Kit;

/// <summary>Desired retention per grade (srs-kit <c>DesiredRetentionByGrade</c>). Interval policies may change entries.</summary>
public sealed class DesiredRetentionByGrade
{
    private readonly double[] _values;

    public DesiredRetentionByGrade(double all) => _values = [all, all, all, all];

    public double this[Rating grade]
    {
        get => _values[Index(grade)];
        set => _values[Index(grade)] = value;
    }

    private static int Index(Rating grade) => (int)Grades.Validate(grade) - 1;
}

/// <summary>
/// The per-review candidate shared by all middlewares (srs-kit <c>ReviewCandidateContext</c>):
/// memoised model steps per grade and a replaceable interval function. Middlewares that adjust
/// intervals (learning steps, fuzzing, ...) wrap <see cref="NextInterval"/>; they may stash private
/// per-review state in <see cref="Items"/> (upstream uses symbol-keyed properties for this).
/// </summary>
public sealed class ReviewCandidate<TState> where TState : class
{
    public ReviewCandidate(
        DesiredRetentionByGrade desiredRetention,
        Func<Rating, TState> step,
        Func<TState, Rating?> findGrade,
        Func<TState, double, double> nextInterval)
    {
        DesiredRetention = desiredRetention;
        Step = step;
        FindGrade = findGrade;
        NextInterval = nextInterval;
    }

    public DesiredRetentionByGrade DesiredRetention { get; }

    /// <summary>Post-rating memory state for a grade (memoised; the same instance is returned per grade).</summary>
    public Func<Rating, TState> Step { get; }

    /// <summary>The grade most recently stepped into the given memory state (by reference), if any.</summary>
    public Func<TState, Rating?> FindGrade { get; }

    /// <summary>Interval in days for a post-rating state and retention. Replace to layer a policy.</summary>
    public Func<TState, double, double> NextInterval { get; set; }

    /// <summary>Middleware-private per-review storage. Key by a private static object.</summary>
    public Dictionary<object, object?> Items { get; } = [];
}

/// <summary>Context passed to <see cref="Middleware.NextInterval{TState,TTime}"/> (srs-kit <c>NextIntervalMiddlewareContext</c>).</summary>
public class NextIntervalContext<TState, TTime>
    where TState : class
    where TTime : struct
{
    public NextIntervalContext(
        Scheduler<TState, TTime> instance,
        Card<TState> card,
        Rating grade,
        double? desiredRetention,
        double elapsedDays,
        ReviewCandidate<TState> candidate)
    {
        Instance = instance;
        Card = card;
        Grade = grade;
        DesiredRetention = desiredRetention;
        ElapsedDays = elapsedDays;
        Candidate = candidate;
    }

    public Scheduler<TState, TTime> Instance { get; }
    public FieldBag Config => Instance.Config;

    /// <summary>The (parsed) input card, before the review.</summary>
    public Card<TState> Card { get; }
    public Rating Grade { get; }

    /// <summary>Explicit retention supplied to a standalone interval query; <c>null</c> during review.</summary>
    public double? DesiredRetention { get; }

    public double ElapsedDays { get; }

    /// <summary>The interval being decided. Set it to override; the terminal step fills it if still null.</summary>
    public double? ScheduledDays { get; set; }

    public ReviewCandidate<TState> Candidate { get; }
}

/// <summary>Context passed to <see cref="Middleware.Review{TState,TTime}"/>. Also flows through <see cref="Middleware.NextInterval{TState,TTime}"/> during a review.</summary>
public sealed class ReviewContext<TState, TTime> : NextIntervalContext<TState, TTime>
    where TState : class
    where TTime : struct
{
    public ReviewContext(
        Scheduler<TState, TTime> instance,
        Card<TState> card,
        Rating grade,
        TTime now,
        double elapsedDays,
        double retrievability,
        ReviewCandidate<TState> candidate)
        : base(instance, card, grade, null, elapsedDays, candidate)
    {
        Now = now;
        Retrievability = retrievability;
    }

    public TTime Now { get; }

    /// <summary>Raw forgetting-curve value for the input card at <see cref="NextIntervalContext{TState,TTime}.ElapsedDays"/>. May be NaN.</summary>
    public double Retrievability { get; }

    public CardDraft<TState> ResultCard { get; } = new();
    public RevlogDraft<TState> ResultRevlog { get; } = new();
}

/// <summary>Context passed to <see cref="Middleware.Rollback{TState,TTime}"/>.</summary>
public sealed class RollbackContext<TState, TTime>(Scheduler<TState, TTime> instance, Card<TState> card, ReviewLog<TState> revlog)
    where TState : class
    where TTime : struct
{
    public Scheduler<TState, TTime> Instance { get; } = instance;
    public FieldBag Config => Instance.Config;
    public Card<TState> Card { get; } = card;
    public ReviewLog<TState> Revlog { get; } = revlog;
    public CardDraft<TState> ResultCard { get; } = new();
}

public enum CardDefaultOperation { NewCard, Forget }

/// <summary>Context for <see cref="Middleware.WriteCardDefaults{TState}"/> (srs-kit <c>MiddlewareDefaultValueContext</c>).</summary>
/// <param name="Operation">Whether a new card is created or an existing one is forgotten.</param>
/// <param name="Config">The parsed scheduler config.</param>
/// <param name="InitInput">For <see cref="CardDefaultOperation.NewCard"/>: the card init input fields.</param>
/// <param name="ExistingCard">For <see cref="CardDefaultOperation.Forget"/>: the card being reset.</param>
public sealed record CardDefaultContext<TState>(
    CardDefaultOperation Operation,
    FieldBag Config,
    FieldBag InitInput,
    Card<TState>? ExistingCard) where TState : class;

/// <summary>
/// A scheduling policy plugged into a <see cref="SchedulerDefinition{TState,TTime}"/> with
/// <c>.Use(...)</c> (srs-kit <c>Middleware</c>). Middlewares are model- and chrono-agnostic
/// stateless singletons: they declare the config and card/revlog fields they own and override any
/// of the onion-style handlers. Each handler must call <c>next()</c> exactly once; code before it
/// runs on the way in, code after it on the way out (apply overrides on unwind).
/// </summary>
/// <remarks>
/// During a review the pipeline is <c>[Review₀, NextInterval₀, Review₁, NextInterval₁, ...]</c>,
/// so each middleware's interval policy runs inside its own review handler, at the same position.
/// Standalone interval queries run only the <c>NextInterval</c> handlers.
/// </remarks>
public abstract class Middleware
{
    /// <summary>Unique name (upstream uses symbols like <c>ts-fsrs.learning-steps</c>).</summary>
    public abstract string Name { get; }

    /// <summary>Extra schedule statuses this middleware may assign.</summary>
    public virtual IReadOnlyList<ScheduleStatus> ScheduleStatuses => [];

    public virtual IReadOnlyList<FieldKey> ConfigKeys => [];

    /// <summary>Fields accepted as input to <c>NewCard</c>.</summary>
    public virtual IReadOnlyList<FieldKey> CardInitInputKeys => [];

    public virtual IReadOnlyList<FieldKey> CardFields => [];
    public virtual IReadOnlyList<FieldKey> RevlogFields => [];

    /// <summary>
    /// Cross-field config validation/normalization after every key was parsed individually.
    /// Return <c>null</c> when valid, else an error message.
    /// </summary>
    public virtual string? ValidateConfig(FieldBag config) => null;

    /// <summary>Writes this middleware's card fields for a new or forgotten card.</summary>
    public virtual void WriteCardDefaults<TState>(CardDefaultContext<TState> context, FieldBag.Builder card)
        where TState : class
    {
    }

    public virtual void NextInterval<TState, TTime>(NextIntervalContext<TState, TTime> context, Action next)
        where TState : class
        where TTime : struct => next();

    public virtual void Review<TState, TTime>(ReviewContext<TState, TTime> context, Action next)
        where TState : class
        where TTime : struct => next();

    public virtual void Rollback<TState, TTime>(RollbackContext<TState, TTime> context, Action next)
        where TState : class
        where TTime : struct => next();

    public override string ToString() => Name;
}
