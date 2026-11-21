using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs6;

namespace FsrsSharp.Legacy;

/// <summary>
/// The main entry point — a C# port of ts-fsrs's <c>FSRS</c> class (<c>fsrs.ts</c>).
/// Wraps the FSRS-6 model with the scheduling operations you actually call
/// day to day: <see cref="Repeat"/>/<see cref="Next"/> to schedule a review,
/// <see cref="Retrievability(Card,System.Nullable{System.DateTime})"/>,
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
public class Fsrs
{
    private LearningStepsResolver? _learningStepsStrategy;
    private FsrsParameters _parameters = null!;
    private IModelCore<FsrsState> _model = null!;

    public Fsrs(FsrsParametersInput? parameters = null)
    {
        UpdateParameters(parameters ?? new FsrsParametersInput());
    }

    /// <summary>The FSRS-6 model built from the current parameters (ts-fsrs's <c>fsrs.model</c>).</summary>
    public IModelCore<FsrsState> Model => _model;

    /// <summary>
    /// The parameters in effect. Like ts-fsrs's <c>Proxy</c>-backed <c>parameters</c>, the instance is live:
    /// assigning <see cref="FsrsParameters.W"/> migrates and clips the weights and rebuilds <see cref="Model"/>,
    /// and assigning <see cref="FsrsParameters.EnableShortTerm"/> rebuilds it and switches the scheduler.
    /// </summary>
    public FsrsParameters Parameters => _parameters;

    /// <summary>
    /// Replaces the parameters with <paramref name="overrides"/> merged over the defaults (same semantics as
    /// the constructor; ts-fsrs's <c>fsrs.parameters = {...}</c>).
    /// </summary>
    public void UpdateParameters(FsrsParametersInput overrides)
    {
        var generated = Defaults.GeneratorParameters(overrides);
        generated.W = Fsrs6Parameters.Clip(generated.W, generated.RelearningSteps.Length, generated.EnableShortTerm);
        generated.Changed = RebuildModel;
        generated.Live = true;
        _parameters = generated;
        RebuildModel();
    }

    private void RebuildModel() =>
        _model = Fsrs6Model.Instance.Create(
            new Fsrs6Config(_parameters.W, _parameters.EnableShortTerm, _parameters.RelearningSteps.Length),
            new ModelCreateOptions(Bypass: true));

    /// <summary>The custom (re)learning steps resolver installed with <see cref="UseLearningStepsStrategy"/>, or null for the default.</summary>
    public LearningStepsResolver? LearningStepsStrategy => _learningStepsStrategy;

    /// <summary>Overrides how (re)learning step wait times are computed; pass null to restore the default.</summary>
    public Fsrs UseLearningStepsStrategy(LearningStepsResolver? strategy)
    {
        _learningStepsStrategy = strategy;
        return this;
    }

    private IScheduler GetScheduler(Card card, DateTime now) => _parameters.EnableShortTerm
        ? new BasicScheduler(card, now, _model, _parameters, _learningStepsStrategy)
        : new LongTermScheduler(card, now, _model, _parameters);

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

    /// <summary>The card's current probability of recall, as a fraction in [0,1] (0 for New cards).</summary>
    public double Retrievability(Card card, DateTime? now = null)
    {
        var at = now ?? DateTime.UtcNow;
        if (card.State == State.New) return 0;
        var t = Math.Max(LegacyHelpers.DateDiff(at, card.LastReview!.Value, LegacyHelpers.DiffUnit.Days), 0);
        return _model.ForgettingCurve(new FsrsState(card.Stability, card.Difficulty), t);
    }

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
    /// <param name="card">The card to reset.</param>
    /// <param name="now">When the reset happens.</param>
    /// <param name="resetCount">If true, also zeroes Reps and Lapses.</param>
    public RecordLogItem Forget(Card card, DateTime now, bool resetCount = false)
    {
        var scheduledDays = card.State == State.New ? 0 : (int)LegacyHelpers.DateDiff(now, card.Due, LegacyHelpers.DiffUnit.Days);
        var log = new ReviewLog
        {
            Rating = Rating.Manual,
            State = card.State,
            Due = card.Due,
            Stability = card.Stability,
            Difficulty = card.Difficulty,
            ScheduledDays = scheduledDays,
            LearningSteps = card.LearningSteps,
            Review = now,
        };

        var forgotten = card.Clone();
        forgotten.Due = now;
        forgotten.Stability = 0;
        forgotten.Difficulty = 0;
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
