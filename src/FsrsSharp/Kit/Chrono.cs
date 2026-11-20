namespace FsrsSharp.Kit;

/// <summary>The previous review time (if any) and the current due time of a card or revlog.</summary>
public readonly record struct ChronoTimes<TTime>(TTime? Previous, TTime Current) where TTime : struct;

/// <summary>Time arithmetic for one time representation (srs-kit <c>ChronoCore</c>).</summary>
public interface IChronoCore<TTime> where TTime : struct
{
    TTime Now();
    int Compare(TTime left, TTime right);

    /// <summary>Days from <paramref name="from"/> to <paramref name="to"/> (whole calendar days unless fractional days are enabled).</summary>
    double Difference(TTime from, TTime to);

    TTime Add(TTime from, double days);
}

/// <summary>
/// A chrono definition (srs-kit <c>Chrono</c>): the time type, the card/revlog fields it owns
/// (e.g. <c>dueAt</c>, <c>lastReviewAt</c>, <c>reviewTime</c>), how to read times back out of them,
/// and a factory for <see cref="IChronoCore{TTime}"/>. A chrono may own no fields at all
/// (see <c>NumericChrono</c>).
/// </summary>
public interface IChrono<TTime> where TTime : struct
{
    IReadOnlyList<FieldKey> ConfigKeys { get; }
    IReadOnlyList<FieldKey> CardFields { get; }
    IReadOnlyList<FieldKey> RevlogFields { get; }

    /// <summary>Reads the card's previous/current times before a review at <paramref name="time"/>.</summary>
    ChronoTimes<TTime> NormalizeReview(FieldBag cardFields, TTime time);

    /// <summary>Reads the times recorded in a review log (used by rollback).</summary>
    ChronoTimes<TTime> NormalizeRollback(FieldBag revlogFields);

    /// <summary>Writes this chrono's card fields: due at <paramref name="time"/>, last reviewed at <paramref name="previous"/>.</summary>
    void WriteCardDefaults(FieldBag.Builder card, FieldBag config, TTime time, TTime? previous);

    /// <summary>
    /// Writes this chrono's revlog fields. <paramref name="time"/> is the card's due time before the
    /// review; <paramref name="previous"/> holds the card's previous review time and the review time.
    /// </summary>
    void WriteRevlogDefaults(FieldBag.Builder revlog, FieldBag config, TTime time, ChronoTimes<TTime> previous);

    IChronoCore<TTime> Create(FieldBag config);
}
