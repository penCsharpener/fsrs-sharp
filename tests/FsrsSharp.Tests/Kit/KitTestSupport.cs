using AwesomeAssertions;
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;

namespace FsrsSharp.Tests.Kit;

/// <summary>Port of srs-kit's <c>scheduler/scheduler.test.ts</c> fixtures.</summary>
public static class KitFixtures
{
    public static FieldBag Config => Sm2Model.DefaultConfig;

    public static SchedulerDefinition<Sm2State, double> NumericDefinition() =>
        SchedulerDefinition.Define(Sm2Model.Instance, NumericChrono.Instance);

    public static SchedulerDefinition<Sm2State, DateTime> DateDefinition() =>
        SchedulerDefinition.Define(Sm2Model.Instance, DateTimeChrono.Instance);

    public static Scheduler<Sm2State, double> NumericScheduler(params Middleware[] middlewares) =>
        NumericDefinition().Use(middlewares).Create(Config);

    public static FieldBag.Builder ConfigBuilder() => new FieldBag.Builder().AssignFrom(Config);

    public static Sm2Core Core(this Scheduler<Sm2State, double> scheduler) => (Sm2Core)scheduler.Model;
    public static Sm2Core Core(this Scheduler<Sm2State, DateTime> scheduler) => (Sm2Core)scheduler.Model;

    public static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0, int ms = 0) =>
        new(y, mo, d, h, mi, s, ms, DateTimeKind.Utc);
}

/// <summary>The <c>sourceMiddleware</c> fixture: config <c>source</c>, optional card input <c>source</c>, card field <c>source</c>, revlog field <c>audit</c>.</summary>
public sealed class SourceMiddleware : Middleware
{

    public static FieldKey<string> SourceConfig { get; } = new("source", v => v is null ? "Expected source config" : null);
    public static FieldKey<string> SourceInput { get; } = new("source", v => v is null ? "Expected source card init input" : null);
    public static FieldKey<string> SourceCard { get; } = new("source", v => v is null ? "Expected source card field" : null);
    public static FieldKey<string> Audit { get; } = new("audit", v => v is null ? "Expected audit revlog field" : null);

    public static SourceMiddleware Instance { get; } = new();

    public override string Name => "sourceMiddleware";
    public override IReadOnlyList<FieldKey> ConfigKeys { get; } = [SourceConfig];
    public override IReadOnlyList<FieldKey> CardInitInputKeys { get; } = [SourceInput];
    public override IReadOnlyList<FieldKey> CardFields { get; } = [SourceCard];
    public override IReadOnlyList<FieldKey> RevlogFields { get; } = [Audit];

    public override void WriteCardDefaults<TState>(CardDefaultContext<TState> context, FieldBag.Builder card)
    {
        var source = context.Operation == CardDefaultOperation.NewCard && context.InitInput.TryGet(SourceInput, out var input)
            ? input
            : context.Config.Get(SourceConfig);
        card.Set(SourceCard, source);
    }

    public override void Review<TState, TTime>(ReviewContext<TState, TTime> context, Action next)
    {
        next();
        context.ResultCard.Set(SourceCard, context.Config.Get(SourceConfig));
        context.ResultRevlog.Set(Audit, context.Config.Get(SourceConfig));
    }

    public override void Rollback<TState, TTime>(RollbackContext<TState, TTime> context, Action next)
    {
        next();
        context.ResultCard.Set(SourceCard, context.Config.Get(SourceConfig));
    }
}

/// <summary>A middleware with no policy of its own that only carries declared keys/statuses (upstream <c>statusMiddleware</c>).</summary>
public sealed class StatusMiddleware : Middleware
{
    public override string Name => "statusMiddleware";
    public override IReadOnlyList<ScheduleStatus> ScheduleStatuses { get; } = ["suspend", "buried"];
}

