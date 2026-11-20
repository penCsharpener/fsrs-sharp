namespace FsrsSharp;

/// <summary>A scheduler for one particular (card, now) pair: previews or applies a grade.</summary>
public interface IScheduler
{
    /// <summary>Computes the outcome of all four possible grades.</summary>
    RecordLog Preview();

    /// <summary>Computes the outcome of one grade.</summary>
    RecordLogItem Review(Rating grade);
}

/// <summary>
/// Ported from ts-fsrs's <c>abstract_scheduler.ts</c>. Handles the bookkeeping shared by
/// <see cref="BasicScheduler"/> (learning steps enabled) and <see cref="LongTermScheduler"/>
/// (learning steps disabled): elapsed-day tracking, memoized per-grade results, log building,
/// and the New/Learning-Relearning/Review state dispatch.
/// </summary>
public abstract class AbstractScheduler : IScheduler
{
    protected readonly Card Last;
    protected readonly FsrsAlgorithm Algorithm;
    protected readonly Dictionary<Rating, RecordLogItem> NextByGrade = new();
    protected int ElapsedDays;

    /// <summary>The card as of the start of this review (mutated in place as scheduling proceeds; safe to read from a seed strategy).</summary>
    public Card Current { get; }

    /// <summary>The instant this review is being scheduled at.</summary>
    public DateTime ReviewTime { get; }

    protected AbstractScheduler(Card card, DateTime now, FsrsAlgorithm algorithm, SeedStrategy? seedStrategy = null)
    {
        Algorithm = algorithm;
        Last = card.Clone();
        Current = card.Clone();
        ReviewTime = now;

        int interval = 0;
        if (Current.State != State.New && Current.LastReview is { } lastReview)
        {
            interval = Helpers.DateDiffInDays(lastReview, ReviewTime);
        }

        Current.LastReview = ReviewTime;
        ElapsedDays = interval;
        Current.ElapsedDays = interval; // pending removal upstream in a future FSRS version
        Current.Reps += 1;

        Algorithm.Seed = (seedStrategy ?? Strategies.DefaultSeed)(this);
    }

    protected static void CheckGrade(Rating grade)
    {
        if (grade is < Rating.Again or > Rating.Easy)
        {
            throw new FsrsValidationException($"Invalid grade \"{grade}\", expected 1-4");
        }
    }

    public RecordLog Preview() => new()
    {
        Again = Review(Rating.Again),
        Hard = Review(Rating.Hard),
        Good = Review(Rating.Good),
        Easy = Review(Rating.Easy),
    };

    public RecordLogItem Review(Rating grade)
    {
        CheckGrade(grade);
        return Last.State switch
        {
            State.New => NewState(grade),
            State.Learning or State.Relearning => LearningState(grade),
            State.Review => ReviewState(grade),
            _ => throw new FsrsValidationException($"Invalid state \"{Last.State}\""),
        };
    }

    protected abstract RecordLogItem NewState(Rating grade);

    protected abstract RecordLogItem LearningState(Rating grade);

    protected abstract RecordLogItem ReviewState(Rating grade);

    protected ReviewLog BuildLog(Rating rating) => new()
    {
        Rating = rating,
        State = Current.State,
        Due = Last.LastReview ?? Last.Due,
        Stability = Current.Stability,
        Difficulty = Current.Difficulty,
        ElapsedDays = ElapsedDays,
        LastElapsedDays = Last.ElapsedDays,
        ScheduledDays = Current.ScheduledDays,
        LearningSteps = Current.LearningSteps,
        Review = ReviewTime,
    };
}
