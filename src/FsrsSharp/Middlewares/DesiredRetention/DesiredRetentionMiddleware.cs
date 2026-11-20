using FsrsSharp.Kit;

namespace FsrsSharp.Middlewares;

/// <summary>
/// Injects the configured per-grade desired retention into every review candidate
/// (<c>ts-fsrs.desired-retention</c>). Config <c>desiredRetention</c> is either one number in (0, 1)
/// or a per-grade map with all four grades (normalized to a read-only map). A retention
/// passed explicitly to a standalone interval query wins.
/// </summary>
public sealed class DesiredRetentionMiddleware : Middleware
{
    public const string Message = "Expected desiredRetention in (0, 1)";
    private static readonly object InjectedKey = new();

    /// <summary>
    /// Config key <c>desiredRetention</c> (required). Input: a <see cref="double"/> in (0, 1) or a map of
    /// <see cref="Rating"/> to <see cref="double"/> (<see cref="IReadOnlyDictionary{TKey,TValue}"/>,
    /// <see cref="IDictionary{TKey,TValue}"/> or <see cref="DesiredRetentionByGrade"/>) that has all four
    /// grades. Stored value: a read-only map with all four grades.
    /// </summary>
    public static FieldKey<IReadOnlyDictionary<Rating, double>> DesiredRetention { get; } =
        new("desiredRetention", coerce: Coerce);

    public static DesiredRetentionMiddleware Instance { get; } = new();

    private DesiredRetentionMiddleware() { }

    public override string Name => "ts-fsrs.desired-retention";
    public override IReadOnlyList<FieldKey> ConfigKeys { get; } = [DesiredRetention];

    /// <summary>The normalized per-grade retention of <paramref name="config"/> as a mutable copy.</summary>
    public static DesiredRetentionByGrade Resolve(FieldBag config)
    {
        var map = config.Get(DesiredRetention);
        var result = new DesiredRetentionByGrade(0.5);
        foreach (var grade in Grades.All) result[grade] = map[grade];
        return result;
    }

    // Mirrors upstream desired-retention/schema.ts: every failure has the same message.
    private static IReadOnlyDictionary<Rating, double> Coerce(object? value)
    {
        double? Single(object? v) => v switch
        {
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            long l => l,
            short sh => sh,
            byte b => b,
            _ => null,
        };

        Func<Rating, double?>? lookup = value switch
        {
            DesiredRetentionByGrade byGrade => g => byGrade[g],
            IReadOnlyDictionary<Rating, double> map => g => map.TryGetValue(g, out var v) ? v : null,
            IDictionary<Rating, double> map => g => map.TryGetValue(g, out var v) ? v : null,
            _ => null,
        };

        var result = new Dictionary<Rating, double>(4);
        if (lookup is null)
        {
            if (Single(value) is not { } single || KitSchemas.ValidateDesiredRetention(single) is not null)
                throw new FsrsValidationException(Message);
            foreach (var grade in Grades.All) result[grade] = single;
        }
        else
        {
            foreach (var grade in Grades.All)
            {
                if (lookup(grade) is not { } v || KitSchemas.ValidateDesiredRetention(v) is not null)
                    throw new FsrsValidationException(Message);
                result[grade] = v;
            }
        }

        return result.AsReadOnly();
    }

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

/// <summary>Collection-initializer support: <c>new FieldBag.Builder { { DesiredRetentionMiddleware.DesiredRetention, 0.9 } }</c>.</summary>
public static class DesiredRetentionBuilderExtensions
{
    /// <summary>Adds a single desired retention for every grade (coerced to a per-grade map by the scheduler).</summary>
    public static void Add(this FieldBag.Builder builder, FieldKey<IReadOnlyDictionary<Rating, double>> key, double value) =>
        builder.AddRaw(key, value);

    /// <summary>Adds a per-grade desired retention (all four grades required).</summary>
    public static void Add(this FieldBag.Builder builder, FieldKey<IReadOnlyDictionary<Rating, double>> key, DesiredRetentionByGrade value) =>
        builder.AddRaw(key, value);
}