/// <summary>
/// A middleware whose handlers are supplied by the test (upstream <c>defineMiddleware({ handlers })</c>).
/// Handlers only fire for <typeparamref name="TTime"/> schedulers over <see cref="Sm2State"/>.
/// </summary>
public sealed class LambdaMiddleware<TTime>(string name) : Middleware where TTime : struct
{
    public Action<ReviewContext<Sm2State, TTime>, Action>? OnReview { get; init; }
    public Action<NextIntervalContext<Sm2State, TTime>, Action>? OnNextInterval { get; init; }
    public Action<RollbackContext<Sm2State, TTime>, Action>? OnRollback { get; init; }
    public Func<FieldBag, string?>? OnValidateConfig { get; init; }
    public IReadOnlyList<FieldKey> Config { get; init; } = [];
    public IReadOnlyList<FieldKey> Init { get; init; } = [];
    public IReadOnlyList<FieldKey> Card { get; init; } = [];
    public IReadOnlyList<FieldKey> Revlog { get; init; } = [];
    public Action<CardDefaultContext<Sm2State>, FieldBag.Builder>? OnCardDefaults { get; init; }

    public override string Name => name;
    public override IReadOnlyList<FieldKey> ConfigKeys => Config;
    public override IReadOnlyList<FieldKey> CardInitInputKeys => Init;
    public override IReadOnlyList<FieldKey> CardFields => Card;
    public override IReadOnlyList<FieldKey> RevlogFields => Revlog;
    public override string? ValidateConfig(FieldBag config) => OnValidateConfig?.Invoke(config);

    public override void WriteCardDefaults<TState>(CardDefaultContext<TState> context, FieldBag.Builder card)
    {
        if (OnCardDefaults is not null && context is CardDefaultContext<Sm2State> typed) OnCardDefaults(typed, card);
    }

    public override void Review<TState, TTime2>(ReviewContext<TState, TTime2> context, Action next)
    {
        if (OnReview is not null && context is ReviewContext<Sm2State, TTime> typed) OnReview(typed, next);
        else next();
    }

    public override void NextInterval<TState, TTime2>(NextIntervalContext<TState, TTime2> context, Action next)
    {
        if (OnNextInterval is not null && context is NextIntervalContext<Sm2State, TTime> typed) OnNextInterval(typed, next);
        else next();
    }

    public override void Rollback<TState, TTime2>(RollbackContext<TState, TTime2> context, Action next)
    {
        if (OnRollback is not null && context is RollbackContext<Sm2State, TTime> typed) OnRollback(typed, next);
        else next();
    }
}

/// <summary>A configurable chrono for engine tests (upstream <c>defineChrono</c> fixtures over plain numbers).</summary>
public sealed class TestChrono : IChrono<double>
{
    public IReadOnlyList<FieldKey> ConfigKeys { get; init; } = [];
    public IReadOnlyList<FieldKey> CardFields { get; init; } = [];
    public IReadOnlyList<FieldKey> RevlogFields { get; init; } = [];
    public Func<FieldBag, double, ChronoTimes<double>> OnNormalizeReview { get; init; } = (_, time) => new(0, time);
    public Func<FieldBag, ChronoTimes<double>> OnNormalizeRollback { get; init; } = _ => new(0, 0);
    public Action<FieldBag.Builder, FieldBag, double, double?>? OnWriteCardDefaults { get; init; }
    public Action<FieldBag.Builder, FieldBag, double, ChronoTimes<double>>? OnWriteRevlogDefaults { get; init; }
    public Func<double> OnNow { get; init; } = () => 0;

    public ChronoTimes<double> NormalizeReview(FieldBag cardFields, double time) => OnNormalizeReview(cardFields, time);
    public ChronoTimes<double> NormalizeRollback(FieldBag revlogFields) => OnNormalizeRollback(revlogFields);
    public void WriteCardDefaults(FieldBag.Builder card, FieldBag config, double time, double? previous) =>
        OnWriteCardDefaults?.Invoke(card, config, time, previous);
    public void WriteRevlogDefaults(FieldBag.Builder revlog, FieldBag config, double time, ChronoTimes<double> previous) =>
        OnWriteRevlogDefaults?.Invoke(revlog, config, time, previous);
    public IChronoCore<double> Create(FieldBag config) => new Core(OnNow);

    private sealed class Core(Func<double> now) : IChronoCore<double>
    {
        public double Now() => now();
        public int Compare(double left, double right) => left.CompareTo(right);
        public double Difference(double from, double to) => to - from;
        public double Add(double from, double days) => from + days;
    }
}

public static class FieldBagAssertions
{
    /// <summary><c>FieldBag</c> is a dictionary, so AwesomeAssertions offers no <c>Be</c>; compare with its value equality.</summary>
    public static void ShouldBeBag(this FieldBag actual, FieldBag expected) =>
        actual.Equals(expected).Should().BeTrue($"expected {expected} but was {actual}");
}
