namespace FsrsSharp.Kit;

// Engine implementation of Scheduler<TState,TTime> (port of srs-kit scheduler/base.ts + scheduler-schema.ts).
// STUB — to be implemented.
public sealed partial class Scheduler<TState, TTime>
{
    internal Scheduler(SchedulerDefinition<TState, TTime> definition, FieldBag config)
    {
        Definition = definition;
        Config = config;
        Model = definition.Model.Create(config, new ModelCreateOptions(Bypass: true));
        Chrono = definition.Chrono.Create(config);
    }

    public partial Card<TState> NewCard(TTime? now, FieldBag? input) => throw new NotImplementedException();
    public partial ScheduleResult<TState> Review(Card<TState> card, Rating grade, TTime? now) => throw new NotImplementedException();
    public partial SchedulePreview<TState> Preview(Card<TState> card, TTime? now) => throw new NotImplementedException();
    public partial Card<TState> Rollback(Card<TState> card, ReviewLog<TState> revlog) => throw new NotImplementedException();
    public partial Card<TState> Forget(Card<TState> card, TTime? now) => throw new NotImplementedException();
    public partial double NextInterval(TState memoryState, double desiredRetention, NextIntervalQuery<TState> context) => throw new NotImplementedException();
    public partial IReadOnlyList<ScheduleResult<TState>> Forward(IReadOnlyList<SchedulerReview<TTime>> history, Card<TState>? initialCard) => throw new NotImplementedException();
}
