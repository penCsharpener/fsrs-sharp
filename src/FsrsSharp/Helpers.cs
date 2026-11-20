namespace FsrsSharp;

/// <summary>
/// Small numeric/date helpers ported from ts-fsrs's <c>help.ts</c>.
/// </summary>
public static class Helpers
{
    public static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);

    /// <summary>
    /// Rounds like JavaScript's <c>Math.round</c> (half rounds toward +Infinity),
    /// rather than .NET's default banker's rounding or MidpointRounding.AwayFromZero —
    /// values here are never negative in practice, but this keeps behavior identical.
    /// </summary>
    public static double JsRound(double x) => Math.Floor(x + 0.5);

    public static double RoundTo(double num, int decimals)
    {
        var factor = Math.Pow(10, decimals);
        return JsRound(num * factor) / factor;
    }

    /// <summary>Adds <paramref name="t"/> minutes (or days, if <paramref name="isDay"/>) to <paramref name="now"/>.</summary>
    public static DateTime DateScheduler(DateTime now, double t, bool isDay = false) =>
        isDay ? now.AddDays(t) : now.AddMinutes(t);

    public enum DiffUnit { Days, Minutes }

    /// <summary>Whole units of time between two instants, floored (matches ts-fsrs's <c>date_diff</c>).</summary>
    public static long DateDiff(DateTime now, DateTime pre, DiffUnit unit)
    {
        var diffMs = (now - pre).TotalMilliseconds;
        return unit switch
        {
            DiffUnit.Days => (long)Math.Floor(diffMs / (24 * 60 * 60 * 1000)),
            DiffUnit.Minutes => (long)Math.Floor(diffMs / (60 * 1000)),
            _ => throw new ArgumentOutOfRangeException(nameof(unit)),
        };
    }

    /// <summary>
    /// Calendar-day difference between two instants (ignores time-of-day and any
    /// offset, comparing Y/M/D directly) — used to compute a card's elapsed days.
    /// </summary>
    public static int DateDiffInDays(DateTime last, DateTime cur)
    {
        var utc1 = new DateTime(last.Year, last.Month, last.Day, 0, 0, 0, DateTimeKind.Utc);
        var utc2 = new DateTime(cur.Year, cur.Month, cur.Day, 0, 0, 0, DateTimeKind.Utc);
        return (int)Math.Floor((utc2 - utc1).TotalDays);
    }

    private static readonly (double Start, double End, double Factor)[] FuzzRanges =
    {
        (2.5, 7.0, 0.15),
        (7.0, 20.0, 0.1),
        (20.0, double.PositiveInfinity, 0.05),
    };

    public static (int MinIvl, int MaxIvl) GetFuzzRange(double interval, double elapsedDays, int maximumInterval)
    {
        var delta = 1.0;
        foreach (var range in FuzzRanges)
        {
            delta += range.Factor * Math.Max(Math.Min(interval, range.End) - range.Start, 0.0);
        }

        interval = Math.Min(interval, maximumInterval);
        var minIvl = Math.Max(2, (int)JsRound(interval - delta));
        var maxIvl = Math.Min((int)JsRound(interval + delta), maximumInterval);
        if (interval > elapsedDays)
        {
            minIvl = Math.Max(minIvl, (int)elapsedDays + 1);
        }

        minIvl = Math.Min(minIvl, maxIvl);
        return (minIvl, maxIvl);
    }

    public static readonly Rating[] Grades = { Rating.Again, Rating.Hard, Rating.Good, Rating.Easy };
}
