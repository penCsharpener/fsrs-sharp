using FsrsSharp.Kit;

namespace FsrsSharp.Chrono;

/// <summary>
/// Chrono over UTC <see cref="DateTime"/> (srs-kit <c>chrono/presets/date</c>). Owns the card fields
/// <c>dueAt</c> and <c>lastReviewAt</c>, the revlog fields <c>dueAt</c>, <c>lastReviewAt</c> and
/// <c>reviewTime</c>, and the <c>fractionalDays</c> config key (default <c>false</c>: whole UTC
/// calendar days between reviews; <c>true</c>: elapsed 24-hour days).
/// </summary>
/// <remarks>
/// <see cref="DateTimeKind.Local"/> values are converted to UTC and <see cref="DateTimeKind.Unspecified"/>
/// is treated as UTC. <see cref="DateTime.UnixEpoch"/> is rejected like upstream's <c>new Date(0)</c>.
/// </remarks>
public sealed class DateTimeChrono : IChrono<DateTime>
{
    public const long MillisecondsPerDay = 86_400_000;
    private const string InvalidFields = "Expected valid Date fields";


    public static FieldKey<DateTime> DueAt { get; } = new("dueAt", ValidateField);
    public static FieldKey<DateTime?> LastReviewAt { get; } = new("lastReviewAt", null, ValidateOptionalField);
    public static FieldKey<DateTime> ReviewTime { get; } = new("reviewTime", ValidateField);

    public static DateTimeChrono Instance { get; } = new();

    private DateTimeChrono() { }

    public IReadOnlyList<FieldKey> ConfigKeys { get; } = [KitSchemas.FractionalDays];
    public IReadOnlyList<FieldKey> CardFields { get; } = [DueAt, LastReviewAt];
    public IReadOnlyList<FieldKey> RevlogFields { get; } = [DueAt, LastReviewAt, ReviewTime];

    private static string? ValidateField(DateTime value) => KitSchemas.ValidateDate(value) is null ? null : InvalidFields;
    private static string? ValidateOptionalField(DateTime? value) => value is { } v ? ValidateField(v) : null;

    public ChronoTimes<DateTime> NormalizeReview(FieldBag cardFields, DateTime time)
    {
        var card = ReadFields(cardFields, withReviewTime: false);
        if (KitSchemas.ValidateDate(time) is { } error) throw new FsrsValidationException(error);
        return new ChronoTimes<DateTime>(card.LastReviewAt, card.DueAt);
    }

    public ChronoTimes<DateTime> NormalizeRollback(FieldBag revlogFields)
    {
        var revlog = ReadFields(revlogFields, withReviewTime: true);
        return new ChronoTimes<DateTime>(revlog.LastReviewAt, revlog.DueAt);
    }

    private static (DateTime DueAt, DateTime? LastReviewAt) ReadFields(FieldBag fields, bool withReviewTime)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (!fields.TryGet(DueAt, out var dueAt) || ValidateField(dueAt) is not null) throw new FsrsValidationException(InvalidFields);
        DateTime? lastReviewAt = null;
        if (fields.TryGetValue(LastReviewAt.Name, out var raw))
        {
            if (raw is not null && (raw is not DateTime d || ValidateField(d) is not null)) throw new FsrsValidationException(InvalidFields);
            lastReviewAt = (DateTime?)raw;
        }

        if (withReviewTime && (!fields.TryGet(ReviewTime, out var reviewTime) || ValidateField(reviewTime) is not null))
            throw new FsrsValidationException(InvalidFields);
        return (dueAt, lastReviewAt);
    }

    public void WriteCardDefaults(FieldBag.Builder card, FieldBag config, DateTime time, DateTime? previous)
    {
        card.Set(DueAt, time);
        card.Set(LastReviewAt, previous);
    }

    public void WriteRevlogDefaults(FieldBag.Builder revlog, FieldBag config, DateTime time, ChronoTimes<DateTime> previous)
    {
        revlog.Set(DueAt, time);
        revlog.Set(LastReviewAt, previous.Previous);
        revlog.Set(ReviewTime, previous.Current);
    }

    public IChronoCore<DateTime> Create(FieldBag config) =>
        config.Get(KitSchemas.FractionalDays) ? ElapsedCore.Instance : CalendarCore.Instance;

    /// <summary>The Unix milliseconds of <paramref name="value"/> (UTC; sub-millisecond ticks truncated like a JS <c>Date</c>).</summary>
    internal static long ToUnixMilliseconds(DateTime value) =>
        Math.DivRem(KitSchemas.UtcTicks(value) - DateTime.UnixEpoch.Ticks, TimeSpan.TicksPerMillisecond, out _);

    /// <summary>UTC <see cref="DateTime"/> for <paramref name="unixMilliseconds"/>.</summary>
    internal static DateTime FromUnixMilliseconds(long unixMilliseconds) =>
        new(DateTime.UnixEpoch.Ticks + unixMilliseconds * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

    /// <summary>Calendar-day difference of the UTC dates (floor), ignoring the time of day.</summary>
    public static double DateDiffInDays(DateTime last, DateTime cur)
    {
        var d1 = FloorDay(ToUnixMilliseconds(last));
        var d2 = FloorDay(ToUnixMilliseconds(cur));
        return d2 - d1;
    }

    private static long FloorDay(long ms) => Math.DivRem(ms, MillisecondsPerDay, out var rem) - (rem < 0 ? 1 : 0);

    private abstract class CoreBase : IChronoCore<DateTime>
    {
        public DateTime Now() => FromUnixMilliseconds(ToUnixMilliseconds(DateTime.UtcNow));

        public int Compare(DateTime left, DateTime right) =>
            ToUnixMilliseconds(left).CompareTo(ToUnixMilliseconds(right));

        public abstract double Difference(DateTime from, DateTime to);

        /// <summary>Mirrors <c>new Date(from.getTime() + days * 86_400_000)</c>: the sum is truncated toward zero to whole milliseconds.</summary>
        public DateTime Add(DateTime from, double days)
        {
            var total = Math.Truncate(ToUnixMilliseconds(from) + days * MillisecondsPerDay);
            // JS clips to +-8.64e15 ms (and yields an Invalid Date outside); DateTime covers years 1..9999.
            var ticks = total * TimeSpan.TicksPerMillisecond + DateTime.UnixEpoch.Ticks;
            if (!double.IsFinite(total) || Math.Abs(total) > 8.64e15 || ticks < 0 || ticks > DateTime.MaxValue.Ticks)
                throw new FsrsValidationException(InvalidFields);
            return FromUnixMilliseconds((long)total);
        }
    }

    private sealed class ElapsedCore : CoreBase
    {
        public static ElapsedCore Instance { get; } = new();

        /// <summary>Elapsed UTC time in fixed 24-hour days, independent of calendar boundaries.</summary>
        public override double Difference(DateTime from, DateTime to) =>
            (double)(ToUnixMilliseconds(to) - ToUnixMilliseconds(from)) / MillisecondsPerDay;
    }

    private sealed class CalendarCore : CoreBase
    {
        public static CalendarCore Instance { get; } = new();
        public override double Difference(DateTime from, DateTime to) => DateDiffInDays(from, to);
    }
}
