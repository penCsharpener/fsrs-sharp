using FsrsSharp.Kit;

namespace FsrsSharp.Middlewares;

/// <summary>
/// Caps the scheduled interval at <c>maximumInterval</c> days (<c>ts-fsrs.maximum-interval</c>).
/// Register after learning steps so explicit steps can keep their exact delay.
/// </summary>
public sealed class MaximumIntervalMiddleware : Middleware
{
    /// <summary>Upstream <c>DEFAULT_MAXIMUM_INTERVAL</c> (monotonic-interval/schema.ts).</summary>
    public const int DefaultMaximumInterval = 36_500;

    /// <summary>Config key <c>maximumInterval</c>: a positive integer, default <see cref="DefaultMaximumInterval"/>.</summary>
    public static FieldKey<int> MaximumInterval { get; } =
        new("maximumInterval", DefaultMaximumInterval, v => v > 0 ? null : "Expected positive integer maximumInterval");

    public static MaximumIntervalMiddleware Instance { get; } = new();

    private MaximumIntervalMiddleware() { }

    public override string Name => "ts-fsrs.maximum-interval";
    public override IReadOnlyList<FieldKey> ConfigKeys { get; } = [MaximumInterval];

    public override void NextInterval<TState, TTime>(NextIntervalContext<TState, TTime> context, Action next)
    {
        next();
        if (context.ScheduledDays is { } days)
            context.ScheduledDays = Math.Min(days, context.Config.Get(MaximumInterval));
    }
}
