using FsrsSharp.Kit;

namespace FsrsSharp.Middlewares;

/// <summary>
/// Review/lapse counters (srs-kit <c>middleware/stats</c>). Declares the card fields <c>reps</c> and
/// <c>lapses</c> and the config key <c>clearStatsOnForget</c> (default <c>true</c>: forgetting an
/// existing card resets the counters; <c>false</c> carries them over).
/// </summary>
public sealed class StatsMiddleware : Middleware
{

    public static FieldKey<int> Reps { get; } = new("reps", v => v >= 0 ? null : "Expected non-negative integer reps");
    public static FieldKey<int> Lapses { get; } = new("lapses", v => v >= 0 ? null : "Expected non-negative integer lapses");
    public static FieldKey<bool> ClearStatsOnForget { get; } = new("clearStatsOnForget", true);

    public static StatsMiddleware Instance { get; } = new();

    private StatsMiddleware() { }

    public override string Name => "srs-kit.stats";
    public override IReadOnlyList<FieldKey> ConfigKeys { get; } = [ClearStatsOnForget];
    public override IReadOnlyList<FieldKey> CardFields { get; } = [Reps, Lapses];

    public override void WriteCardDefaults<TState>(CardDefaultContext<TState> context, FieldBag.Builder card)
    {
        if (context.Operation == CardDefaultOperation.Forget
            && !context.Config.Get(ClearStatsOnForget)
            && context.ExistingCard is { } existing)
        {
            card.Set(Reps, existing.Get(Reps));
            card.Set(Lapses, existing.Get(Lapses));
            return;
        }

        card.Set(Reps, 0);
        card.Set(Lapses, 0);
    }

    public override void Review<TState, TTime>(ReviewContext<TState, TTime> context, Action next)
    {
        next();
        var card = context.Card;
        var isLapse = context.Grade == Rating.Again && card.State == State.Review;
        context.ResultCard.Set(Reps, card.Get(Reps) + 1);
        context.ResultCard.Set(Lapses, isLapse ? card.Get(Lapses) + 1 : card.Get(Lapses));
    }

    public override void Rollback<TState, TTime>(RollbackContext<TState, TTime> context, Action next)
    {
        next();
        var card = context.Card;
        var revlog = context.Revlog;
        var isLapse = revlog.Rating == Rating.Again && revlog.State == State.Review;
        context.ResultCard.Set(Reps, Math.Max(0, card.Get(Reps) - 1));
        context.ResultCard.Set(Lapses, Math.Max(0, card.Get(Lapses) - (isLapse ? 1 : 0)));
    }
}

/// <summary>Typed access to the <see cref="StatsMiddleware"/> card fields.</summary>
public static class StatsCardExtensions
{
    extension<TState>(Card<TState> card) where TState : class
    {
        public int Reps => card.Get(StatsMiddleware.Reps);
        public int Lapses => card.Get(StatsMiddleware.Lapses);
    }
}
