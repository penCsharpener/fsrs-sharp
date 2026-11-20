using FsrsSharp.Kit;

namespace FsrsSharp.Middlewares;

/// <summary>Options for <see cref="FuzzingMiddleware.Create"/> (upstream <c>SchedulerFuzzingMiddlewareOptions</c>).</summary>
public sealed record FuzzingMiddlewareOptions
{
    /// <summary>Custom fuzz bands; defaults to <see cref="Fuzzing.FuzzRanges"/>.</summary>
    public IReadOnlyList<FuzzRange>? FuzzingRange { get; init; }

    /// <summary>Custom seeded rng; defaults to <see cref="Fuzzing.Fnv1aMulberry32Rng"/>.</summary>
    public FuzzingRng? Rng { get; init; }
}

/// <summary>
/// Deterministic interval fuzzing (ts-fsrs <c>schedulerFuzzingMiddleware</c>). Config: <c>enableFuzz</c>,
/// <c>maximumInterval</c> (both required) and the shared <c>fractionalDays</c>. Adds the card field
/// <c>cardId</c> (string or number; generated when <c>NewCard</c> gets none), the review-log field
/// <c>cardId</c> and tracks <c>reps</c> (shared with <see cref="StatsMiddleware"/>). The fuzz seed is the
/// card id followed by the review's rep count.
/// </summary>
public sealed class FuzzingMiddleware : Middleware
{
    private static readonly object DecoratorKey = new();

    public static FieldKey<bool> EnableFuzz { get; } = new("enableFuzz");

    /// <summary>Alias of <see cref="MaximumIntervalMiddleware.MaximumInterval"/> (one shared key).</summary>
    public static FieldKey<double> MaximumInterval => MaximumIntervalMiddleware.MaximumInterval;

    // Upstream's fuzzing schema requires maximumInterval (no default), unlike maximum-interval/monotonic;
    // the shared key's required twin enforces that.
    private static FieldKey<double> RequiredMaximumInterval => MaximumIntervalMiddleware.RequiredMaximumInterval;

    /// <summary>The <c>cardId</c> card field.</summary>
    public static FieldKey<object> CardIdField { get; } = new("cardId", v => CardId.IsValid(v) ? null : "Expected card cardId");

    /// <summary>The <c>cardId</c> card init input field.</summary>
    public static FieldKey<object> CardIdInput { get; } = new("cardId", v => CardId.IsValid(v) ? null : "Expected new card cardId");

    /// <summary>The <c>cardId</c> review-log field.</summary>
    public static FieldKey<object> CardIdRevlog { get; } = new("cardId", v => CardId.IsValid(v) ? null : "Expected revlog cardId");

    /// <summary>The default middleware (default fuzz ranges and rng).</summary>
    public static FuzzingMiddleware Instance { get; } = new(new FuzzingMiddlewareOptions());

    private readonly IReadOnlyList<FuzzRange> _fuzzingRange;
    private readonly FuzzingRng _rng;

    private FuzzingMiddleware(FuzzingMiddlewareOptions options)
    {
        _fuzzingRange = options.FuzzingRange ?? Fuzzing.FuzzRanges;
        _rng = options.Rng ?? Fuzzing.Fnv1aMulberry32Rng;
    }

    /// <summary>Creates a fuzzing middleware with a custom range and/or rng.</summary>
    public static FuzzingMiddleware Create(FuzzingMiddlewareOptions? options = null) => new(options ?? new FuzzingMiddlewareOptions());

    public override string Name => "ts-fsrs.fuzzing";
    public override IReadOnlyList<FieldKey> ConfigKeys { get; } = [EnableFuzz, RequiredMaximumInterval, KitSchemas.FractionalDays];
    public override IReadOnlyList<FieldKey> CardInitInputKeys { get; } = [CardIdInput];
    public override IReadOnlyList<FieldKey> CardFields { get; } = [CardIdField, StatsMiddleware.Reps];
    public override IReadOnlyList<FieldKey> RevlogFields { get; } = [CardIdRevlog];

