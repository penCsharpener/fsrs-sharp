using FsrsSharp.Kit;

namespace FsrsSharp.Middlewares;

/// <summary>
/// Keeps the intervals of the four ratings monotonic (ts-fsrs <c>schedulerMonotonicIntervalMiddleware</c>).
/// Register after fuzzing and learning steps, when present. Config: <c>maximumInterval</c> (default 36500).
/// </summary>
public sealed class MonotonicIntervalMiddleware : Middleware
{
    public const double DefaultMaximumInterval = 36_500;

    internal const string MaximumIntervalMessage = "Expected positive integer maximumInterval";

    internal static string? ValidateMaximumInterval(double value) =>
        double.IsFinite(value) && value == Math.Floor(value) && value > 0 && value <= 9007199254740991.0
            ? null
            : MaximumIntervalMessage;

    // Keys must be declared before Instance (the instance initializers capture them).
    internal static FieldKey<double> MaximumInterval { get; } = new("maximumInterval", DefaultMaximumInterval, ValidateMaximumInterval);

    public static MonotonicIntervalMiddleware Instance { get; } = new();

    private MonotonicIntervalMiddleware() { }

    public override string Name => "ts-fsrs.monotonic-interval";
    public override IReadOnlyList<FieldKey> ConfigKeys { get; } = [MaximumInterval];

    public override void NextInterval<TState, TTime>(NextIntervalContext<TState, TTime> context, Action next)
    {
        var candidate = context.Candidate;
        var maximumInterval = context.Config.Get(MaximumInterval);

        double Interval(Rating rating) =>
            candidate.NextInterval(candidate.Step(rating), KitSchemas.ParseDesiredRetention(candidate.DesiredRetention[rating]));

        double Schedule(params double[] candidates) => MonotonicInterval.CalculateScheduleDay(candidates, maximumInterval);

        next();

        var scheduledDays = context.ScheduledDays
            ?? throw new FsrsOperationException("Expected scheduledDays after the interval was resolved");

        context.ScheduledDays = context.Grade switch
        {
            Rating.Again => Schedule(scheduledDays),
            Rating.Hard => Schedule(Interval(Rating.Again), scheduledDays),
            Rating.Good => Schedule(Interval(Rating.Again), Interval(Rating.Hard), scheduledDays),
            Rating.Easy => Schedule(Interval(Rating.Again), Interval(Rating.Hard), Interval(Rating.Good), scheduledDays),
            _ => context.ScheduledDays,
        };
    }
}
