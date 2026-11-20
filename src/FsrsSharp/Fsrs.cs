namespace FsrsSharp;

/// <summary>
/// The main entry point — a C# port of ts-fsrs's <c>FSRS</c> class (<c>fsrs.ts</c>).
/// Wraps <see cref="FsrsAlgorithm"/> with the scheduling operations you actually call
/// day to day: <see cref="Repeat"/>/<see cref="Next"/> to schedule a review,
/// <see cref="GetRetrievability(Card,System.Nullable{System.DateTime})"/>,
/// <see cref="Rollback"/>, <see cref="Forget"/>, and <see cref="Reschedule"/>.
/// </summary>
/// <example>
/// <code>
/// var f = new Fsrs();
/// var card = Defaults.CreateEmptyCard();
/// var now = DateTime.UtcNow;
/// var preview = f.Repeat(card, now);          // all four grades
/// var again = f.Next(card, now, Rating.Again); // just one
/// </code>
/// </example>
public class Fsrs : FsrsAlgorithm
{
    private SeedStrategy? _seedStrategy;
    private LearningStepsStrategy? _learningStepsStrategy;

    public Fsrs(FsrsParametersInput? parameters = null) : base(parameters)
    {
    }

    /// <summary>Overrides how fuzz seeds are derived; pass null to restore the default.</summary>
    public Fsrs UseSeedStrategy(SeedStrategy? strategy)
    {
        _seedStrategy = strategy;
        return this;
    }

    /// <summary>Overrides how (re)learning step wait times are computed; pass null to restore the default.</summary>
    public Fsrs UseLearningStepsStrategy(LearningStepsStrategy? strategy)
    {
        _learningStepsStrategy = strategy;
        return this;
    }

    private IScheduler GetScheduler(Card card, DateTime now) => Parameters.EnableShortTerm
        ? new BasicScheduler(card, now, this, _seedStrategy, _learningStepsStrategy)
        : new LongTermScheduler(card, now, this, _seedStrategy);

    /// <summary>Previews the outcome of all four grades for reviewing <paramref name="card"/> at <paramref name="now"/>.</summary>
    public RecordLog Repeat(Card card, DateTime now) => GetScheduler(card, now).Preview();

    /// <summary>Reviews <paramref name="card"/> at <paramref name="now"/> with a specific <paramref name="grade"/>.</summary>
    /// <exception cref="FsrsValidationException"><paramref name="grade"/> is <see cref="Rating.Manual"/>.</exception>
    public RecordLogItem Next(Card card, DateTime now, Rating grade)
    {
        if (grade == Rating.Manual)
        {
            throw new FsrsValidationException("Cannot review a manual rating");
        }

        return GetScheduler(card, now).Review(grade);
    }

    /// <summary>The card's current probability of recall, as a fraction in [0,1].</summary>
    public double GetRetrievability(Card card, DateTime? now = null)
    {
        var at = now ?? DateTime.UtcNow;
        if (card.State == State.New) return 0;
        var t = Math.Max(Helpers.DateDiff(at, card.LastReview!.Value, Helpers.DiffUnit.Days), 0);
        return ForgettingCurve(t, Helpers.RoundTo(card.Stability, 8));
    }

