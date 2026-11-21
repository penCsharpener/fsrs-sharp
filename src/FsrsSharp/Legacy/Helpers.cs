using System.Globalization;
using FsrsSharp.Chrono;
using FsrsSharp.Middlewares;

namespace FsrsSharp.Legacy;

/// <summary>
/// Date helpers ported from ts-fsrs's <c>legacy/help.ts</c> (named <c>LegacyHelpers</c> so it cannot be
/// confused with <see cref="FsrsSharp.Helpers"/>, which holds <c>JsRound</c>). The <c>Date.prototype</c>
/// extensions and <c>clamp</c>/<c>roundTo</c>/<c>fixDate</c>/<c>fixState</c>/<c>fixRating</c> that v5 had are gone
/// upstream; use <see cref="FsrsMath"/> for the numeric ones.
/// </summary>
public static class LegacyHelpers
{
    private static readonly int[] TimeUnit = [60, 60, 24, 31, 12];
    private static readonly string[] TimeUnitFormat = ["second", "min", "hour", "day", "month", "year"];

    /// <summary>The four gradable ratings, in order (ts-fsrs's <c>Grades</c>).</summary>
    public static IReadOnlyList<Rating> Grades { get; } = Array.AsReadOnly(new[] { Rating.Again, Rating.Hard, Rating.Good, Rating.Easy });

    public enum DiffUnit { Days, Minutes }

    /// <summary>
    /// Adds <paramref name="t"/> minutes (or days, if <paramref name="isDay"/>) to <paramref name="now"/>, like
    /// <c>new Date(now.getTime() + t * ms)</c>: the millisecond offset is truncated to whole milliseconds.
    /// </summary>
    public static DateTime DateScheduler(DateTime now, double t, bool isDay = false)
    {
        var ms = DateTimeChrono.ToUnixMilliseconds(now) + t * (isDay ? 24 * 60 * 60 * 1000 : 60 * 1000);
        return DateTimeChrono.FromUnixMilliseconds((long)Math.Truncate(ms));
    }

    /// <summary>Whole units of time between two instants, floored (matches ts-fsrs's <c>date_diff</c>).</summary>
    public static long DateDiff(DateTime now, DateTime pre, DiffUnit unit)
    {
        var diffMs = (double)(DateTimeChrono.ToUnixMilliseconds(now) - DateTimeChrono.ToUnixMilliseconds(pre));
        return unit switch
        {
            DiffUnit.Days => (long)Math.Floor(diffMs / (24 * 60 * 60 * 1000)),
            DiffUnit.Minutes => (long)Math.Floor(diffMs / (60 * 1000)),
            _ => throw new ArgumentOutOfRangeException(nameof(unit)),
        };
    }

    /// <summary>Calendar-day difference of the UTC dates, ignoring the time of day (ts-fsrs's <c>dateDiffInDays</c>).</summary>
    public static int DateDiffInDays(DateTime last, DateTime cur) => (int)DateTimeChrono.DateDiffInDays(last, cur);

    /// <summary>Legacy shape of <see cref="Fuzzing.GetFuzzRange"/> (<c>get_fuzz_range</c>).</summary>
    public static (int MinIvl, int MaxIvl) GetFuzzRange(double interval, double elapsedDays, int maximumInterval)
    {
        var (min, max) = Fuzzing.GetFuzzRange(interval, elapsedDays, maximumInterval);
        return ((int)min, (int)max);
    }

    /// <summary><c>yyyy-MM-dd HH:mm:ss</c> in local time, like ts-fsrs's <c>formatDate</c> (which reads the local fields of a JS Date).</summary>
    public static string FormatDate(DateTime date)
    {
        var local = date.Kind == DateTimeKind.Utc ? date.ToLocalTime() : date;
        return local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>The distance between <paramref name="due"/> and <paramref name="lastReview"/> as a floored number in the largest fitting unit (ts-fsrs's <c>show_diff_message</c>).</summary>
    /// <param name="due">The later instant.</param>
    /// <param name="lastReview">The earlier instant.</param>
    /// <param name="unit">Append the unit name.</param>
    /// <param name="timeUnit">Six unit names (second, min, hour, day, month, year); any other length falls back to the default names.</param>
    public static string ShowDiffMessage(DateTime due, DateTime lastReview, bool unit = false, IReadOnlyList<string>? timeUnit = null)
    {
        if (timeUnit is null || timeUnit.Count != TimeUnitFormat.Length) timeUnit = TimeUnitFormat;
        var diff = (double)(DateTimeChrono.ToUnixMilliseconds(due) - DateTimeChrono.ToUnixMilliseconds(lastReview));
        diff /= 1000;
        int i;
        for (i = 0; i < TimeUnit.Length; i++)
        {
            if (diff < TimeUnit[i]) break;
            diff /= TimeUnit[i];
        }

        return Math.Floor(diff).ToString("0", CultureInfo.InvariantCulture) + (unit ? timeUnit[i] : "");
    }
}