    public override void WriteCardDefaults<TState>(CardDefaultContext<TState> context, FieldBag.Builder card)
    {
        if (context.Operation == CardDefaultOperation.Forget && context.ExistingCard is { } existing)
        {
            card.Set(CardIdField, existing.Get(CardIdField));
            card.Set(StatsMiddleware.Reps, context.Config.Get(StatsMiddleware.ClearStatsOnForget) ? 0 : existing.Get(StatsMiddleware.Reps));
            return;
        }

        card.Set(CardIdField, context.InitInput.TryGet(CardIdInput, out var cardId) ? cardId : CardId.Create());
        card.Set(StatsMiddleware.Reps, 0);
    }

    public override void NextInterval<TState, TTime>(NextIntervalContext<TState, TTime> context, Action next)
    {
        var enabled = context.Config.Get(EnableFuzz);
        DecorateInterval(context, context.Card.Get(StatsMiddleware.Reps) + 1);
        // Resolve the decorated interval after downstream overrides.
        if (enabled) context.ScheduledDays = null;
        next();
    }

    public override void Review<TState, TTime>(ReviewContext<TState, TTime> context, Action next)
    {
        var card = context.Card;
        var cardId = card.Get(CardIdField);
        var result = context.ResultCard;

        if (!context.Config.Get(EnableFuzz))
        {
            next();
            result.Set(CardIdField, cardId);
            if (!result.Fields.Contains(StatsMiddleware.Reps)) result.Set(StatsMiddleware.Reps, card.Get(StatsMiddleware.Reps) + 1);
            context.ResultRevlog.Set(CardIdRevlog, cardId);
            return;
        }

        var reps = result.Fields.TryGet(StatsMiddleware.Reps, out var assigned) ? assigned : card.Get(StatsMiddleware.Reps) + 1;
        DecorateInterval(context, reps);
        next();
        result.Set(CardIdField, cardId);
        if (!result.Fields.Contains(StatsMiddleware.Reps)) result.Set(StatsMiddleware.Reps, reps);
        context.ResultRevlog.Set(CardIdRevlog, cardId);
    }

    public override void Rollback<TState, TTime>(RollbackContext<TState, TTime> context, Action next)
    {
        next();
        context.ResultCard.Set(CardIdField, context.Revlog.Get(CardIdRevlog));
        if (!context.ResultCard.Fields.Contains(StatsMiddleware.Reps))
            context.ResultCard.Set(StatsMiddleware.Reps, Math.Max(0, context.Card.Get(StatsMiddleware.Reps) - 1));
    }

    private void DecorateInterval<TState, TTime>(NextIntervalContext<TState, TTime> context, int reps)
        where TState : class
        where TTime : struct
    {
        if (!context.Config.Get(EnableFuzz)) return;
        var candidate = context.Candidate;
        if (candidate.Items.ContainsKey(DecoratorKey)) return;

        var nextInterval = candidate.NextInterval;
        var seed = $"{CardId.ToSeedString(context.Card.Get(CardIdField))}{reps}";
        var fuzzingRange = _fuzzingRange;
        var rng = _rng;
        candidate.NextInterval = (memoryState, desiredRetention) =>
        {
            var interval = nextInterval(memoryState, desiredRetention);
            if (interval < 2.5)
                return context.Config.Get(KitSchemas.FractionalDays) ? interval : Helpers.JsRound(interval);

            var (minInterval, maxInterval) = Fuzzing.GetFuzzRange(
                interval,
                context.ElapsedDays,
                context.Config.Get(MaximumInterval),
                fuzzingRange);
            var fuzzFactor = rng(seed)();
            return Math.Floor(fuzzFactor * (maxInterval - minInterval + 1) + minInterval);
        };
        candidate.Items[DecoratorKey] = true;
    }
}

/// <summary>Typed access to the <see cref="FuzzingMiddleware"/> card and review-log fields. The id is a <see cref="string"/> or a number.</summary>
public static class FuzzingCardExtensions
{
    extension<TState>(Card<TState> card) where TState : class
    {
        public object CardId => card.Get(FuzzingMiddleware.CardIdField);
    }

    extension<TState>(ReviewLog<TState> revlog) where TState : class
    {
        public object CardId => revlog.Get(FuzzingMiddleware.CardIdRevlog);
    }
}
