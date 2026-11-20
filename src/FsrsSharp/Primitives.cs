namespace FsrsSharp;

/// <summary>
/// The learning state of a card. Numeric values match ts-fsrs / py-fsrs so that
/// data can round-trip between implementations.
/// </summary>
public enum State
{
    New = 0,
    Learning = 1,
    Review = 2,
    Relearning = 3,
}

/// <summary>
/// The rating a learner gives a card during a review.
/// <see cref="Manual"/> is only used for manually-edited history entries (see
/// <see cref="Fsrs.Rollback"/> / <see cref="Fsrs.Forget"/>) and can never be
/// passed to <see cref="Fsrs.Next"/>.
/// </summary>
public enum Rating
{
    Manual = 0,
    Again = 1,
    Hard = 2,
    Good = 3,
    Easy = 4,
}

/// <summary>
/// Where a card sits in the scheduling pipeline (srs-kit <c>scheduleStatus</c>). The built-in values
/// are <see cref="New"/>, <see cref="Learning"/> and <see cref="Review"/>; middlewares may introduce
/// their own (declared via <see cref="Kit.Middleware.ScheduleStatuses"/>). Stored as its upstream
/// string so persisted data stays interchangeable with ts-fsrs.
/// </summary>
public readonly record struct ScheduleStatus(string Value)
{
    public static ScheduleStatus New { get; } = new("new");
    public static ScheduleStatus Learning { get; } = new("learning");
    public static ScheduleStatus Review { get; } = new("review");

    /// <summary>The three statuses every scheduler understands.</summary>
    public static IReadOnlyList<ScheduleStatus> BuiltIn { get; } = [New, Learning, Review];

    public static implicit operator ScheduleStatus(string value) => new(value);
    public override string ToString() => Value;
}

/// <summary>Helpers for the subset of <see cref="Rating"/> a learner can actually press (srs-kit <c>Grade</c>).</summary>
public static class Grades
{
    /// <summary>Again, Hard, Good, Easy — in that order.</summary>
    public static IReadOnlyList<Rating> All { get; } = [Rating.Again, Rating.Hard, Rating.Good, Rating.Easy];

    public static bool IsGrade(Rating rating) => rating is Rating.Again or Rating.Hard or Rating.Good or Rating.Easy;

    /// <summary>Throws <see cref="FsrsValidationException"/> ("Expected grade") unless <paramref name="rating"/> is a grade.</summary>
    public static Rating Validate(Rating rating) =>
        IsGrade(rating) ? rating : throw new FsrsValidationException("Expected grade");
}
