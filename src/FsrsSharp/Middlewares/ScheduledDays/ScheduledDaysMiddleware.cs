using FsrsSharp.Kit;

namespace FsrsSharp.Middlewares;

/// <summary>
/// Records the final scheduled days on the card and the previous value on the review log
/// (<c>ts-fsrs.scheduled-days</c>). Config: <c>fractionalDays</c>. Register before interval
/// middlewares so it records their final value on unwind.
/// </summary>
public sealed class ScheduledDaysMiddleware : Middleware
{
    public static FieldKey<double> ScheduledDays { get; } =
        new("scheduledDays", v => KitSchemas.ValidateScheduledDays(v));

    public static ScheduledDaysMiddleware Instance { get; } = new();

    private ScheduledDaysMiddleware() { }

    public override string Name => "ts-fsrs.scheduled-days";
    public override IReadOnlyList<FieldKey> ConfigKeys { get; } = [KitSchemas.FractionalDays];
    public override IReadOnlyList<FieldKey> CardFields { get; } = [ScheduledDays];
    public override IReadOnlyList<FieldKey> RevlogFields { get; } = [ScheduledDays];

    public override void WriteCardDefaults<TState>(CardDefaultContext<TState> context, FieldBag.Builder card) =>
        card.Set(ScheduledDays, 0d);

    public override void Review<TState, TTime>(ReviewContext<TState, TTime> context, Action next)
    {
        var previousScheduledDays = context.Card.Get(ScheduledDays);
        next();

        var days = Math.Max(0, context.ScheduledDays ?? 0);
        context.ResultCard.Set(ScheduledDays, context.Config.Get(KitSchemas.FractionalDays) ? days : Math.Floor(days));
        context.ResultRevlog.Set(ScheduledDays, previousScheduledDays);
    }

    public override void Rollback<TState, TTime>(RollbackContext<TState, TTime> context, Action next)
    {
        next();
        context.ResultCard.Set(ScheduledDays, context.Revlog.Get(ScheduledDays));
    }
}

/// <summary>Typed access to the <c>scheduledDays</c> field of cards and review logs.</summary>
public static class ScheduledDaysExtensions
{
    extension<TState>(Card<TState> card) where TState : class
    {
        public double ScheduledDays => card.Get(ScheduledDaysMiddleware.ScheduledDays);
    }

    extension<TState>(ReviewLog<TState> revlog) where TState : class
    {
        public double ScheduledDays => revlog.Get(ScheduledDaysMiddleware.ScheduledDays);
    }
}
