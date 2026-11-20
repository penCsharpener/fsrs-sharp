namespace FsrsSharp;

/// <summary>
/// One entry in a card's review history, as replayed by <see cref="Fsrs.Reschedule"/>.
/// For a normal graded review, set <see cref="Rating"/> (Again/Hard/Good/Easy) and
/// <see cref="Review"/>. For a manually-edited entry (e.g. imported history where the
/// resulting state/due date is already known rather than derived), set
/// <see cref="Rating"/> to <see cref="FsrsSharp.Rating.Manual"/> and also supply
/// <see cref="Due"/> and <see cref="State"/>.
/// </summary>
public sealed class FsrsHistoryEntry
{
    public required Rating Rating { get; init; }
    public required DateTime Review { get; init; }

    /// <summary>Required when <see cref="Rating"/> is <see cref="FsrsSharp.Rating.Manual"/>.</summary>
    public DateTime? Due { get; init; }

    /// <summary>Required when <see cref="Rating"/> is <see cref="FsrsSharp.Rating.Manual"/>.</summary>
    public State? State { get; init; }

    public double? Stability { get; init; }
    public double? Difficulty { get; init; }
}

/// <summary>Options controlling <see cref="Fsrs.Reschedule"/>.</summary>
public sealed class RescheduleOptions
{
    /// <summary>Optional comparer to sort the review history before replaying it.</summary>
    public Comparison<FsrsHistoryEntry>? ReviewsOrderBy { get; init; }

    /// <summary>Skip any <see cref="Rating.Manual"/> entries in the input history. Default true.</summary>
    public bool SkipManual { get; init; } = true;

    /// <summary>
    /// If true, the final "reschedule" item (aligning <paramref name="currentCard"/>'s stored
    /// due date with what replaying the history produced) also adopts the replayed
    /// stability/difficulty; otherwise only the due date/state/scheduled_days are aligned.
    /// </summary>
    public bool UpdateMemoryState { get; init; }

    /// <summary>The current date and time; defaults to <see cref="DateTime.UtcNow"/>.</summary>
    public DateTime? Now { get; init; }

    /// <summary>Overrides the starting (empty) card used as the seed of the replay.</summary>
    public Card? FirstCard { get; init; }
}

/// <summary>Result of <see cref="Fsrs.Reschedule"/>: the replayed history, plus (if needed) one final alignment entry.</summary>
public sealed record RescheduleResult(IReadOnlyList<RecordLogItem> Collections, RecordLogItem? RescheduleItem);

/// <summary>
/// Ported from ts-fsrs's <c>reschedule.ts</c>. Replays a card's full review history from
/// scratch through the current FSRS parameters — useful when parameters have been
/// re-optimized and existing cards' schedules should be recomputed to match, without
/// re-entering every review by hand.
/// </summary>
internal sealed class ReschedulerCore
{
    private readonly Fsrs _fsrs;

    public ReschedulerCore(Fsrs fsrs) => _fsrs = fsrs;

    private RecordLogItem Replay(Card card, DateTime reviewed, Rating rating) => _fsrs.Next(card, reviewed, rating);

    private RecordLogItem HandleManualRating(Card card, State state, DateTime reviewed, int elapsedDays, double? stability, double? difficulty, DateTime? due)
    {
        ReviewLog log;
        Card nextCard;
        if (state == FsrsSharp.State.New)
        {
            log = new ReviewLog
            {
                Rating = Rating.Manual,
                State = state,
                Due = due ?? reviewed,
                Stability = card.Stability,
                Difficulty = card.Difficulty,
                ElapsedDays = elapsedDays,
                LastElapsedDays = card.ElapsedDays,
                ScheduledDays = card.ScheduledDays,
                LearningSteps = card.LearningSteps,
                Review = reviewed,
            };
            nextCard = Defaults.CreateEmptyCard(reviewed);
            nextCard.LastReview = reviewed;
        }
        else
        {
            if (due is null)
            {
                throw new FsrsValidationException("reschedule: due is required for manual rating");
            }

            var scheduledDays = (int)Helpers.DateDiff(due.Value, reviewed, Helpers.DiffUnit.Days);
            log = new ReviewLog
            {
                Rating = Rating.Manual,
                State = card.State,
                Due = card.LastReview ?? card.Due,
                Stability = card.Stability,
                Difficulty = card.Difficulty,
                ElapsedDays = elapsedDays,
                LastElapsedDays = card.ElapsedDays,
                ScheduledDays = card.ScheduledDays,
                LearningSteps = card.LearningSteps,
                Review = reviewed,
            };
            nextCard = card.Clone();
            nextCard.State = state;
            nextCard.Due = due.Value;
            nextCard.LastReview = reviewed;
            nextCard.Stability = stability ?? card.Stability;
            nextCard.Difficulty = difficulty ?? card.Difficulty;
            nextCard.ElapsedDays = elapsedDays;
            nextCard.ScheduledDays = scheduledDays;
            nextCard.Reps = card.Reps + 1;
        }

        return new RecordLogItem(nextCard, log);
    }

    public List<RecordLogItem> Reschedule(Card currentCard, IReadOnlyList<FsrsHistoryEntry> reviews)
    {
        var collections = new List<RecordLogItem>();
        var curCard = Defaults.CreateEmptyCard(currentCard.Due);
        foreach (var review in reviews)
        {
            RecordLogItem item;
            if (review.Rating == Rating.Manual)
            {
                var interval = 0;
                if (curCard.State != FsrsSharp.State.New && curCard.LastReview is { } lastReview)
                {
                    interval = (int)Helpers.DateDiff(review.Review, lastReview, Helpers.DiffUnit.Days);
                }

                item = HandleManualRating(
                    curCard,
                    review.State ?? throw new FsrsValidationException("reschedule: state is required for manual rating"),
                    review.Review,
                    interval,
                    review.Stability,
                    review.Difficulty,
                    review.Due);
            }
            else
            {
                item = Replay(curCard, review.Review, review.Rating);
            }

            collections.Add(item);
            curCard = item.Card;
        }

        return collections;
    }

    public RecordLogItem? CalculateManualRecord(Card currentCard, DateTime now, RecordLogItem? recordLogItem, bool updateMemory)
    {
        if (recordLogItem is not { } item) return null;

        var rescheduleCard = item.Card;
        var log = item.Log;
        var curCard = currentCard.Clone();
        if (curCard.Due == rescheduleCard.Due) return null;

        curCard.ScheduledDays = (int)Helpers.DateDiff(rescheduleCard.Due, curCard.Due, Helpers.DiffUnit.Days);
        return HandleManualRating(
            curCard,
            rescheduleCard.State,
            now,
            log.ElapsedDays,
            updateMemory ? rescheduleCard.Stability : null,
            updateMemory ? rescheduleCard.Difficulty : null,
            rescheduleCard.Due);
    }
}
