using FsrsSharp.Kit;

namespace FsrsSharp.Middlewares;

/// <summary>
/// Injects the configured per-grade desired retention into every review candidate
/// (<c>ts-fsrs.desired-retention</c>). Config <c>desiredRetention</c> is either one number in (0, 1)
/// or a per-grade <c>IReadOnlyDictionary&lt;Rating, double&gt;</c> with all four grades. A retention
/// passed explicitly to a standalone interval query wins.
/// </summary>
public sealed class DesiredRetentionMiddleware : Middleware
{
    public const string Message = "Expected desiredRetention in (0, 1)";
    private static readonly object InjectedKey = new();

    /// <summary>Config key <c>desiredRetention</c> (required): a <see cref="double"/> or an <see cref="IReadOnlyDictionary{TKey,TValue}"/> / <see cref="IDictionary{TKey,TValue}"/> of <see cref="Rating"/> to <see cref="double"/> (also <see cref="DesiredRetentionByGrade"/>).</summary>
    public static FieldKey<object> DesiredRetention { get; } = new("desiredRetention", v => TryResolve(v, out _) ? null : Message);

    public static DesiredRetentionMiddleware Instance { get; } = new();

    private DesiredRetentionMiddleware() { }

    public override string Name => "ts-fsrs.desired-retention";
    public override IReadOnlyList<FieldKey> ConfigKeys { get; } = [DesiredRetention];

    /// <summary>Normalizes the <c>desiredRetention</c> config value to one value per grade.</summary>
    /// <exception cref="FsrsValidationException">The value is invalid.</exception>
    public static DesiredRetentionByGrade Resolve(FieldBag config) =>
        TryResolve(config.Get(DesiredRetention), out var result) ? result : throw new FsrsValidationException(Message);

    private static bool TryResolve(object? value, out DesiredRetentionByGrade result)
    {
        result = null!;
        switch (value)
        {
            case DesiredRetentionByGrade byGrade:
                return FromLookup(g => byGrade[g], out result);
            case IReadOnlyDictionary<Rating, double> map:
                return FromLookup(g => map.TryGetValue(g, out var v) ? v : null, out result);
            case IDictionary<Rating, double> map:
                return FromLookup(g => map.TryGetValue(g, out var v) ? v : null, out result);
            case null:
            case string:
                return false;
            default:
                if (AsDouble(value) is { } single && IsValid(single))
                {
                    result = new DesiredRetentionByGrade(single);
                    return true;
                }
                return false;
        }
    }

    private static bool IsValid(double d) => KitSchemas.ValidateDesiredRetention(d) is null;

    private static bool FromLookup(Func<Rating, double?> get, out DesiredRetentionByGrade result)
    {
        result = new DesiredRetentionByGrade(0.5);
        foreach (var grade in Grades.All)
        {
            if (get(grade) is not { } v || !IsValid(v)) return false;
            result[grade] = v;
        }
        return true;
    }

    private static double? AsDouble(object value) => value switch
    {
        double d => d,
        float f => f,
        decimal m => (double)m,
        int i => i,
        long l => l,
        _ => null,
    };

    public override void NextInterval<TState, TTime>(NextIntervalContext<TState, TTime> context, Action next)
    {
        var candidate = context.Candidate;
        if (context.DesiredRetention is null && !candidate.Items.ContainsKey(InjectedKey))
        {
            var configured = Resolve(context.Config);
            foreach (var grade in Grades.All) candidate.DesiredRetention[grade] = configured[grade];
            candidate.Items[InjectedKey] = true;
        }
        next();
    }
}
