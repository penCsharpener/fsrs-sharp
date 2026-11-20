using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs6;
using FsrsSharp.Models.Fsrs7;

namespace FsrsSharp.Tests.Middlewares;

/// <summary>Shared fixtures for the fuzzing, monotonic-interval and cost-ADR middleware tests.</summary>
public static class FuzzMonotonicTestSchedulers
{
    public const double Day = 86_400_000;

    public static DateTime Now { get; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static DateTime Later { get; } = new(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

    public static FieldBag.Builder Fsrs6Config(bool enableShortTerm = false, int numRelearningSteps = 0) =>
        new FieldBag.Builder()
            .Set(Fsrs6Model.Weights, (IReadOnlyList<double>)Fsrs6Constants.DefaultWeights)
            .Set(Fsrs6Model.EnableShortTerm, enableShortTerm)
            .Set(Fsrs6Model.NumRelearningSteps, numRelearningSteps);

    public static FieldBag.Builder Fsrs7Config() =>
        new FieldBag.Builder().Set(Fsrs7Model.Weights, Fsrs7Constants.DefaultWeights);

    public static SchedulerDefinition<FsrsState, DateTime> Fsrs6Definition(params Middleware[] middlewares) =>
        SchedulerDefinition.Define(Fsrs6Model.Instance, DateTimeChrono.Instance).Use(middlewares);

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
}

/// <summary>An <see cref="IModel{TState}"/> decorator recording the calls made to the created model core (the C# counterpart of <c>vi.spyOn(core.model, ...)</c>).</summary>
public sealed class CostAdrSpyModel<TState>(IModel<TState> inner) : IModel<TState> where TState : class
{
    public List<ModelStepInput<TState>> Steps { get; } = [];
    public List<(TState State, double Retention)> NextIntervals { get; } = [];
    public int ForgettingCurves { get; private set; }

    public string Name => inner.Name;
    public IReadOnlyList<FieldKey> ConfigKeys => inner.ConfigKeys;
    public FieldBag ParseConfig(FieldBag config) => inner.ParseConfig(config);
    public TState DefaultMemoryState(FieldBag config) => inner.DefaultMemoryState(config);
    public string? ValidateMemoryState(TState state) => inner.ValidateMemoryState(state);
    public IModelCore<TState> Create(FieldBag config, ModelCreateOptions? options = null) => new Core(this, inner.Create(config, options));

    public void Clear()
    {
        Steps.Clear();
        NextIntervals.Clear();
        ForgettingCurves = 0;
    }

    private sealed class Core(CostAdrSpyModel<TState> spy, IModelCore<TState> core) : IModelCore<TState>
    {
        public object Config => core.Config;
        public ModelBounds Bounds => core.Bounds;
        public object Algorithm => core.Algorithm;

        public TState Step(ModelStepInput<TState> input)
        {
            spy.Steps.Add(input);
            return core.Step(input);
        }

        public double NextInterval(TState memoryState, double desiredRetention)
        {
            spy.NextIntervals.Add((memoryState, desiredRetention));
            return core.NextInterval(memoryState, desiredRetention);
        }

        public double ForgettingCurve(TState memoryState, double elapsedDays)
        {
            spy.ForgettingCurves++;
            return core.ForgettingCurve(memoryState, elapsedDays);
        }

        public IReadOnlyList<TState> Forward(IReadOnlyList<ModelReview> history, TState? initialState = null) =>
            core.Forward(history, initialState);
    }
}
