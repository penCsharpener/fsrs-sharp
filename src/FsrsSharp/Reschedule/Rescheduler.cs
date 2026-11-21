using FsrsSharp.Kit;

namespace FsrsSharp.Reschedule;

/// <summary>
/// Replays a review history on top of an existing scheduler (upstream <c>Reschedule</c> class; renamed to avoid
/// clashing with the namespace). The scheduler couples model and chrono, so review-time and memory-state types
/// come from it.
/// </summary>
public sealed class Rescheduler<TState, TTime>(Scheduler<TState, TTime> scheduler)
    where TState : class
    where TTime : struct
{
    /// <summary>The scheduler the history is replayed on.</summary>
    public Scheduler<TState, TTime> Scheduler { get; } = scheduler ?? throw new ArgumentNullException(nameof(scheduler));

    /// <summary>Rebuilds model memory from the review history and returns the memory state after each review.</summary>
    /// <exception cref="FsrsValidationException">
    /// A rating or time is invalid, there is no non-manual review, or review times do not produce finite
    /// non-negative intervals.
    /// </exception>
    public ReplayResult<TState> Replay(ReplayInput<TState, TTime> input, RescheduleOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reviews = PrepareReviews(input.History, options ?? new RescheduleOptions());
        var memoryStates = Scheduler.Model.Forward(ToModelHistory(reviews), input.InitialState);
        return new ReplayResult<TState>(memoryStates[^1], memoryStates);
    }

    /// <summary>Replays the review history through the scheduler and returns the card and revlog produced by each review.</summary>
    /// <exception cref="FsrsValidationException">A rating or time is invalid, or there is no non-manual review.</exception>
    public RescheduleResult<TState> Reschedule(RescheduleInput<TState, TTime> input, RescheduleOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reviews = PrepareReviews(input.History, options ?? new RescheduleOptions());
        var results = Scheduler.Forward(
            [.. reviews.Select(review => new SchedulerReview<TTime>(review.Rating, review.ReviewTime))],
            input.InitialCard);
        return new RescheduleResult<TState>(results, results[^1].Card);
    }

    private List<RescheduleReview<TTime>> PrepareReviews(IReadOnlyList<RescheduleReview<TTime>> history, RescheduleOptions options)
    {
        ArgumentNullException.ThrowIfNull(history);
        List<RescheduleReview<TTime>> reviews = [];

        foreach (var review in history)
        {
            if (!Enum.IsDefined(review.Rating)) throw new FsrsValidationException("Expected rating");
            if (review.Rating == Rating.Manual) continue;
            if (KitSchemas.ValidateTime(review.ReviewTime) is { } error) throw new FsrsValidationException(error);
            reviews.Add(review);
        }

        if (reviews.Count == 0)
            throw new FsrsValidationException("Reschedule requires at least one non-manual review");

        if (options.SortHistory)
        {
            var chrono = Scheduler.Chrono;
            // OrderBy is stable, like Array.prototype.sort.
            reviews = [.. reviews.OrderBy(review => review.ReviewTime, Comparer<TTime>.Create(chrono.Compare))];
        }

        return reviews;
    }

    private List<ModelReview> ToModelHistory(List<RescheduleReview<TTime>> history)
    {
        var modelHistory = new List<ModelReview>(history.Count) { new(history[0].Rating, 0) };

        for (var i = 1; i < history.Count; i++)
        {
            var deltaT = Scheduler.Chrono.Difference(history[i - 1].ReviewTime, history[i].ReviewTime);
            if (!double.IsFinite(deltaT) || deltaT < 0)
                throw new FsrsValidationException("Review times must produce finite non-negative intervals");
            modelHistory.Add(new ModelReview(history[i].Rating, deltaT));
        }

        return modelHistory;
    }
}