    /// <summary>The card's current probability of recall, formatted like "87.34%". Invariant culture, not
    /// the ambient one: ts-fsrs produces "90.25%" everywhere, and a server running under a comma-decimal
    /// locale must not start emitting "90,25%".</summary>
    public string GetRetrievabilityPercent(Card card, DateTime? now = null) =>
        (GetRetrievability(card, now) * 100).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// Reconstructs the card as it was immediately before the review recorded in
    /// <paramref name="log"/> was applied — the inverse of <see cref="Next"/>.
    /// </summary>
    /// <exception cref="FsrsValidationException"><paramref name="log"/>.Rating is <see cref="Rating.Manual"/>.</exception>
    public Card Rollback(Card card, ReviewLog log)
    {
        if (log.Rating == Rating.Manual)
        {
            throw new FsrsValidationException("Cannot rollback a manual rating");
        }

        DateTime lastDue;
        DateTime? lastReview;
        int lastLapses;
        switch (log.State)
        {
            case State.New:
                lastDue = log.Due;
                lastReview = null;
                lastLapses = 0;
                break;
            case State.Learning:
            case State.Relearning:
            case State.Review:
                lastDue = log.Review;
                lastReview = log.Due;
                lastLapses = card.Lapses - (log.Rating == Rating.Again && log.State == State.Review ? 1 : 0);
                break;
            default:
                throw new FsrsValidationException($"Invalid state \"{log.State}\"");
        }

        var prev = card.Clone();
        prev.Due = lastDue;
        prev.Stability = log.Stability;
        prev.Difficulty = log.Difficulty;
        prev.ElapsedDays = log.LastElapsedDays;
        prev.ScheduledDays = log.ScheduledDays;
        prev.Reps = Math.Max(0, card.Reps - 1);
        prev.Lapses = Math.Max(0, lastLapses);
        prev.LearningSteps = log.LearningSteps;
        prev.State = log.State;
        prev.LastReview = lastReview;
        return prev;
    }

    /// <summary>
    /// Resets <paramref name="card"/> to <see cref="State.New"/> as of <paramref name="now"/>
    /// (e.g. the learner said "I don't remember this at all, start over").
    /// </summary>
    /// <param name="resetCount">If true, also zeroes Reps and Lapses.</param>
    public RecordLogItem Forget(Card card, DateTime now, bool resetCount = false)
    {
        var scheduledDays = card.State == State.New ? 0 : (int)Helpers.DateDiff(now, card.Due, Helpers.DiffUnit.Days);
        var log = new ReviewLog
        {
            Rating = Rating.Manual,
            State = card.State,
            Due = card.Due,
            Stability = card.Stability,
            Difficulty = card.Difficulty,
            ElapsedDays = 0,
            LastElapsedDays = card.ElapsedDays,
            ScheduledDays = scheduledDays,
            LearningSteps = card.LearningSteps,
            Review = now,
        };

        var forgotten = card.Clone();
        forgotten.Due = now;
        forgotten.Stability = 0;
        forgotten.Difficulty = 0;
        forgotten.ElapsedDays = 0;
        forgotten.ScheduledDays = 0;
        forgotten.Reps = resetCount ? 0 : card.Reps;
        forgotten.Lapses = resetCount ? 0 : card.Lapses;
        forgotten.LearningSteps = 0;
        forgotten.State = State.New;

        return new RecordLogItem(forgotten, log);
    }

    /// <summary>
    /// Replays <paramref name="reviews"/> from an empty card through the current parameters,
    /// producing what each review's (card, log) would look like today — useful for
    /// re-scheduling existing cards after re-optimizing parameters. See
    /// <see cref="RescheduleOptions"/> for options, and <see cref="RescheduleResult"/> for
    /// what's returned.
    /// </summary>
    public RescheduleResult Reschedule(Card currentCard, IReadOnlyList<FsrsHistoryEntry>? reviews = null, RescheduleOptions? options = null)
    {
        reviews ??= Array.Empty<FsrsHistoryEntry>();
        options ??= new RescheduleOptions();

        var ordered = reviews.ToList();
        if (options.ReviewsOrderBy is { } cmp)
        {
            ordered.Sort(cmp);
        }

        if (options.SkipManual)
        {
            ordered = ordered.Where(r => r.Rating != Rating.Manual).ToList();
        }

        var core = new ReschedulerCore(this);
        var collections = core.Reschedule(options.FirstCard ?? Defaults.CreateEmptyCard(), ordered);
        var manualItem = core.CalculateManualRecord(
            currentCard,
            options.Now ?? DateTime.UtcNow,
            collections.Count > 0 ? collections[^1] : null,
            options.UpdateMemoryState);

        return new RescheduleResult(collections, manualItem);
    }
}
