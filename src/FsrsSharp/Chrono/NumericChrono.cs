using FsrsSharp.Kit;

namespace FsrsSharp.Chrono;

/// <summary>
/// Chrono over plain numbers, interpreted as days (srs-kit <c>chrono/presets/numeric</c>). Owns no
/// card, revlog or config fields: cards carry no due date, <c>now</c> defaults to <c>0</c> and the
/// previous review time is always <c>0</c>.
/// </summary>
public sealed class NumericChrono : IChrono<double>
{
    public static NumericChrono Instance { get; } = new();

    private NumericChrono() { }

    public IReadOnlyList<FieldKey> ConfigKeys { get; } = [];
    public IReadOnlyList<FieldKey> CardFields { get; } = [];
    public IReadOnlyList<FieldKey> RevlogFields { get; } = [];

    public ChronoTimes<double> NormalizeReview(FieldBag cardFields, double time) =>
        KitSchemas.ValidateFiniteNumber(time) is { } error
            ? throw new FsrsValidationException(error)
            : new ChronoTimes<double>(0, time);

    /// <summary>Upstream's numeric normalizer needs the review <c>time</c>, which a revlog does not carry; the scheduler never calls this (the chrono has no card fields).</summary>
    public ChronoTimes<double> NormalizeRollback(FieldBag revlogFields) =>
        throw new FsrsValidationException(KitSchemas.FiniteNumberMessage);

    public void WriteCardDefaults(FieldBag.Builder card, FieldBag config, double time, double? previous) { }

    public void WriteRevlogDefaults(FieldBag.Builder revlog, FieldBag config, double time, ChronoTimes<double> previous) { }

    public IChronoCore<double> Create(FieldBag config) => Core.Instance;

    private sealed class Core : IChronoCore<double>
    {
        public static Core Instance { get; } = new();
        public double Now() => 0;
        public int Compare(double left, double right) => left.CompareTo(right);
        public double Difference(double from, double to) => to - from;
        public double Add(double from, double days) => from + days;
    }
}
