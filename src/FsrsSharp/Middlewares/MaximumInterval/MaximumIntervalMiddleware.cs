using FsrsSharp.Kit;

namespace FsrsSharp.Middlewares;

/// <summary>
/// Caps the scheduled interval at <c>maximumInterval</c> days (<c>ts-fsrs.maximum-interval</c>).
/// Register after learning steps so explicit steps can keep their exact delay.
/// </summary>
public sealed class MaximumIntervalMiddleware : Middleware
{
    /// <summary>Upstream <c>DEFAULT_MAXIMUM_INTERVAL</c> (monotonic-interval/schema.ts).</summary>
    public const double DefaultMaximumInterval = 36_500;

    /// <summary>Upstream message for an invalid <c>maximumInterval</c>.</summary>
    public const string MaximumIntervalMessage = "Expected positive integer maximumInterval";

    /// <summary>Upstream: a positive safe integer (<c>Number.isSafeInteger(v) &amp;&amp; v &gt; 0</c>).</summary>
    internal static string? ValidateMaximumInterval(double value) =>
        double.IsFinite(value) && value == Math.Floor(value) && value > 0 && value <= 9007199254740991.0
            ? null
            : MaximumIntervalMessage;

    /// <summary>
    /// Config key <c>maximumInterval</c>, shared by the maximum-interval, monotonic-interval and
    /// fuzzing middlewares: a positive integer, default <see cref="DefaultMaximumInterval"/>
    /// (fuzzing additionally requires it to be present, see <see cref="FuzzingMiddleware"/>).
    /// </summary>
    public static FieldKey<double> MaximumInterval { get; } =
        new("maximumInterval", DefaultMaximumInterval, ValidateMaximumInterval, CoerceMaximumInterval);

    /// <summary>The same key without a default (fuzzing's schema requires the value).</summary>
    internal static FieldKey<double> RequiredMaximumInterval { get; } =
        new("maximumInterval", ValidateMaximumInterval, CoerceMaximumInterval);

    // Upstream: typeof value === 'number'; any CLR numeric is accepted here.
    private static double CoerceMaximumInterval(object? value) => value switch
    {
        double d => d,
        int i => i,
        long l => l,
        float f => f,
        short s => s,
        byte b => b,
        decimal m => (double)m,
        _ => throw new FsrsValidationException(MaximumIntervalMessage),
    };

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
