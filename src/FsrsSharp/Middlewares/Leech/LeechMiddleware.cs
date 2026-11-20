using FsrsSharp.Kit;

namespace FsrsSharp.Middlewares;

/// <summary>
/// Suspends a card once it lapses <c>leechThreshold</c> times (<c>ts-fsrs.leech</c>). A threshold of 0
/// (the default) disables suspension. Declares the card field <c>lapses</c> (shared with
/// <see cref="StatsMiddleware"/>) and the schedule status <c>suspended</c>. Register before
/// middlewares that write <c>scheduleStatus</c> after <c>next()</c>.
/// </summary>
public sealed class LeechMiddleware : Middleware
{
    public const int DefaultLeechThreshold = 0;

    public static ScheduleStatus Suspended { get; } = "suspended";

    /// <summary>Config key <c>leechThreshold</c>: a non-negative integer, default <see cref="DefaultLeechThreshold"/>.</summary>
    public static FieldKey<int> LeechThreshold { get; } =
        new("leechThreshold", DefaultLeechThreshold, v => v >= 0 ? null : "Expected non-negative integer leechThreshold");

    /// <summary>The <c>lapses</c> card field (same key as <see cref="StatsMiddleware.Lapses"/>).</summary>
    public static FieldKey<int> Lapses => StatsMiddleware.Lapses;

    public static LeechMiddleware Instance { get; } = new();

    private LeechMiddleware() { }

    public override string Name => "ts-fsrs.leech";
    public override IReadOnlyList<ScheduleStatus> ScheduleStatuses { get; } = [Suspended];
    public override IReadOnlyList<FieldKey> ConfigKeys { get; } = [LeechThreshold];
    public override IReadOnlyList<FieldKey> CardFields { get; } = [StatsMiddleware.Lapses];

    public override void WriteCardDefaults<TState>(CardDefaultContext<TState> context, FieldBag.Builder card)
    {
        var keep = context.Operation == CardDefaultOperation.Forget
            && context.Config.TryGet(StatsMiddleware.ClearStatsOnForget, out var clear) && !clear
            && context.ExistingCard is not null;
        card.Set(Lapses, keep ? context.ExistingCard!.Get(Lapses) : 0);
    }

    public override void Review<TState, TTime>(ReviewContext<TState, TTime> context, Action next)
    {
        next();
        var card = context.Card;
        var isLapse = card.State == State.Review && context.Grade == Rating.Again;

        if (!context.ResultCard.Fields.Contains(Lapses))
            context.ResultCard.Set(Lapses, isLapse ? card.Get(Lapses) + 1 : card.Get(Lapses));
        var lapses = context.ResultCard.Get(Lapses);
        var threshold = context.Config.Get(LeechThreshold);
        if (isLapse && threshold > 0 && lapses % threshold == 0)
            context.ResultCard.ScheduleStatus = Suspended;
    }

    public override void Rollback<TState, TTime>(RollbackContext<TState, TTime> context, Action next)
    {
        next();
        var revlog = context.Revlog;
        var isLapse = revlog.Rating == Rating.Again && revlog.State == State.Review;
        if (!context.ResultCard.Fields.Contains(Lapses))
            context.ResultCard.Set(Lapses, Math.Max(0, context.Card.Get(Lapses) - (isLapse ? 1 : 0)));
    }
}
