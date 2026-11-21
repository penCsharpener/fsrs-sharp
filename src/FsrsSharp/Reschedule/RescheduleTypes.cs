using FsrsSharp.Kit;

namespace FsrsSharp.Reschedule;

/// <summary>
/// A review the scheduler can replay, widened to accept <see cref="Rating.Manual"/> so manual entries can be
/// filtered out instead of rejected (upstream <c>RescheduleReview</c>).
/// </summary>
public readonly record struct RescheduleReview<TTime>(Rating Rating, TTime ReviewTime) where TTime : struct;

/// <summary>Input of <see cref="Rescheduler{TState,TTime}.Replay"/> (upstream <c>ReplayInput</c>).</summary>
/// <param name="History">The reviews to replay.</param>
/// <param name="InitialState">The memory state to start from; <c>null</c> starts from a never reviewed card.</param>
public sealed record ReplayInput<TState, TTime>(
    IReadOnlyList<RescheduleReview<TTime>> History,
    TState? InitialState = null)
    where TState : class
    where TTime : struct;

/// <summary>Input of <see cref="Rescheduler{TState,TTime}.Reschedule"/> (upstream <c>RescheduleInput</c>).</summary>
/// <param name="History">The reviews to replay.</param>
/// <param name="InitialCard">The card to start from; <c>null</c> starts from a new card at the first review time.</param>
public sealed record RescheduleInput<TState, TTime>(
    IReadOnlyList<RescheduleReview<TTime>> History,
    Card<TState>? InitialCard = null)
    where TState : class
    where TTime : struct;

/// <summary>Options shared by replay and reschedule (upstream <c>RescheduleOptions</c>).</summary>
public sealed record RescheduleOptions
{
    /// <summary>
    /// Sorts the review history by review time in ascending order before replaying it. Disable it when the
    /// history is already sorted. Defaults to <c>true</c>.
    /// </summary>
    public bool SortHistory { get; init; } = true;
}

/// <summary>Result of <see cref="Rescheduler{TState,TTime}.Replay"/> (upstream <c>ReplayResult</c>).</summary>
/// <param name="MemoryState">The memory state after the last review.</param>
/// <param name="MemoryStates">The memory state after each review.</param>
public sealed record ReplayResult<TState>(TState MemoryState, IReadOnlyList<TState> MemoryStates)
    where TState : class;

/// <summary>Result of <see cref="Rescheduler{TState,TTime}.Reschedule"/> (upstream <c>RescheduleResult</c>).</summary>
/// <param name="Results">The card and revlog produced by each review.</param>
/// <param name="Card">The card after the last review.</param>
public sealed record RescheduleResult<TState>(IReadOnlyList<ScheduleResult<TState>> Results, Card<TState> Card)
    where TState : class;
