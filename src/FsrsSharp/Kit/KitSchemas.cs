using System.Runtime.CompilerServices;

namespace FsrsSharp.Kit;

/// <summary>
/// Shared field keys and validators of srs-kit's <c>schema/</c> folder (<c>fractional-days.ts</c>,
/// <c>field.ts</c>): the <c>fractionalDays</c> config key and the finite/range checks the scheduler
/// applies to elapsed days, scheduled days and desired retention.
/// </summary>
public static class KitSchemas
{
    /// <summary>Config key <c>fractionalDays</c> (default <c>false</c>): elapsed time instead of calendar-day differences.</summary>
    public static FieldKey<bool> FractionalDays { get; } = new("fractionalDays", false);

    public const string DesiredRetentionMessage = "desiredRetention must be finite and in (0, 1)";
    public const string ElapsedDaysMessage = "elapsedDays must be finite and non-negative";
    public const string ScheduledDaysMessage = "scheduledDays must be finite and non-negative";
    public const string FiniteNumberMessage = "Expected finite number";
    public const string ValidDateMessage = "Expected valid Date";

    /// <summary>Returns <c>null</c> when <paramref name="value"/> is finite and in (0, 1).</summary>
    public static string? ValidateDesiredRetention(double value) =>
        double.IsFinite(value) && value > 0 && value < 1 ? null : DesiredRetentionMessage;

    /// <summary>Returns <c>null</c> when <paramref name="value"/> is finite and non-negative.</summary>
    public static string? ValidateElapsedDays(double value) =>
        double.IsFinite(value) && value >= 0 ? null : ElapsedDaysMessage;

    /// <summary>Returns <c>null</c> when <paramref name="value"/> is finite and non-negative.</summary>
    public static string? ValidateScheduledDays(double value) =>
        double.IsFinite(value) && value >= 0 ? null : ScheduledDaysMessage;

    public static string? ValidateFiniteNumber(double value) => double.IsFinite(value) ? null : FiniteNumberMessage;

    /// <summary>Upstream's <c>dateSchema</c>: a date is valid unless it is the Unix epoch (<c>getTime() === 0</c>).</summary>
    public static string? ValidateDate(DateTime value) =>
        UtcTicks(value) == DateTime.UnixEpoch.Ticks ? ValidDateMessage : null;

    /// <summary>Ticks of <paramref name="value"/> in UTC: <see cref="DateTimeKind.Local"/> is converted, <see cref="DateTimeKind.Unspecified"/> is taken as UTC.</summary>
    internal static long UtcTicks(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime().Ticks : value.Ticks;

    public static double ParseDesiredRetention(double value) => Parse(value, ValidateDesiredRetention(value));
    public static double ParseElapsedDays(double value) => Parse(value, ValidateElapsedDays(value));
    public static double ParseScheduledDays(double value) => Parse(value, ValidateScheduledDays(value));

    /// <summary>Validates a chrono time value for the built-in time types (<see cref="DateTime"/>, <see cref="double"/>).</summary>
    internal static string? ValidateTime<TTime>(TTime time) where TTime : struct
    {
        if (typeof(TTime) == typeof(DateTime)) return ValidateDate(Unsafe.As<TTime, DateTime>(ref time));
        if (typeof(TTime) == typeof(double)) return ValidateFiniteNumber(Unsafe.As<TTime, double>(ref time));
        return null;
    }

    private static double Parse(double value, string? error) =>
        error is null ? value : throw new FsrsValidationException(error);
}

/// <summary>Onion-model middleware runner (srs-kit <c>schema/middleware.ts</c> <c>runMiddlewares</c>).</summary>
public static class MiddlewarePipeline
{
    /// <summary>
    /// Runs <paramref name="handlers"/> outermost first around <paramref name="terminal"/>. Each handler
    /// receives a <c>next</c> that continues inward; calling it twice throws.
    /// </summary>
    /// <exception cref="FsrsOperationException">A handler called <c>next()</c> more than once.</exception>
    public static void Run<TContext>(
        IReadOnlyList<Action<TContext, Action>?> handlers,
        TContext context,
        Action<TContext> terminal)
    {
        if (handlers.Count == 0)
        {
            terminal(context);
            return;
        }

        var index = -1;
        void Dispatch(int nextIndex)
        {
            if (nextIndex <= index) throw new FsrsOperationException("Middleware next() called multiple times");
            index = nextIndex;

            if (nextIndex >= handlers.Count)
            {
                terminal(context);
                return;
            }

            var handler = handlers[nextIndex];
            if (handler is null)
            {
                Dispatch(nextIndex + 1);
                return;
            }

            handler(context, () => Dispatch(nextIndex + 1));
        }

        Dispatch(0);
    }
}
