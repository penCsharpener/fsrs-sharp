using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs6;
using FsrsSharp.Models.Fsrs7;

namespace FsrsSharp.Tests.Middlewares;

/// <summary>Scheduler fixtures shared by the middleware spec ports (upstream: <c>defineScheduler({ model: FSRS6Model, chrono: dateChrono })</c>).</summary>
internal static class MiddlewareTestSchedulers
{
    public static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime Later = new(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The 2022-12-29 12:30 reference time (UTC; upstream uses local time, which does not matter here).</summary>
    public static readonly DateTime LegacyNow = new(2022, 12, 29, 12, 30, 0, DateTimeKind.Utc);

    public static FieldBag.Builder Fsrs6Config(bool enableShortTerm = false, int numRelearningSteps = 0) => new()
    {
        { Fsrs6Model.Weights, Fsrs6Constants.DefaultWeights.ToArray() },
        { Fsrs6Model.EnableShortTerm, enableShortTerm },
        { Fsrs6Model.NumRelearningSteps, numRelearningSteps },
    };

    public static SchedulerDefinition<FsrsState, DateTime> Fsrs6(params Middleware[] middlewares) =>
        SchedulerDefinition.Define(Fsrs6Model.Instance, DateTimeChrono.Instance).Use(middlewares);

    public static SchedulerDefinition<FsrsState, DateTime> Fsrs6(IModel<FsrsState> model, params Middleware[] middlewares) =>
        SchedulerDefinition.Define(model, DateTimeChrono.Instance).Use(middlewares);

    public const double Day = 86_400_000;

    public static FieldBag.Builder Fsrs7Config() =>
        new FieldBag.Builder().Set(Fsrs7Model.Weights, Fsrs7Constants.DefaultWeights);

    public static SchedulerDefinition<FsrsState, DateTime> Fsrs6Definition(params Middleware[] middlewares) => Fsrs6(middlewares);

    public static SchedulerDefinition<Fsrs7State, DateTime> Fsrs7Definition(params Middleware[] middlewares) =>
        SchedulerDefinition.Define(Fsrs7Model.Instance, DateTimeChrono.Instance).Use(middlewares);

    public static double DueDays(DateTime dueAt, DateTime reviewTime) => (dueAt - reviewTime).TotalMilliseconds / Day;

    public static FieldBag CardInput(object cardId) =>
        new FieldBag.Builder().Set(FsrsSharp.Middlewares.FuzzingMiddleware.CardIdInput, cardId).ToFieldBag();

    public static IReadOnlyList<Card<TState>> PreviewCards<TState>(Scheduler<TState, DateTime> scheduler, Card<TState> card, DateTime now)
        where TState : class =>
        [.. scheduler.Preview(card, now).Select(item => item.Card)];

    public static double[] PreviewDays<TState>(Scheduler<TState, DateTime> scheduler, Card<TState> card, DateTime now)
        where TState : class =>
        [.. scheduler.Preview(card, now).Select(item => DueDays(item.Card.DueAt, now))];

    public static double Millis(DateTime later, DateTime earlier) => (later - earlier).TotalMilliseconds;
}

/// <summary>
/// Wraps a model so tests can stub or observe its core's calls (upstream: <c>vi.spyOn(core.model, ...)</c>).
/// </summary>
internal sealed class SpyModel<TState>(IModel<TState> inner) : IModel<TState> where TState : class
{
    /// <summary>When set, replaces the model's interval (<c>mockReturnValue</c>).</summary>
    public Func<TState, double, double>? NextIntervalOverride { get; set; }

    /// <summary>Every <c>Step</c> call, in order.</summary>
    public List<ModelStepInput<TState>> Steps { get; } = [];

    /// <summary>Every <c>NextInterval</c> call (state and desired retention), in order.</summary>
    public List<(TState State, double Retention)> NextIntervals { get; } = [];

    /// <summary>The desired retentions of every <c>NextInterval</c> call, in order.</summary>
    public IReadOnlyList<double> Retentions => [.. NextIntervals.Select(c => c.Retention)];

    public int CallCount => NextIntervals.Count;
    public int ForgettingCurves { get; private set; }

    public void Clear()
    {
        Steps.Clear();
        NextIntervals.Clear();
        ForgettingCurves = 0;
    }

    public string Name => inner.Name;
    public IReadOnlyList<FieldKey> ConfigKeys => inner.ConfigKeys;
    public FieldBag ParseConfig(FieldBag config) => inner.ParseConfig(config);
    public TState DefaultMemoryState(FieldBag config) => inner.DefaultMemoryState(config);
    public string? ValidateMemoryState(TState state) => inner.ValidateMemoryState(state);
    public IModelCore<TState> Create(FieldBag config, ModelCreateOptions? options = null) =>
        new Core(this, inner.Create(config, options));

    private sealed class Core(SpyModel<TState> owner, IModelCore<TState> core) : IModelCore<TState>
    {
        public object Config => core.Config;
        public ModelBounds Bounds => core.Bounds;
        public object Algorithm => core.Algorithm;
        public TState Step(ModelStepInput<TState> input)
        {
            owner.Steps.Add(input);
            return core.Step(input);
        }

        public double NextInterval(TState memoryState, double desiredRetention)
        {
            owner.NextIntervals.Add((memoryState, desiredRetention));
            return owner.NextIntervalOverride is { } o ? o(memoryState, desiredRetention) : core.NextInterval(memoryState, desiredRetention);
        }

        public double ForgettingCurve(TState memoryState, double elapsedDays)
        {
            owner.ForgettingCurves++;
            return core.ForgettingCurve(memoryState, elapsedDays);
        }
        public IReadOnlyList<TState> Forward(IReadOnlyList<ModelReview> history, TState? initialState = null) =>
            core.Forward(history, initialState);
    }
}

/// <summary>A middleware whose handlers are supplied by the test (upstream <c>defineMiddleware({ handlers })</c>).</summary>
internal sealed class ProbeMiddleware<TState, TTime>(string name) : Middleware
    where TState : class
    where TTime : struct
{
    public Action<ReviewContext<TState, TTime>, Action>? OnReview { get; init; }
    public Action<NextIntervalContext<TState, TTime>, Action>? OnNextInterval { get; init; }
    public IReadOnlyList<FieldKey> Card { get; init; } = [];

    public override string Name => name;
    public override IReadOnlyList<FieldKey> CardFields => Card;

    public override void Review<TState2, TTime2>(ReviewContext<TState2, TTime2> context, Action next)
    {
        if (OnReview is not null && (object)context is ReviewContext<TState, TTime> typed) OnReview(typed, next);
        else next();
    }

    public override void NextInterval<TState2, TTime2>(NextIntervalContext<TState2, TTime2> context, Action next)
    {
        if (OnNextInterval is not null && (object)context is NextIntervalContext<TState, TTime> typed) OnNextInterval(typed, next);
        else next();
    }
}
